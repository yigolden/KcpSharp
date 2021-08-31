using System.Net;

namespace KcpEchoWithConnectionManagement.SocketTransport
{
    public interface IKcpNetworkTransport : IDisposable
    {
        KcpSocketNetworkApplicationRegistration Register(EndPoint remoteEndPoint, IKcpNetworkApplication application);
        bool QueuePacket(ReadOnlySpan<byte> packet, EndPoint remoteEndPoint);
        ValueTask QueueAndSendPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken);
    }
}
