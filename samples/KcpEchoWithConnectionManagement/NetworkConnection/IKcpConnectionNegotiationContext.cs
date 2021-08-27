using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public interface IKcpConnectionNegotiationContext
    {
        bool TryGetSessionId(out uint sessionId);
        void SetSessionId(uint sessionId);
        KcpConnectionNegotiationResult PutNegotiationData(ReadOnlySpan<byte> data);
        KcpConnectionNegotiationResult GetNegotiationData(Span<byte> data);
    }
}
