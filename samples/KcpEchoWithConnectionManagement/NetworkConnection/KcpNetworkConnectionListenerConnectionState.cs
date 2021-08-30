using System.Net;
using KcpEchoWithConnectionManagement.SocketTransport;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal sealed class KcpNetworkConnectionListenerConnectionState : IKcpNetworkTransport
    {
        private readonly KcpNetworkConnectionListener _listener;
        private readonly EndPoint _remoteEndPoint;
        private bool _disposed;
        private KcpNetworkConnection? _networkConnection;

        public KcpNetworkConnectionListenerConnectionState(KcpNetworkConnectionListener listener, EndPoint remoteEndPoint)
        {
            _listener = listener;
            _remoteEndPoint = remoteEndPoint;
        }

        public KcpNetworkConnection CreateNetworkConnection()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(KcpNetworkConnectionListenerConnectionState));
            }
            if (_networkConnection is not null)
            {
                return _networkConnection;
            }
            _networkConnection = new KcpNetworkConnection(this, true, _remoteEndPoint, null);
            return _networkConnection;
        }

        public void SetDisposed()
        {
            // This is called by the listener
            if (_networkConnection is not null)
            {
                _networkConnection.Dispose();
            }
        }

        public void Dispose()
        {
            // This is called by NetworkConnection
            if (_disposed)
            {
                return;
            }
            _networkConnection = null;
        }

        bool IKcpNetworkTransport.QueuePacket(ReadOnlySpan<byte> packet, EndPoint remoteEndPoint)
        {
            if (_disposed)
            {
                return false;
            }
            return ((IKcpNetworkTransport)_listener).QueuePacket(packet, remoteEndPoint);
        }

        ValueTask IKcpNetworkTransport.QueueAndSendPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return default;
            }
            return ((IKcpNetworkTransport)_listener).QueueAndSendPacketAsync(packet, remoteEndPoint, cancellationToken);
        }

    }
}
