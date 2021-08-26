using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace KcpEchoWithConnectionManagement.SocketTransport
{
    public interface IKcpNetworkApplication
    {
        ValueTask InputPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken);
        void SetTransportClosed();
    }
}
