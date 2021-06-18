using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp.ThroughputBanchmarks.PacketsThroughputBenchmark
{
    internal class PacketsThroughputBenchmarkClient
    {
        private long _packetsTransmitted;

        public async Task RunAsync(string endpoint, int mtu, int concurrency, int packetSize, int windowSize, int queueSize, int updateInterval, bool noDelay, CancellationToken cancellationToken)
        {
            if (!IPEndPoint.TryParse(endpoint, out IPEndPoint? ipEndPoint))
            {
                throw new ArgumentException("endpoint is not a valid IPEndPoint.", nameof(endpoint));
            }
            if (mtu < 50 || mtu > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(mtu), "mtu is not valid.");
            }
            if (concurrency <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(concurrency), "concurrency is not valid.");
            }
            if (packetSize < 0 || packetSize > (mtu - 24))
            {
                throw new ArgumentOutOfRangeException(nameof(packetSize), "packetSize is not valid.");
            }
            if (windowSize <= 0 || windowSize >= ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(windowSize), "windowSize is not valid.");
            }
            if (queueSize <= 0 || queueSize >= ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(queueSize), "queueSize is not valid.");
            }
            if (updateInterval <= 0 || updateInterval > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(updateInterval), "updateInterval is not valid.");
            }

            var allocator = new PinnedBlockMemoryPool(mtu);
            var options = new KcpConversationOptions()
            {
                BufferAllocator = allocator,
                Mtu = mtu,
                SendQueueSize = queueSize,
                SendWindow = windowSize,
                RemoteReceiveWindow = windowSize,
                UpdateInterval = updateInterval,
                NoDelay = noDelay,
            };

            _ = Task.Run(() => DisplayStats(packetSize, cancellationToken));

            var tasks = new Task[concurrency];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = RunSingleAsync(ipEndPoint, packetSize, options, cancellationToken);
            }

            Console.WriteLine($"Started {concurrency} tasks concurrently.");
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Do nothing
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private async Task RunSingleAsync(IPEndPoint ipEndPoint, int packetSize, KcpConversationOptions options, CancellationToken cancellationToken)
        {
            var socket = new Socket(ipEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            SocketHelper.PatchSocket(socket);
            await socket.ConnectAsync(ipEndPoint, cancellationToken);

            var transport = new SocketKcpTransport(socket, ipEndPoint);
            var conversation = new KcpConversation(transport, 0, options);
            transport.StartPumpPacketsToConversation(conversation, options.Mtu, cancellationToken);

            byte[] packet = new byte[packetSize];
            while (!cancellationToken.IsCancellationRequested)
            {
                await conversation.SendAsync(packet, cancellationToken);
                Interlocked.Increment(ref _packetsTransmitted);
            }
        }

        private async Task DisplayStats(int packetSize, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(30 * 1000, cancellationToken);

                long packetsTransmitted = Interlocked.Exchange(ref _packetsTransmitted, 0);
                long bytesTransferred = packetsTransmitted * packetSize;
                Console.WriteLine($"{DateTime.Now:O}: {packetsTransmitted} packets transmitted. size: {SizeSuffix(bytesTransferred)}. speed: {SizeSuffix(bytesTransferred / 30)}/s.");
            }
        }

        // https://stackoverflow.com/questions/14488796/does-net-provide-an-easy-way-convert-bytes-to-kb-mb-gb-etc/14488941#14488941
        static readonly string[] SizeSuffixes =
                   { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        static string SizeSuffix(long value, int decimalPlaces = 1)
        {
            if (decimalPlaces < 0) { throw new ArgumentOutOfRangeException("decimalPlaces"); }
            if (value < 0) { return "-" + SizeSuffix(-value, decimalPlaces); }
            if (value == 0) { return string.Format("{0:n" + decimalPlaces + "} bytes", 0); }

            // mag is 0 for bytes, 1 for KB, 2, for MB, etc.
            int mag = (int)Math.Log(value, 1024);

            // 1L << (mag * 10) == 2 ^ (10 * mag) 
            // [i.e. the number of bytes in the unit corresponding to mag]
            decimal adjustedSize = (decimal)value / (1L << (mag * 10));

            // make adjustment when the value is large enough that
            // it would round up to 1000 or more
            if (Math.Round(adjustedSize, decimalPlaces) >= 1000)
            {
                mag += 1;
                adjustedSize /= 1024;
            }

            return string.Format("{0:n" + decimalPlaces + "} {1}",
                adjustedSize,
                SizeSuffixes[mag]);
        }
    }
}
