using System.Net;
using KcpEchoWithConnectionManagement.SocketTransport;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal sealed class KcpNetworkConnectionListenerConnectionState : IKcpNetworkTransport
    {
        private readonly KcpNetworkConnectionListener _listener;
        private readonly EndPoint _remoteEndPoint;
        private bool _transportClosed;
        private bool _disposed;
        private KcpNetworkConnection? _networkConnection;
        private SpinLock _lock;

        public KcpNetworkConnectionListenerConnectionState(KcpNetworkConnectionListener listener, EndPoint remoteEndPoint)
        {
            _listener = listener;
            _remoteEndPoint = remoteEndPoint;
        }

        public KcpNetworkConnection CreateNetworkConnection()
        {
            bool lockTaken = false;
            try
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(KcpNetworkConnectionListenerConnectionState));
                }
                if (_transportClosed)
                {
                    throw new InvalidOperationException();
                }
                if (_networkConnection is not null)
                {
                    return _networkConnection;
                }
                _networkConnection = new KcpNetworkConnection(this, true, _remoteEndPoint, _listener.GetConnectionOptions());
                return _networkConnection;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        public void SetTransportClosed()
        {
            bool lockTaken = false;
            try
            {
                if (_transportClosed)
                {
                    return;
                }
                _transportClosed = true;
                _networkConnection?.SetTransportClosed();
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        public void SetDisposed()
        {
            // This is called by the listener
            IDisposable? disposable = null;
            bool lockTaken = false;
            try
            {
                if (_disposed)
                {
                    return;
                }
                disposable = _networkConnection;
                _transportClosed = true;
                _disposed = true;
                _networkConnection = null;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }

            disposable?.Dispose();
        }

        public void Dispose()
        {
            // This is called by NetworkConnection
            bool lockTaken = false;
            try
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _networkConnection = null;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }

            _listener.NotifyDisposed(_remoteEndPoint, this);
        }

        public bool QueuePacket(ReadOnlySpan<byte> packet, EndPoint remoteEndPoint)
        {
            if (_transportClosed || _disposed)
            {
                return false;
            }
            return _listener.QueuePacket(packet, remoteEndPoint);
        }

        public ValueTask QueueAndSendPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            if (_transportClosed || _disposed)
            {
                return default;
            }
            return _listener.QueueAndSendPacketAsync(packet, remoteEndPoint, cancellationToken);
        }

        public ValueTask InputPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            IKcpNetworkApplication? networkConnection;
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                if (_transportClosed || _disposed)
                {
                    return default;
                }
                networkConnection = _networkConnection;
                if (networkConnection is null)
                {
                    networkConnection = _networkConnection = new KcpNetworkConnection(this, true, _remoteEndPoint, _listener.GetConnectionOptions());
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }

            return networkConnection.InputPacketAsync(packet, remoteEndPoint, cancellationToken);
        }

    }
}
