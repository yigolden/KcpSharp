using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public class KcpNetworkConnectionOptions
    {
        public IKcpBufferPool? BufferPool { get; set; }
        public int Mtu { get; set; }
    }
}
