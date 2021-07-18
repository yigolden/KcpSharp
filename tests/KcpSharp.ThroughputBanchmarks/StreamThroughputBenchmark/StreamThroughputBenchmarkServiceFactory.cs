using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace KcpSharp.ThroughputBanchmarks.StreamThroughputBenchmark
{
    internal sealed class StreamThroughputBenchmarkServiceFactory : UdpSocketDispatcherOptions<StreamThroughputBenchmarkService>
    {
        private readonly KcpConversationOptions _options;

        public StreamThroughputBenchmarkServiceFactory(KcpConversationOptions options)
        {
            _options = options;
        }

        public override TimeSpan KeepAliveInterval => TimeSpan.FromMinutes(2);
        public override TimeSpan ScanInterval => TimeSpan.FromMinutes(2);

        public override StreamThroughputBenchmarkService Activate(IUdpServiceDispatcher dispatcher, EndPoint endpoint)
            => new StreamThroughputBenchmarkService(dispatcher, endpoint, _options);

        public override void Close(StreamThroughputBenchmarkService service)
            => service.Dispose();
    }
}
