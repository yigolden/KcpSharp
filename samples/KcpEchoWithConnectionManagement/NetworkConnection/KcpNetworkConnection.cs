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
                socketTransport.Start(networkConnection, remoteEndPoint);
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

        public async ValueTask NegotiateAsync(IKcpConnectionAuthenticationContext authenticationContext, CancellationToken cancellationToken)
        {
            if (_state != KcpNetworkConnectionState.None)
            {
                ThrowInvalidOperationException();
            }

            Debug.Assert(_negotiationOperation is null);
            _state = KcpNetworkConnectionState.Connecting;

        }

        internal ValueTask SendRawPacket(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            // TODO should we send?
            return _transport.SendPacketAsync(packet, _remoteEndPoint, cancellationToken);
        }

        ValueTask IKcpNetworkApplication.InputPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken) => throw new NotImplementedException();

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
