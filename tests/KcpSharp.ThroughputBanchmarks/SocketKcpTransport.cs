using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp.ThroughputBanchmarks
{
    internal sealed class SocketKcpTransport : IKcpTransport
    {
        private readonly Socket _socket;
        private readonly EndPoint _endPoint;

        public SocketKcpTransport(Socket socket, EndPoint endPoint)
        {
            _socket = socket;
            _endPoint = endPoint;
        }

        ValueTask IKcpTransport.SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
            => new ValueTask(_socket.SendToAsync(packet, SocketFlags.None, _endPoint, cancellationToken).AsTask());

        public void StartPumpPacketsToConversation(KcpConversation conversation, int mtu, CancellationToken cancellationToken)
        {
            byte[] buffer = GC.AllocateUninitializedArray<byte>(mtu, true);
            _ = Task.Run(() => PumpPacketsToConversationLoop(conversation, buffer, cancellationToken));
        }

        private async Task PumpPacketsToConversationLoop(KcpConversation conversation, byte[] buffer, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SocketReceiveFromResult result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, _endPoint, cancellationToken);
                await conversation.OnReceivedAsync(buffer.AsMemory(0, result.ReceivedBytes), cancellationToken);
            }
        }
    }
}
