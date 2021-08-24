using BenchmarkDotNet.Attributes;
using KcpSharp;

namespace KcpSimpleForwardErrorCorrection.Benchmarks
{
    [MemoryDiagnoser]
    public class DropRandomPacketBenchmarks
    {
        private const int MTU = 1400;
        private const int FileSize = 8 * 1024 * 1024; // 8MB

        private const int WindowSize = 64;
        private const int QueueSize = 256;
        private const int UpdateInterval = 30;
        private const int PipeCapacity = 512;

        private readonly PreallocatedBufferPool _bufferPool = new(MTU);
        private readonly byte[] _sendBuffer = new byte[16384];
        private readonly byte[] _receiveBuffer = new byte[16384];

        private readonly DropRandomPacketTransport _aliceToBobTransport;
        private readonly UnidirectionalTransport _bobToAliceTransport;

        private KcpConversationOptions _options;

        public DropRandomPacketBenchmarks()
        {
            _aliceToBobTransport = new DropRandomPacketTransport(_bufferPool, PipeCapacity, 42);
            _bobToAliceTransport = new UnidirectionalTransport(_bufferPool, PipeCapacity);
            _options = new KcpConversationOptions
            {
                BufferPool = _bufferPool,
                Mtu = MTU,
                UpdateInterval = UpdateInterval,
                StreamMode = true,
                SendQueueSize = QueueSize,
                ReceiveWindow = QueueSize,
                SendWindow = WindowSize,
                RemoteReceiveWindow = WindowSize,
                DisableCongestionControl = true,
                NoDelay = true
            };
        }

        [GlobalSetup]
        public void Setup()
        {
            _bufferPool.Fill(WindowSize * 2 + QueueSize * 2 + PipeCapacity * 2);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _aliceToBobTransport.Dispose();
            _bobToAliceTransport.Dispose();
        }

        /*
        [Benchmark]
        public Task SendWithoutFec()
        {
            return SendWithoutFec(passRate: 1);
        }

        [Benchmark]
        public Task SendWithRank5Fec100PassRate()
        {
            return SendWithFec(passRate: 1f,rank: 5);
        }
        */

        [Benchmark]
        public Task SendWithoutFec98PassRate()
        {
            return SendWithoutFec(passRate: 0.98f);
        }

        [Benchmark]
        public Task SendWithRank3Fec98PassRate()
        {
            return SendWithFec(passRate: 0.98f, rank: 3);
        }

        [Benchmark]
        public Task SendWithoutFec95PassRate()
        {
            return SendWithoutFec(passRate: 0.95f);
        }

        [Benchmark]
        public Task SendWithRank2Fec95PassRate()
        {
            return SendWithFec(passRate: 0.95f, rank: 2);
        }

        private async Task SendWithoutFec(float passRate)
        {
            _aliceToBobTransport.SetPassRate(passRate);

            using var alice = new KcpConversation(_aliceToBobTransport, _options);
            using var bob = new KcpConversation(_bobToAliceTransport, _options);

            _aliceToBobTransport.SetTarget(bob);
            _bobToAliceTransport.SetTarget(alice);

            Task sendTask = SendAll(alice, FileSize, _sendBuffer, CancellationToken.None);
            Task receiveTask = ReceiveAll(bob, FileSize, _receiveBuffer, CancellationToken.None);
            await Task.WhenAll(sendTask, receiveTask);
        }

        private async Task SendWithFec(float passRate, int rank)
        {
            _aliceToBobTransport.SetPassRate(passRate);

            using var aliceTransport = new KcpSimpleFecTransport(_aliceToBobTransport, null, _options, rank);
            using var bobTransport = new KcpSimpleFecTransport(_bobToAliceTransport, null, _options, rank);

            _aliceToBobTransport.SetTarget(bobTransport);
            _bobToAliceTransport.SetTarget(aliceTransport);

            aliceTransport.Start();
            bobTransport.Start();

            KcpConversation alice = aliceTransport.Connection;
            KcpConversation bob = bobTransport.Connection;

            Task sendTask = SendAll(alice, FileSize, _sendBuffer, CancellationToken.None);
            Task receiveTask = ReceiveAll(bob, FileSize, _receiveBuffer, CancellationToken.None);
            await Task.WhenAll(sendTask, receiveTask);
        }

        private static async Task SendAll(KcpConversation conversation, int length, byte[] buffer, CancellationToken cancellationToken)
        {
            int totalSentBytes = 0;
            while (totalSentBytes < length)
            {
                int sendSize = Math.Min(buffer.Length, length - totalSentBytes);
                if (!await conversation.SendAsync(buffer.AsMemory(0, sendSize), cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
                totalSentBytes += sendSize;
            }
            await conversation.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task ReceiveAll(KcpConversation conversation, int length, byte[] buffer, CancellationToken cancellationToken)
        {
            int totalReceivedBytes = 0;
            while (totalReceivedBytes < length)
            {
                KcpConversationReceiveResult result = await conversation.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.TransportClosed)
                {
                    break;
                }
                totalReceivedBytes += result.BytesReceived;
            }
        }
    }
}
