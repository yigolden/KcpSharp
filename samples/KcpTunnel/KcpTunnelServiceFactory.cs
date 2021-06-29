using System.Net;

namespace KcpTunnel
{
    internal class KcpTunnelServiceFactory : UdpSocketDispatcherOptions<KcpTunnelService>
    {
        private readonly KcpTunnelServiceOptions _options;

        public KcpTunnelServiceFactory(KcpTunnelServiceOptions options)
        {
            _options = options;
        }
        public override KcpTunnelService Activate(IUdpServiceDispatcher dispatcher, EndPoint endpoint)
        {
            var service = new KcpTunnelService(dispatcher, endpoint, _options);
            service.Start();
            return service;
        }

        public override void Close(KcpTunnelService service)
        {
            service.Stop();
        }
    }
}
