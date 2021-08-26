using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public interface IKcpConnectionAuthenticationContext
    {
        ValueTask<KcpConnectionAuthenticationResult> PutAuthenticationDataAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
        ValueTask<KcpConnectionAuthenticationResult> GetAuthenticationDataAsync(Memory<byte> data, CancellationToken cancellationToken);
    }
}
