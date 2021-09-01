using System.Net;
using KcpEchoWithConnectionManagement.SocketTransport;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public sealed class KcpNetworkConnectionListener : IKcpNetworkApplication
    {
        private IKcpNetworkTransport? _transport;
        private bool _ownsTransport;
        private readonly KcpNetworkConnectionOptions _connectionOptions;

        private KcpNetworkConnectionAcceptQueue? _acceptQueue;
        private bool _transportClosed;
        private bool _disposed;

        public KcpNetworkConnectionListener(IKcpNetworkTransport transport, NetworkConnectionListenerOptions? options)
        {
            _transport = transport;
            _ownsTransport = false;
            _connectionOptions = new KcpNetworkConnectionOptions
            {
                BufferPool = options?.BufferPool,
                Mtu = options?.Mtu ?? 1400,
                NegotiationOperationPool = new KcpNetworkConnectionNegotiationOperationInfinitePool()
            };
            _acceptQueue = new KcpNetworkConnectionAcceptQueue(options?.BackLog ?? 128);
        }

        internal KcpNetworkConnectionListener(IKcpNetworkTransport transport, bool ownsTransport, NetworkConnectionListenerOptions? options)
        {
            _transport = transport;
            _ownsTransport = ownsTransport;
            _connectionOptions = new KcpNetworkConnectionOptions
            {
                BufferPool = options?.BufferPool,
                Mtu = options?.Mtu ?? 1400,
                NegotiationOperationPool = new KcpNetworkConnectionNegotiationOperationInfinitePool()
            };
            _acceptQueue = new KcpNetworkConnectionAcceptQueue(options?.BackLog ?? 128);
        }

        public static KcpNetworkConnectionListener Listen(EndPoint localEndPoint, EndPoint remoteEndPoint, int sendQueueSize, NetworkConnectionListenerOptions? options)
        {
            KcpSocketNetworkTransport? transport = new KcpSocketNetworkTransport(options?.Mtu ?? 1400, options?.BufferPool);
            KcpNetworkConnectionListener? listener = null;
            try
            {
                transport.Bind(localEndPoint);

                listener = new KcpNetworkConnectionListener(transport, true, options);

                transport.RegisterFallback(listener);

                transport.Start(remoteEndPoint, sendQueueSize);

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
            if (_transportClosed || _disposed)
            {
                return default;
            }

            IKcpNetworkTransport? transport = Volatile.Read(ref _transport);
            if (transport is null)
            {
                return default;
            }

            KcpNetworkConnectionAcceptQueue? acceptQueue = Volatile.Read(ref _acceptQueue);
            if (acceptQueue is null || !acceptQueue.IsQueueAvailable())
            {
                return default;
            }

            var networkConnection = new KcpNetworkConnection(transport, false, remoteEndPoint, _connectionOptions);

            try
            {
                KcpSocketNetworkApplicationRegistration registration = transport.Register(remoteEndPoint, networkConnection);
                networkConnection.SetApplicationRegistration(registration);
            }
            catch
            {
                networkConnection.Dispose();
                return default;
            }

            if (!acceptQueue.TryQueue(networkConnection))
            {
                networkConnection.Dispose();
                return default;
            }

            return ((IKcpNetworkApplication)networkConnection).InputPacketAsync(packet, remoteEndPoint, cancellationToken);
        }

        public void SetTransportClosed()
        {
            if (_transportClosed)
            {
                return;
            }
            _transportClosed = true;

            KcpNetworkConnectionAcceptQueue? acceptQueue = Interlocked.Exchange(ref _acceptQueue, null);
            if (acceptQueue is not null)
            {
                acceptQueue.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            SetTransportClosed();

            IKcpNetworkTransport? transport = Interlocked.Exchange(ref _transport, null);
            if (transport is not null && _ownsTransport)
            {
                _ownsTransport = false;
                transport.Dispose();
            }
        }

    }
}
