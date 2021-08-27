using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using KcpEchoWithConnectionManagement.SocketTransport;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal sealed class KcpNetworkConnection : IKcpNetworkApplication
    {
        private readonly IKcpNetworkTransport _transport;
        private bool _ownsTransport;
        private KcpNetworkConnectionNegotiationOperationPool? _negotiationOperationPool;
        private readonly IKcpBufferPool _bufferPool;
        private readonly int _mtu;
        private readonly EndPoint _remoteEndPoint;
        private KcpNetworkConnectionState _state;

        private KcpNetworkConnectionNegotiationOperation? _negotiationOperation;

        public KcpNetworkConnection(IKcpNetworkTransport transport, bool ownsTransport, EndPoint remoteEndPoint, KcpNetworkConnectionOptions? options)
        {
            _transport = transport;
            _ownsTransport = ownsTransport;
            _bufferPool = options?.BufferPool ?? DefaultBufferPool.Instance;
            _negotiationOperationPool = options?.NegotiationOperationPool;
            _mtu = options?.Mtu ?? 1400;
            _remoteEndPoint = remoteEndPoint;
        }

        internal KcpRentedBuffer AllocateMtu()
        {
            return _bufferPool.Rent(new KcpBufferPoolRentOptions(_mtu, false));
        }

        public static async Task ConnectAsync(EndPoint remoteEndPoint, KcpNetworkConnectionOptions? options, CancellationToken cancellationToken)
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
            }
            finally
            {
                if (networkConnection is not null)
                {
                    networkConnection.Dispose();
                }
                else if (socketTransport is not null)
                {
                    socketTransport.Dispose();
                }
            }
        }

        public ValueTask<bool> NegotiateAsync(IKcpConnectionNegotiationContext negotiationContext, CancellationToken cancellationToken)
        {
            if (_state != KcpNetworkConnectionState.None)
            {
                ThrowInvalidOperationException();
            }

            Debug.Assert(_negotiationOperation is null);
            _state = KcpNetworkConnectionState.Connecting;
            KcpNetworkConnectionNegotiationOperationPool pool = _negotiationOperationPool ?? new KcpNetworkConnectionNegotiationOperationPool();
            _negotiationOperation = pool.Rent(_bufferPool, this, negotiationContext);
            return _negotiationOperation.NegotiateAsync(cancellationToken);
        }

        internal bool QueueRawPacket(ReadOnlySpan<byte> packet)
        {
            return _transport.QueuePacket(packet, _remoteEndPoint);
        }

        ValueTask IKcpNetworkApplication.InputPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            if (packet.Length < 4)
            {
                return default;
            }

            if (_state == KcpNetworkConnectionState.Connecting)
            {
                ReadOnlySpan<byte> packetSpan = packet.Span;
                if (!packetSpan.IsEmpty && packetSpan[0] == 1)
                {
                    _negotiationOperation?.InputPacket(packetSpan);
                }
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

        }

        [DoesNotReturn]
        private static void ThrowInvalidOperationException()
        {
            throw new InvalidOperationException();
        }


    }
}
