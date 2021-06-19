using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KcpSharp.Tests
{
    public class PerfectUnidirectionalTransferTests
    {
        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TestOnePacketSendReceive(bool waitToReceive)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe();

                Assert.False(pipe.Bob.TryPeek(out int packetSize));
                Assert.Equal(0, packetSize);

                byte[] data1 = new byte[4096];
                Random.Shared.NextBytes(data1);
                await pipe.Alice.SendAsync(data1, cancellationToken);

                KcpConversationReceiveResult result;
                if (waitToReceive)
                {
                    result = await pipe.Bob.WaitToReceiveAsync(cancellationToken);
                    Assert.False(result.TransportClosed, "Transport should not be closed.");
                    Assert.Equal(4096, result.BytesReceived);

                    Assert.True(pipe.Bob.TryPeek(out packetSize));
                    Assert.Equal(4096, packetSize);
                }

                byte[] data2 = new byte[4096];
                result = await pipe.Bob.ReceiveAsync(data2, cancellationToken);
                Assert.False(result.TransportClosed);
                Assert.Equal(4096, result.BytesReceived);

                Assert.True(data1.AsSpan().SequenceEqual(data2.AsSpan()));
            });
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TestZeroByteReadReceive(bool stream)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { UpdateInterval = 30, StreamMode = stream });

                Task<KcpConversationReceiveResult> receiveTask = pipe.Bob.ReceiveAsync(default, cancellationToken).AsTask();
                Assert.False(receiveTask.IsCompleted);

                byte[] data1 = new byte[4096];
                Random.Shared.NextBytes(data1);
                await pipe.Alice.SendAsync(data1, cancellationToken);

                await Task.Delay(500, cancellationToken);

                if (stream)
                {
                    KcpConversationReceiveResult result = await receiveTask;
                    Assert.False(result.TransportClosed);
                    Assert.Equal(0, result.BytesReceived);
                }
                else
                {
                    InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => receiveTask);
                    Assert.Equal("Buffer is too small.", exception.Message);
                }
            });
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TestZeroBytePacketSendReceive(bool waitToReceive)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe();

                await pipe.Alice.SendAsync(default, cancellationToken);
                KcpConversationReceiveResult result;
                if (waitToReceive)
                {
                    result = await pipe.Bob.WaitToReceiveAsync(cancellationToken);
                    Assert.False(result.TransportClosed);
                    Assert.Equal(0, result.BytesReceived);
                }
                result = await pipe.Bob.ReceiveAsync(default, cancellationToken);
                Assert.False(result.TransportClosed);
                Assert.Equal(0, result.BytesReceived);
                await AssertReceiveNoDataAsync(pipe.Bob, waitToReceive);
            });
        }

        [Fact]
        public Task TestZeroByteReceiveOnStream()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { UpdateInterval = 30, StreamMode = true });

                Task<KcpConversationReceiveResult> receiveTask = pipe.Bob.ReceiveAsync(default, cancellationToken).AsTask();
                await Task.Delay(500, cancellationToken);
                Assert.False(receiveTask.IsCompleted);
                await pipe.Alice.SendAsync(new byte[100], cancellationToken);
                await Task.Delay(500, cancellationToken);
                Assert.True(receiveTask.IsCompleted);
                KcpConversationReceiveResult result = await receiveTask;
                Assert.False(result.TransportClosed);
                Assert.Equal(0, result.BytesReceived);
                result = await pipe.Bob.ReceiveAsync(new byte[200], cancellationToken);
                Assert.False(result.TransportClosed);
                Assert.Equal(100, result.BytesReceived);
            });
        }

        [Fact]
        public Task TestZeroByteStreamSend()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { UpdateInterval = 30, StreamMode = true });

                byte[] buffer = new byte[2000];
                Random.Shared.NextBytes(buffer);

                await pipe.Alice.SendAsync(buffer.AsMemory(0, 1000), cancellationToken);
                await pipe.Alice.SendAsync(default, cancellationToken);
                await pipe.Alice.SendAsync(buffer.AsMemory(1000, 1000), cancellationToken);

                byte[] buffer2 = new byte[3000];
                int bytesRead = 0;

                while (bytesRead < 2000)
                {
                    KcpConversationReceiveResult result = await pipe.Bob.ReceiveAsync(buffer2.AsMemory(bytesRead), cancellationToken);
                    Assert.False(result.TransportClosed);
                    bytesRead += result.BytesReceived;
                }

                Assert.Equal(2000, bytesRead);
                Assert.True(buffer.AsSpan().SequenceEqual(buffer2.AsSpan(0, 2000)));
            });
        }

        [InlineData(32, 800, 1000, false)]
        [InlineData(32, 800, 1000, true)]
        [InlineData(32, 1600, 3200, false)]
        [InlineData(32, 1600, 3200, true)]
        [InlineData(4, 1600, 3200, false)]
        [InlineData(4, 1600, 3200, true)]
        [InlineData(4, 800, 1200, false)]
        [InlineData(4, 800, 1200, true)]
        [InlineData(2, 100, 200, false)]
        [InlineData(2, 100, 200, true)]
        [Theory]
        public Task TestMultiplePacketSendReceive(int packetCount, int minPacketSize, int maxPacketSize, bool waitToReceive)
        {
            Assert.True(minPacketSize <= maxPacketSize);
            List<byte[]> packets = new List<byte[]>(
                Enumerable.Range(1, packetCount)
                .Select(i => new byte[Random.Shared.Next(minPacketSize, maxPacketSize + 1)])
                );
            foreach (byte[] packet in packets)
            {
                Random.Shared.NextBytes(packet);
            }

            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(20), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { SendQueueSize = 8, SendWindow = 4, ReceiveWindow = 4, UpdateInterval = 30 });

                Assert.False(pipe.Bob.TryPeek(out int packetSize));
                Assert.Equal(0, packetSize);

                Task sendTask = SendMultplePacketsAsync(pipe.Alice, packets, cancellationToken);
                Task receiveTask = ReceiveMultiplePacketsAsync(pipe.Bob, packets, maxPacketSize, waitToReceive, cancellationToken);
                await Task.WhenAll(sendTask, receiveTask);

                await AssertReceiveNoDataAsync(pipe.Bob, waitToReceive);
            });

            static async Task SendMultplePacketsAsync(KcpConversation conversation, IEnumerable<byte[]> packets, CancellationToken cancellationToken)
            {
                foreach (byte[] packet in packets)
                {
                    await conversation.SendAsync(packet, cancellationToken);
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

                        Assert.True(conversation.TryPeek(out int packetSize));
                        Assert.Equal(packet.Length, packetSize);
                    }
                    result = await conversation.ReceiveAsync(buffer, cancellationToken);
                    Assert.False(result.TransportClosed, "Transport should not be closed.");
                    Assert.Equal(packet.Length, result.BytesReceived);
                    Assert.True(buffer.AsSpan(0, result.BytesReceived).SequenceEqual(packet));
                }
            }
        }

        [InlineData(80 * 1024, 1000, false)]
        [InlineData(80 * 1024, 1000, true)]
        [InlineData(80 * 1024, 2000, false)]
        [InlineData(80 * 1024, 2000, true)]
        [InlineData(80 * 1024, 4096, false)]
        [InlineData(80 * 1024, 4096, true)]
        [Theory]
        public Task TestBigFileStreamSendReceive(int fileSize, int receiveBufferSize, bool waitToReceive)
        {
            Assert.True(receiveBufferSize < fileSize);
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(15), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { SendQueueSize = 16, SendWindow = 8, ReceiveWindow = 8, UpdateInterval = 30, StreamMode = true });

                byte[] bigFile = new byte[fileSize];
                Random.Shared.NextBytes(bigFile);
                Task sendTask = Task.Run(async () => await pipe.Alice.SendAsync(bigFile, cancellationToken));

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

                await sendTask;

                await AssertReceiveNoDataAsync(pipe.Bob, waitToReceive, buffer);
            });
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TestLargePacketSendReceive(bool waitToReceive)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                const int mtu = 100;
                const int mss = mtu - 24;
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { Mtu = 100, UpdateInterval = 30, SendQueueSize = 512, SendWindow = 260, ReceiveWindow = 260, RemoteReceiveWindow = 260 });

                byte[] buffer = new byte[256 * mss];
                Random.Shared.NextBytes(buffer);
                await pipe.Alice.SendAsync(buffer, cancellationToken);

                KcpConversationReceiveResult result;
                if (waitToReceive)
                {
                    result = await pipe.Bob.WaitToReceiveAsync(cancellationToken);
                    Assert.False(result.TransportClosed);
                    Assert.Equal(buffer.Length, result.BytesReceived);
                }

                InvalidOperationException exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await pipe.Bob.ReceiveAsync(new byte[100], cancellationToken));
                Assert.Equal("Buffer is too small.", exception.Message);

                byte[] buffer2 = new byte[buffer.Length + 100];
                result = await pipe.Bob.ReceiveAsync(buffer2, cancellationToken);
                Assert.False(result.TransportClosed);
                Assert.Equal(buffer.Length, result.BytesReceived);

                Assert.True(buffer2.AsSpan(0, result.BytesReceived).SequenceEqual(buffer));

                // larger message should not be sent
                buffer = new byte[buffer.Length + 1];
                exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await pipe.Alice.SendAsync(buffer, cancellationToken));
                Assert.Equal("Message is too large.", exception.Message);
            });
        }

        private static async Task AssertReceiveNoDataAsync(KcpConversation conversation, bool waitToReceive, byte[]? buffer = null)
        {
            buffer = buffer ?? new byte[1];

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
