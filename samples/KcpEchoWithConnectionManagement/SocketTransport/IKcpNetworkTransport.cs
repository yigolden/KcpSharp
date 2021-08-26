using System.Net;

namespace KcpEchoWithConnectionManagement.SocketTransport
{
    public interface IKcpNetworkTransport : IDisposable
    {
        ValueTask SendPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken);
    }
}
