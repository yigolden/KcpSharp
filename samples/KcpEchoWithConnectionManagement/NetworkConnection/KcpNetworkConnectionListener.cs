using System.Collections.Concurrent;
using System.Net;
using KcpEchoWithConnectionManagement.SocketTransport;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public sealed class KcpNetworkConnectionListener : IKcpNetworkApplication, IKcpNetworkTransport, IDisposable
    {
        private readonly IKcpNetworkTransport _transport;
        private bool _ownsTransport;

        private ConcurrentDictionary<EndPoint, KcpNetworkConnectionListenerConnectionState> _connections = new();

        public KcpNetworkConnectionListener(IKcpNetworkTransport transport, bool ownsTransport)
        {
            _transport = transport;
            _ownsTransport = ownsTransport;
        }

        public static KcpNetworkConnectionListener Listen(EndPoint localEndPoint, EndPoint remoteEndPoint, NetworkConnectionListenerOptions? options)
        {
            KcpSocketNetworkTransport? transport = new KcpSocketNetworkTransport(options?.Mtu ?? 1400, options?.BufferPool);
            KcpNetworkConnectionListener? listener = null;
            try
            {
                // bind to local port
                transport.Bind(localEndPoint);

                // setup connection
                listener = new KcpNetworkConnectionListener(transport, true);

                // start pumping data
                transport.Start(listener, remoteEndPoint, options?.SendQueueSize ?? 1024);

                transport = null;
                return Interlocked.Exchange<KcpNetworkConnectionListener?>(ref listener, null);
            }
            finally
            {
                listener?.Dispose();
                transport?.Dispose();
            }
        }

        ValueTask IKcpNetworkApplication.InputPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            if (_connections.TryGetValue(remoteEndPoint, out KcpNetworkConnectionListenerConnectionState? connectionState))
            {
                return default;
            }
            return default;
        }

        void IKcpNetworkApplication.SetTransportClosed() => throw new NotImplementedException();

        bool IKcpNetworkTransport.QueuePacket(ReadOnlySpan<byte> packet, EndPoint remoteEndPoint) => _transport.QueuePacket(packet, remoteEndPoint);
        ValueTask IKcpNetworkTransport.QueueAndSendPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken) => _transport.QueueAndSendPacketAsync(packet, remoteEndPoint, cancellationToken);

        public void Dispose()
        {

        }


    }
}
