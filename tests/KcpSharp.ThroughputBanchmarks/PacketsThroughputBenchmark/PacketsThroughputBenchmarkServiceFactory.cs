using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace KcpSharp.ThroughputBanchmarks.PacketsThroughputBenchmark
{
    internal sealed class PacketsThroughputBenchmarkServiceFactory : UdpSocketDispatcherOptions<PacketsThroughputBenchmarkService>
    {
        private readonly KcpConversationOptions _options;

        public PacketsThroughputBenchmarkServiceFactory(KcpConversationOptions options)
        {
            _options = options;
        }

        public override TimeSpan KeepAliveInterval => TimeSpan.FromMinutes(2);
        public override TimeSpan ScanInterval => TimeSpan.FromMinutes(2);

        public override PacketsThroughputBenchmarkService Activate(IUdpServiceDispatcher dispatcher, EndPoint endpoint)
            => new PacketsThroughputBenchmarkService(dispatcher, endpoint, _options);
        public override void Close(PacketsThroughputBenchmarkService service)
            => service.Dispose();
    }
}
