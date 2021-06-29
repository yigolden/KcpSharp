using System.Net;

namespace KcpTunnel
{
    internal sealed record KcpTunnelServiceOptions
    {
        public int Mtu { get; init; }
        public EndPoint? ForwardEndPoint { get; init; }
    }
}
