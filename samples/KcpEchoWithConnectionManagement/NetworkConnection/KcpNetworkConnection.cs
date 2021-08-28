using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using KcpEchoWithConnectionManagement.SocketTransport;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public sealed class KcpNetworkConnection : IKcpNetworkApplication
    {
        private readonly IKcpNetworkTransport _transport;
        private bool _ownsTransport;
        private KcpNetworkConnectionNegotiationOperationPool? _negotiationOperationPool;
        private readonly IKcpBufferPool _bufferPool;
        private int _mtu;
        private readonly EndPoint _remoteEndPoint;
        private readonly KcpNetworkConnectionCallbackManagement _callbackManagement;
        private KcpNetworkConnectionState _state;

        private KcpNetworkConnectionNegotiationOperation? _negotiationOperation;
        private KcpNetworkConnectionKeepAliveHandler? _keepAliveHandler;

        private uint _nextLocalSerial;

        private long _lastActiveTimeTicks;
        private SpinLock _remoteStatisticsLock;
        private uint _nextRemoteSerial;
        private uint _packetsReceived;

        public int Mtu => _mtu;

        public const int PreBufferSize = 8;

        public KcpNetworkConnection(IKcpNetworkTransport transport, bool ownsTransport, EndPoint remoteEndPoint, KcpNetworkConnectionOptions? options)
        {
            _transport = transport;
            _ownsTransport = ownsTransport;
            _bufferPool = options?.BufferPool ?? DefaultBufferPool.Instance;
            _negotiationOperationPool = options?.NegotiationOperationPool;
            _mtu = options?.Mtu ?? 1400;
            _remoteEndPoint = remoteEndPoint;
            _callbackManagement = new KcpNetworkConnectionCallbackManagement();
        }

        public static async Task<KcpNetworkConnection> ConnectAsync(EndPoint remoteEndPoint, KcpNetworkConnectionOptions? options, CancellationToken cancellationToken)
        {
            KcpSocketNetworkTransport? socketTransport = new KcpSocketNetworkTransport(options?.Mtu ?? 1400, options?.BufferPool);
            KcpNetworkConnection? networkConnection = null;
            try
            {
                // connect to the remote host
                await socketTransport.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);

                // setup connection
                networkConnection = new KcpNetworkConnection(socketTransport, true, remoteEndPoint, options);

                // start pumping data
                socketTransport.Start(networkConnection, remoteEndPoint, options?.SendQueueSize ?? 1024);

                socketTransport = null;
                return Interlocked.Exchange<KcpNetworkConnection?>(ref networkConnection, null);
            }
            finally
            {
                networkConnection?.Dispose();
                socketTransport?.Dispose();
            }
        }

        public static KcpNetworkConnection Bind(EndPoint localEndPoint, EndPoint remoteEndPoint, KcpNetworkConnectionOptions? options)
        {
            KcpSocketNetworkTransport? socketTransport = new KcpSocketNetworkTransport(options?.Mtu ?? 1400, options?.BufferPool);
            KcpNetworkConnection? networkConnection = null;
            try
            {
                // bind to local port
                socketTransport.Bind(localEndPoint);

                // setup connection
                networkConnection = new KcpNetworkConnection(socketTransport, true, remoteEndPoint, options);

                // start pumping data
                socketTransport.Start(networkConnection, remoteEndPoint, options?.SendQueueSize ?? 1024);

                socketTransport = null;
                return Interlocked.Exchange<KcpNetworkConnection?>(ref networkConnection, null);
            }
            finally
            {
                networkConnection?.Dispose();
                socketTransport?.Dispose();
            }
        }

        public ValueTask<bool> NegotiateAsync(IKcpConnectionNegotiationContext negotiationContext, CancellationToken cancellationToken)
        {
            SwitchToConnectingState();
            Debug.Assert(_negotiationOperation is null);
            if (_negotiationOperationPool is null)
            {
                _negotiationOperation = new KcpNetworkConnectionNegotiationOperation(null);
                _negotiationOperation.Initialize(this, negotiationContext);
            }
            else
            {
                KcpNetworkConnectionNegotiationOperationPool pool = _negotiationOperationPool ?? new KcpNetworkConnectionNegotiationOperationPool();
                _negotiationOperation = pool.Rent(this, negotiationContext);
            }
            return _negotiationOperation.NegotiateAsync(cancellationToken);
        }

        public void SkipNegotiation()
        {
            Interlocked.Exchange(ref _lastActiveTimeTicks, DateTime.UtcNow.ToBinary());
            SwitchToConnectedState();
        }

        internal IKcpBufferPool GetAllocator() => _bufferPool;

        internal bool QueueRawPacket(ReadOnlySpan<byte> packet)
        {
            return _transport.QueuePacket(packet, _remoteEndPoint);
        }

        internal void NotifyNegotiationResult(KcpNetworkConnectionNegotiationOperation operation, bool success, int? negotiatedMtu)
        {
            Interlocked.CompareExchange(ref _negotiationOperation, null, operation);
            if (_state != KcpNetworkConnectionState.Connecting)
            {
                return;
            }
            if (negotiatedMtu.HasValue)
            {
                _mtu = negotiatedMtu.GetValueOrDefault();
            }
            if (success)
            {
                Interlocked.Exchange(ref _lastActiveTimeTicks, DateTime.UtcNow.ToBinary());
                SwitchToConnectedState();
            }
            else
            {
                SwitchToFailedState();
            }
        }

        public void SetupKeepAlive(TimeSpan interval)
            => SetupKeepAliveCore(null, interval);

        public void SetupKeepAlive(IKcpConnectionKeepAliveContext keepAliveContext, TimeSpan interval)
            => SetupKeepAliveCore(keepAliveContext, interval);

        private void SetupKeepAliveCore(IKcpConnectionKeepAliveContext? keepAliveContext, TimeSpan? interval)
        {
            if (_state != KcpNetworkConnectionState.Connected)
            {
                ThrowInvalidOperationException();
            }
            if (_keepAliveHandler is not null)
            {
                ThrowInvalidOperationException();
            }
            _keepAliveHandler = new KcpNetworkConnectionKeepAliveHandler(this, keepAliveContext, interval);
        }

        public void SendKeepAlive()
        {
            if (_state != KcpNetworkConnectionState.Connected)
            {
                return;
            }
            _keepAliveHandler?.Send();
        }

        internal (uint nextRemoteSerial, uint packetsReceived) GatherPacketStatistics()
        {
            bool lockTaken = false;
            try
            {
                _remoteStatisticsLock.Enter(ref lockTaken);

                uint nextRemoteSerial = _nextRemoteSerial;
                uint packetsReceived = _packetsReceived;
                _packetsReceived = 0;

                return (nextRemoteSerial, packetsReceived);
            }
            finally
            {
                if (lockTaken)
                {
                    _remoteStatisticsLock.Exit();
                }
            }
        }

        private void SwitchToConnectingState()
        {
            if (_state != KcpNetworkConnectionState.None)
            {
                ThrowInvalidOperationException();
            }
            _state = KcpNetworkConnectionState.Connecting;
            _callbackManagement.NotifyStateChanged(this);
        }

        private void SwitchToConnectedState()
        {
            if (_state != KcpNetworkConnectionState.None || _state != KcpNetworkConnectionState.Connecting)
            {
                ThrowInvalidOperationException();
            }
            _state = KcpNetworkConnectionState.Connected;
            _callbackManagement.NotifyStateChanged(this);
        }

        private void SwitchToFailedState()
        {
            if (_state != KcpNetworkConnectionState.None || _state != KcpNetworkConnectionState.Connecting)
            {
                ThrowInvalidOperationException();
            }
            _state = KcpNetworkConnectionState.Failed;
            _callbackManagement.NotifyStateChanged(this);
        }

        private void SwitchToDeadState()
        {
            _state = KcpNetworkConnectionState.Dead;
            _callbackManagement.NotifyStateChanged(this);
        }

        public ValueTask SendPacketWithPreBufferAsync(Memory<byte> packet, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled(cancellationToken);
            }
            if (packet.Length < PreBufferSize)
            {
                return default;
            }

            WriteDataPacketHeader(packet.Span, _nextLocalSerial++);
            return _transport.QueueAndSendPacketAsync(packet, _remoteEndPoint, cancellationToken);
        }

        private static void WriteDataPacketHeader(Span<byte> buffer, uint serial)
        {
            if (buffer.Length < PreBufferSize)
            {
                Debug.Fail("Invalid buffer.");
                return;
            }
            buffer[0] = 3;
            buffer[1] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2), (ushort)(buffer.Length - 4));
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(4), serial);
        }

        //private bool ProcessDataPacket(ReadOnlyMemory<byte> packet, )
        private bool TryParseDataPacketHeader(ReadOnlySpan<byte> packet, out ushort length, out uint serial)
        {
            if (packet.Length < 8 || packet[0] != 3 || packet[1] != 0)
            {
                length = 0;
                serial = 0;
                return false;
            }
            length = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2));
            serial = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4));
            if ((packet.Length - 4) < length)
            {
                return false;
            }
            return true;
        }


        ValueTask IKcpNetworkApplication.InputPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            if (packet.Length < 4)
            {
                return default;
            }

            bool? processResult = null;
            uint? remoteSerial = null;
            ReadOnlyMemory<byte> dataPayload = default;
            if (_state == KcpNetworkConnectionState.Connecting)
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
                    // payload0
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

        void IKcpNetworkApplication.SetTransportClosed() => throw new NotImplementedException();

        public void Dispose()
        {
            if (_ownsTransport)
            {
                IKcpNetworkTransport transport = _transport;
                _ownsTransport = false;
                transport.Dispose();
            }
            if (_negotiationOperation is not null)
            {
                _negotiationOperation.SetDisposed();
                _negotiationOperation = null;
            }
            if (_keepAliveHandler is not null)
            {
                _keepAliveHandler.Dispose();
                _keepAliveHandler = null;
            }
            if (_state != KcpNetworkConnectionState.Dead)
            {
                SwitchToDeadState();
            }
        }

        [DoesNotReturn]
        private static void ThrowInvalidOperationException()
        {
            throw new InvalidOperationException();
        }

    }
}
