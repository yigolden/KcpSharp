using System.Collections.Concurrent;
using System.Net;
using KcpEchoWithConnectionManagement.SocketTransport;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public sealed class KcpNetworkConnectionListener : IKcpNetworkApplication, IKcpNetworkTransport, IDisposable
    {
        private readonly IKcpNetworkTransport _transport;
        private bool _ownsTransport;
        private readonly NetworkConnectionListenerOptions? _options;

        private readonly ConcurrentDictionary<EndPoint, KcpNetworkConnectionListenerConnectionState> _connections = new();
        private readonly KcpNetworkConnectionAcceptQueue _acceptQueue;

        public KcpNetworkConnectionListener(IKcpNetworkTransport transport, bool ownsTransport, NetworkConnectionListenerOptions? options)
        {
            _transport = transport;
            _ownsTransport = ownsTransport;
            _options = options;
            _acceptQueue = new KcpNetworkConnectionAcceptQueue(options?.BackLog ?? 128);
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
                listener = new KcpNetworkConnectionListener(transport, true, options);

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
                return connectionState.InputPacketAsync(packet, remoteEndPoint, cancellationToken);
            }

            if (!_acceptQueue.IsQueueAvailable())
            {
                return default;
            }

            // TODO create and add to dictionary

            // add to accept queue
            // forward packet
            return default;
        }

        void IKcpNetworkApplication.SetTransportClosed() => throw new NotImplementedException();

        bool IKcpNetworkTransport.QueuePacket(ReadOnlySpan<byte> packet, EndPoint remoteEndPoint) => _transport.QueuePacket(packet, remoteEndPoint);
        ValueTask IKcpNetworkTransport.QueueAndSendPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken) => _transport.QueueAndSendPacketAsync(packet, remoteEndPoint, cancellationToken);

        public void Dispose()
        {
            if (_ownsTransport)
            {
                _transport.Dispose();
                _ownsTransport = false;
            }
        }


    }
}
