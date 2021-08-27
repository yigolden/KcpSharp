using System.Net;

namespace KcpEchoWithConnectionManagement.SocketTransport
{
    public interface IKcpNetworkTransport : IDisposable
    {
        bool QueuePacket(ReadOnlySpan<byte> packet, EndPoint remoteEndPoint);
        ValueTask QueueAndSendPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken);
    }
}
