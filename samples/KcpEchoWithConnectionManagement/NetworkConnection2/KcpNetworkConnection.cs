using System.Net;
using KcpEchoWithConnectionManagement.SocketTransport;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.NetworkConnection2
{
    public class KcpNetworkConnection : IKcpNetworkApplication
    {
        private readonly IKcpNetworkTransport _transport;
        private bool _ownsTransport;
        private readonly EndPoint _remoteEndPoint;
        private readonly IKcpBufferPool _bufferPool;

        private bool _transportClosed;
        private bool _disposed;

        private KcpNetworkConnectionState _state;
        private SpinLock _stateChangeLock;
        private KcpNetworkConnectionCallbackManagement _callbackManagement = new();

        private object _negotiationLock = new();
        private KcpRentedBuffer _cachedNegotiationPacket;


        public KcpNetworkConnectionState State => _state;

        public KcpNetworkConnection(IKcpNetworkTransport transport, EndPoint remoteEndPoint, KcpNetworkConnectionOptions? options)
        {
            _transport = transport;
            _ownsTransport = false;
            _remoteEndPoint = remoteEndPoint;
            _bufferPool = options?.BufferPool ?? DefaultBufferPool.Instance;
        }

        internal IKcpBufferPool GetAllocator() => _bufferPool;

        ValueTask IKcpNetworkApplication.InputPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            // TODO other validation
            if (packet.Length < 4)
            {
                return default;
            }
            if (!_remoteEndPoint.Equals(remoteEndPoint))
            {
                return default;
            }

            // TODO process disposed
            if (_disposed || _transportClosed)
            {
                return default;
            }

            bool? processResult = null;
            uint? remoteSerial = null;
            ReadOnlyMemory<byte> dataPayload = default;
            if (_state == KcpNetworkConnectionState.None)
            {
                // cache the initial packet for negotiation
                lock (_negotiationLock)
                {
                    if (_disposed || _transportClosed)
                    {
                        return default;
                    }
                    if (_cachedNegotiationPacket.IsAllocated)
                    {
                        KcpRentedBuffer rentedBuffer = _bufferPool.Rent(new KcpBufferPoolRentOptions(packet.Length, false));
                        packet.Span.CopyTo(rentedBuffer.Span);
                        _cachedNegotiationPacket = rentedBuffer.Slice(0, packet.Length);
                    }
                }
            }
            else if (_state == KcpNetworkConnectionState.Connecting)
            {
                ReadOnlySpan<byte> packetSpan = packet.Span;
                if (packetSpan[0] == 1)
                {
                    processResult = _negotiationOperation?.InputPacket(packetSpan);
                }
            }
            else if (_state == KcpNetworkConnectionState.Connected)
            {
                ReadOnlySpan<byte> packetSpan = packet.Span;
                if (packetSpan[0] == 2)
                {
                    processResult = _keepAliveHandler?.ProcessKeepAlivePacket(packetSpan);
                }
                else if (packetSpan[0] == 3)
                {
                    // payload
                    if (TryParseDataPacketHeader(packetSpan, out ushort length, out uint serial))
                    {
                        dataPayload = packet.Slice(8, length - 4);
                        remoteSerial = serial;
                    }
                }
            }

            if (processResult.GetValueOrDefault())
            {
                Interlocked.Exchange(ref _lastActiveTimeTicks, DateTime.UtcNow.ToBinary());
            }

            if (remoteSerial.HasValue)
            {
                bool lockTaken = false;
                try
                {
                    _remoteStatisticsLock.Enter(ref lockTaken);

                    if (remoteSerial.GetValueOrDefault() >= _nextRemoteSerial)
                    {
                        _nextRemoteSerial = remoteSerial.GetValueOrDefault() + 1;
                    }
                    _packetsReceived++;
                }
                finally
                {
                    if (lockTaken)
                    {
                        _remoteStatisticsLock.Exit();
                    }
                }
            }

            if (!dataPayload.IsEmpty)
            {
                return _callbackManagement.PacketReceivedAsync(dataPayload, cancellationToken);
            }

            return default;
        }

        public void SetTransportClosed()
        {
            if (_transportClosed)
            {
                return;
            }
            _transportClosed = true;

            ChangeStateTo(KcpNetworkConnectionState.Dead);

            lock (_negotiationLock)
            {
                if (_cachedNegotiationPacket.IsAllocated)
                {
                    _cachedNegotiationPacket.Dispose();
                    _cachedNegotiationPacket = default;
                }
            }
        }

        private void CheckAndChangeStateTo(KcpNetworkConnectionState expectedState, KcpNetworkConnectionState newState)
        {
            bool lockTaken = false;
            try
            {
                _stateChangeLock.Enter(ref lockTaken);

                if (_state != expectedState)
                {
                    throw new InvalidOperationException();
                }
                _state = newState;
            }
            finally
            {
                if (lockTaken)
                {
                    _stateChangeLock.Exit();
                }

            }

            _callbackManagement.NotifyStateChanged(this);
        }

        private void ChangeStateTo(KcpNetworkConnectionState newState)
        {
            bool lockTaken = false;
            try
            {
                _stateChangeLock.Enter(ref lockTaken);

                if (_state == newState)
                {
                    return;
                }
                _state = newState;
            }
            finally
            {
                if (lockTaken)
                {
                    _stateChangeLock.Exit();
                }

            }

            _callbackManagement.NotifyStateChanged(this);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            SetTransportClosed();

            if (_ownsTransport)
            {
                _transport.Dispose();
                _ownsTransport = false;
            }
        }


    }
}
