using System;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp
{
    public interface IKcpTransport
    {
        ValueTask SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
    }
}
