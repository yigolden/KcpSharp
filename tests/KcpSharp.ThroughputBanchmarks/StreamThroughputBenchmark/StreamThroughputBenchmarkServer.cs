using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp.ThroughputBanchmarks.StreamThroughputBenchmark
{
    internal class StreamThroughputBenchmarkServer
    {
        public async Task RunAsync(string listen, int mtu, int windowSize, int updateInterval, bool noDelay, CancellationToken cancellationToken)
        {
            if (!IPEndPoint.TryParse(listen, out IPEndPoint? ipEndPoint))
            {
                throw new ArgumentException("endpoint is not a valid IPEndPoint.", nameof(listen));
            }
            if (mtu < 50 || mtu > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(mtu), "mtu is not valid.");
            }
            if (windowSize <= 0 || windowSize >= ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(windowSize), "windowSize is not valid.");
            }
            if (updateInterval <= 0 || updateInterval > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(updateInterval), "updateInterval is not valid.");
            }

            var allocator = new PinnedBlockMemoryPool(mtu);
            var options = new KcpConversationOptions
            {
                BufferPool = allocator,
                Mtu = mtu,
                SendWindow = windowSize,
                RemoteReceiveWindow = windowSize,
                UpdateInterval = updateInterval,
                NoDelay = noDelay,
                StreamMode = true
            };

            var socket = new Socket(ipEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            SocketHelper.PatchSocket(socket);
            if (ipEndPoint.Equals(IPAddress.IPv6Any))
            {
                socket.DualMode = true;
            }
            socket.Bind(ipEndPoint);

            var dispatcher = new UdpSocketServiceDispatcher<StreamThroughputBenchmarkService>(
                socket, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
                (sender, ep, state) => new StreamThroughputBenchmarkService(sender, ep, (KcpConversationOptions?)state!),
                (service, state) => service.Dispose(),
                options);
            await dispatcher.RunAsync(ipEndPoint, GC.AllocateUninitializedArray<byte>(mtu), cancellationToken);
        }
    }
}
