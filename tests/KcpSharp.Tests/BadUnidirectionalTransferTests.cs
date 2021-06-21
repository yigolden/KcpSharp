using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KcpSharp.Tests
{
    public class BadUnidirectionalTransferTests
    {
        public static IEnumerable<object[]> GetMultiplePacketTestData()
        {
            int[] packetCounts = new int[] { 4, 10, 32 };
            (int min, int max)[] packetSizes = new (int min, int max)[]
            {
                (100, 200),
                (1200, 2400)
            };
            double[] drops = new double[] { 0.1 };
            int[] baseLatencies = new int[] { 0 };
            int[] randomDelays = new int[] { 20, 40 };

            foreach (int packetCount in packetCounts)
            {
                foreach ((int packetSizeMin, int packetSizeMax) in packetSizes)
                {
                    foreach (double drop in drops)
                    {
                        foreach (int latency in baseLatencies)
                        {
                            foreach (int delay in randomDelays)
                            {
                                yield return new object[] { packetCount, packetSizeMin, packetSizeMax, drop, latency, delay, false };
                                yield return new object[] { packetCount, packetSizeMin, packetSizeMax, drop, latency, delay, true };
                            }
                        }
                    }
                }
            }
        }

        [MemberData(nameof(GetMultiplePacketTestData))]
        [Theory]
        public Task TestMultiplePacketSendReceive(int packetCount, int minPacketSize, int maxPacketSize, double drop, int latency, int delay, bool waitToReceive)
        {
            Assert.True(minPacketSize <= maxPacketSize);
            List<byte[]> packets = new(
                Enumerable.Range(1, packetCount)
                .Select(i => new byte[Random.Shared.Next(minPacketSize, maxPacketSize + 1)])
                );
            foreach (byte[] packet in packets)
            {
                Random.Shared.NextBytes(packet);
            }

            KcpConversationOptions options = new() { SendQueueSize = 8, SendWindow = 12, ReceiveWindow = 12, UpdateInterval = 30 };
            var connectionOptions = new BadOneWayConnectionOptions { DropProbability = drop, BaseLatency = latency, RandomRelay = delay, ConcurrentCount = 12, Random = new Random(42) };
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(180), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreateBadPipe(connectionOptions, options);

                Task sendTask = SendMultplePacketsAsync(pipe.Alice, packets, cancellationToken);
                Task receiveTask = ReceiveMultiplePacketsAsync(pipe.Bob, packets, maxPacketSize, waitToReceive, cancellationToken);
                await Task.WhenAll(sendTask, receiveTask);

                await AssertReceiveNoDataAsync(pipe.Bob, waitToReceive);
            });

            static async Task SendMultplePacketsAsync(KcpConversation conversation, IEnumerable<byte[]> packets, CancellationToken cancellationToken)
            {
                foreach (byte[] packet in packets)
                {
                    Assert.True(await conversation.SendAsync(packet, cancellationToken));
                }
            }

            static async Task ReceiveMultiplePacketsAsync(KcpConversation conversation, IEnumerable<byte[]> packets, int maxPacketSize, bool waitToReceive, CancellationToken cancellationToken)
            {
                byte[] buffer = new byte[maxPacketSize];
                foreach (byte[] packet in packets)
                {
                    Assert.True(packet.Length <= buffer.Length);
                    KcpConversationReceiveResult result;
                    if (waitToReceive)
                    {
                        result = await conversation.WaitToReceiveAsync(cancellationToken);
                        Assert.False(result.TransportClosed, "Transport should not be closed.");
                        Assert.Equal(packet.Length, result.BytesReceived);
                    }
                    result = await conversation.ReceiveAsync(buffer, cancellationToken);
                    Assert.False(result.TransportClosed, "Transport should not be closed.");
                    Assert.Equal(packet.Length, result.BytesReceived);
                    Assert.True(buffer.AsSpan(0, result.BytesReceived).SequenceEqual(packet));
                }
            }
        }

        private static IEnumerable<object[]> GetBigFileSendReceiveTestData()
        {
            const int fileSize = 80 * 1024;
            int[] receiveBufferSizes = new int[] { 1000, 2000, 4096 };
            double[] drops = new double[] { 0.1 };
            int[] baseLatencies = new int[] { 0 };
            int[] randomDelays = new int[] { 20, 40 };

            foreach (int receiveBufferSize in receiveBufferSizes)
            {
                foreach (double drop in drops)
                {
                    foreach (int latency in baseLatencies)
                    {
                        foreach (int delay in randomDelays)
                        {
                            yield return new object[] { fileSize, drop, latency, delay, receiveBufferSize, false };
                            yield return new object[] { fileSize, drop, latency, delay, receiveBufferSize, true };
                        }
                    }
                }
            }
        }

        [MemberData(nameof(GetBigFileSendReceiveTestData))]
        [Theory]
        public Task TestBigFileStreamSendReceive(int fileSize, double drop, int latency, int delay, int receiveBufferSize, bool waitToReceive)
        {
            Assert.True(receiveBufferSize < fileSize);
            KcpConversationOptions options = new() { SendQueueSize = 32, SendWindow = 16, ReceiveWindow = 16, UpdateInterval = 30, StreamMode = true };
            var connectionOptions = new BadOneWayConnectionOptions { DropProbability = drop, BaseLatency = latency, RandomRelay = delay, ConcurrentCount = 12, Random = new Random(42) };
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(150), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreateBadPipe(connectionOptions, options);

                byte[] bigFile = new byte[fileSize];
                Random.Shared.NextBytes(bigFile);
                Task<bool> sendTask = Task.Run(async () => await pipe.Alice.SendAsync(bigFile, cancellationToken));

                ReadOnlyMemory<byte> remainingFile = bigFile;
                byte[] buffer = new byte[receiveBufferSize];
                while (!remainingFile.IsEmpty)
                {
                    KcpConversationReceiveResult result;
                    if (waitToReceive)
                    {
                        result = await pipe.Bob.WaitToReceiveAsync(cancellationToken);
                        Assert.False(result.TransportClosed, "Transport should not be closed.");
                        Assert.True(result.BytesReceived > 0);
                    }
                    result = await pipe.Bob.ReceiveAsync(buffer, cancellationToken);
                    Assert.False(result.TransportClosed, "Transport should not be closed.");
                    Assert.True(result.BytesReceived > 0);
                    Assert.True(result.BytesReceived <= remainingFile.Length);
                    Assert.True(buffer.AsSpan(0, result.BytesReceived).SequenceEqual(remainingFile.Span.Slice(0, result.BytesReceived)));
                    remainingFile = remainingFile.Slice(result.BytesReceived);
                }

                Assert.True(await sendTask);

                await AssertReceiveNoDataAsync(pipe.Bob, waitToReceive, buffer);
            });
        }

        private static async Task AssertReceiveNoDataAsync(KcpConversation conversation, bool waitToReceive, byte[]? buffer = null)
        {
            buffer ??= new byte[1];

            if (waitToReceive)
            {
                using var cts = new CancellationTokenSource(1000);
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await conversation.WaitToReceiveAsync(cts.Token));
            }

            {
                using var cts = new CancellationTokenSource(1000);
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await conversation.ReceiveAsync(buffer, cts.Token));
            }
        }
    }
}
