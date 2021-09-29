using System.Net;

namespace KcpEchoWithConnectionManagement.SocketTransport
{
    public interface IKcpNetworkApplication
    {
        ValueTask InputPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken);
        void SetTransportClosed();
    }
}
