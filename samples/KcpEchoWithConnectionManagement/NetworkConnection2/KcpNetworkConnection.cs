using System.Net;
using KcpEchoWithConnectionManagement.SocketTransport;

namespace KcpEchoWithConnectionManagement.NetworkConnection2
{
    public class KcpNetworkConnection : IKcpNetworkApplication
    {
        private readonly IKcpNetworkTransport _transport;
        private bool _ownsTransport;

        private bool _transportClosed;
        private bool _disposed;

        public KcpNetworkConnection(IKcpNetworkTransport transport)
        {
            _transport = transport;
            _ownsTransport = false;
        }

        ValueTask IKcpNetworkApplication.InputPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken) => throw new NotImplementedException();

        public void SetTransportClosed()
        {
            if (_transportClosed)
            {
                return;
            }
            _transportClosed = true;
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
