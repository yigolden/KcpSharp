using KcpSharp;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public class KcpNetworkConnectionOptions
    {
        public IKcpBufferPool? BufferPool { get; set; }
        internal KcpNetworkConnectionNegotiationOperationPool? NegotiationOperationPool { get; set; }
        public int Mtu { get; set; }
    }
}
