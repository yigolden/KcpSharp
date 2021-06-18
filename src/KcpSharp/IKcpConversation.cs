using System;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp
{
    public interface IKcpConversation : IDisposable
    {
        ValueTask OnReceivedAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
        void SetTransportClosed();
    }
}
