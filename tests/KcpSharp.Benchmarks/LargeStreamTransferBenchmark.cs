using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace KcpSharp.Benchmarks
{
    [MemoryDiagnoser]
    public class LargeStreamTransferBenchmark
    {
        private const int MTU = 1400;
        private const int fileSize = 16 * 1024 * 1024; // 16MB

        [Params(128)]
        public int WindowSize { get; set; }

        [Params(512)]
        public int QueueSize { get; set; }

        [Params(50)]
        public int UpdateInterval { get; set; }

        [Params(512)]
        public int PipeCapacity { get; set; }

        private readonly PreallocatedBufferPool _bufferPool = new(MTU);
        private PerfectKcpConversationPipe? _pipe;
        private byte[] _sendBuffer = new byte[16384];
        private byte[] _receiveBuffer = new byte[16384];

        [GlobalSetup]
        public void Setup()
        {
            _bufferPool.Fill(WindowSize * 2 + QueueSize * 2 + PipeCapacity * 2);
            _pipe = new PerfectKcpConversationPipe(_bufferPool, MTU, PipeCapacity, new KcpConversationOptions
            {
                Mtu = MTU,
                UpdateInterval = UpdateInterval,
                StreamMode = true,
                SendQueueSize = QueueSize,
                ReceiveWindow = QueueSize,
                SendWindow = WindowSize,
                RemoteReceiveWindow = WindowSize,
                DisableCongestionControl = true
            });
        }

        [Benchmark]
        public Task SendLargeFileAsync()
        {
            Task sendTask = SendAll(_pipe!.Alice, fileSize, _sendBuffer, CancellationToken.None);
            Task receiveTask = ReceiveAll(_pipe!.Bob, fileSize, _receiveBuffer, CancellationToken.None);
            return Task.WhenAll(sendTask, receiveTask);

            static async Task SendAll(KcpConversation conversation, int length, byte[] buffer, CancellationToken cancellationToken)
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

            static async Task ReceiveAll(KcpConversation conversation, int length, byte[] buffer, CancellationToken cancellationToken)
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
}
