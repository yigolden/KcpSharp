using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace KcpSharp.Tests
{
    public class SendQueueAppendTests
    {
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [Theory]
        public void TestTransportClosed(bool disposeOrCloseTransport, bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { StreamMode = streamMode });
            if (disposeOrCloseTransport)
            {
                conversation.Dispose();
            }
            else
            {
                conversation.SetTransportClosed();
            }
            Assert.False(conversation.TrySend(new byte[10]));
            Assert.False(conversation.TrySend(new byte[10], false, out int bytesWritten));
            Assert.Equal(0, bytesWritten);
            if (streamMode)
            {
                Assert.False(conversation.TrySend(new byte[10], false, out bytesWritten));
                Assert.Equal(0, bytesWritten);
            }
            else
            {
                Assert.Throws<ArgumentException>("allowPartialSend", () => conversation.TrySend(new byte[10], true, out bytesWritten));
            }
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestZeroByteSend(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, StreamMode = streamMode, Mtu = mtu, SendWindow = windowSize, RemoteReceiveWindow = windowSize, SendQueueSize = queueSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource(2000);
            Assert.True(await conversation.SendAsync(new byte[mss * windowSize], cts.Token));

            await Task.Delay(1000);

            Assert.True(conversation.TrySend(default));
            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            if (streamMode)
            {
                Assert.Equal(queueSize * mss, byteCount);
                Assert.Equal(queueSize, fragmentCount);
            }
            else
            {
                Assert.Equal((queueSize - 1) * mss, byteCount);
                Assert.Equal((queueSize - 1), fragmentCount);
            }
        }

        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [Theory]
        public async Task TestFullQueueSend(bool streamMode, bool emptyBuffer)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, StreamMode = streamMode, Mtu = mtu, SendWindow = windowSize, RemoteReceiveWindow = windowSize, SendQueueSize = queueSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource(2000);
            Assert.True(await conversation.SendAsync(new byte[mss * (windowSize + queueSize)], cts.Token));

            await Task.Delay(1000);

            if (streamMode)
            {
                if (emptyBuffer)
                {
                    Assert.True(conversation.TrySend(default));
                }
                else
                {
                    Assert.False(conversation.TrySend(new byte[1]));
                }
            }
            else
            {
                if (emptyBuffer)
                {
                    Assert.False(conversation.TrySend(default));
                }
                else
                {
                    Assert.False(conversation.TrySend(new byte[1]));
                }
            }
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestEmptyQueueSend(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, StreamMode = streamMode, Mtu = mtu, SendWindow = windowSize, RemoteReceiveWindow = windowSize, SendQueueSize = queueSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource(2000);
            Assert.True(await conversation.SendAsync(new byte[mss * windowSize], cts.Token));

            await Task.Delay(1000);

            Assert.True(conversation.TrySend(new byte[mss * queueSize / 2]));
            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(mss * queueSize / 2, byteCount);
            Assert.Equal(queueSize / 2, fragmentCount);
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestHalfQueueSend(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, StreamMode = streamMode, Mtu = mtu, SendWindow = windowSize, RemoteReceiveWindow = windowSize, SendQueueSize = queueSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource(2000);
            Assert.True(await conversation.SendAsync(new byte[mss * (windowSize + queueSize / 4)], cts.Token));

            await Task.Delay(1000);

            Assert.True(conversation.TrySend(new byte[mss * queueSize / 2]));
            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(mss * queueSize / 4, byteCount);
            Assert.Equal(queueSize / 4, fragmentCount);
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestPartialSend(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, StreamMode = streamMode, Mtu = mtu, SendWindow = windowSize, RemoteReceiveWindow = windowSize, SendQueueSize = queueSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource(2000);
            Assert.True(await conversation.SendAsync(new byte[mss * (windowSize + queueSize * 3 / 4)], cts.Token));

            await Task.Delay(1000);

            if (streamMode)
            {
                Assert.True(conversation.TrySend(new byte[mss * queueSize / 2], true, out int bytesWritten));
                Assert.Equal(mss * queueSize / 4, bytesWritten);
                Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragment));
                Assert.Equal(0, byteCount);
                Assert.Equal(0, fragment);
            }
            else
            {
                Assert.Throws<ArgumentException>("allowPartialSend", () => conversation.TrySend(new byte[mss * queueSize / 2], true, out int bytesWritten));
            }
        }

        [Fact]
        public Task TestSendPacket()
        {
            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;

            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { UpdateInterval = 30, StreamMode = false, Mtu = mtu, SendWindow = windowSize, ReceiveWindow = (windowSize + queueSize), RemoteReceiveWindow = windowSize, SendQueueSize = queueSize, DisableCongestionControl = true });

                byte[] packet1 = new byte[6 * mss];
                byte[] packet2 = new byte[6 * mss];
                Random.Shared.NextBytes(packet1);
                Random.Shared.NextBytes(packet2);

                Assert.True(pipe.Alice.TrySend(packet1));
                await Task.Delay(500, cancellationToken);
                Assert.True(pipe.Alice.TrySend(packet2));
                await Task.Delay(1000, cancellationToken);
                Assert.Equal(0, pipe.Alice.UnflushedBytes);

                byte[] buffer = new byte[12 * mss];
                KcpConversationReceiveResult result = await pipe.Bob.ReceiveAsync(buffer, cancellationToken);
                Assert.Equal(packet1.Length, result.BytesReceived);
                Assert.True(buffer.AsSpan(0, result.BytesReceived).SequenceEqual(packet1));
                result = await pipe.Bob.ReceiveAsync(buffer, cancellationToken);
                Assert.Equal(packet2.Length, result.BytesReceived);
                Assert.True(buffer.AsSpan(0, result.BytesReceived).SequenceEqual(packet2));
            });
        }

        [Fact]
        public Task TestSendStream()
        {
            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;

            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { UpdateInterval = 30, StreamMode = true, Mtu = mtu, SendWindow = windowSize, ReceiveWindow = (windowSize + queueSize), RemoteReceiveWindow = windowSize, SendQueueSize = queueSize, DisableCongestionControl = true });

                byte[] packet1 = new byte[6 * mss];
                byte[] packet2 = new byte[6 * mss];
                Random.Shared.NextBytes(packet1);
                Random.Shared.NextBytes(packet2);

                Assert.True(pipe.Alice.TrySend(packet1));
                await Task.Delay(500, cancellationToken);
                Assert.True(pipe.Alice.TrySend(packet2));
                await Task.Delay(1000, cancellationToken);
                Assert.Equal(0, pipe.Alice.UnflushedBytes);

                byte[] buffer = new byte[13 * mss];
                KcpConversationReceiveResult result = await pipe.Bob.ReceiveAsync(buffer, cancellationToken);
                Assert.Equal(packet1.Length + packet2.Length, result.BytesReceived);
                Assert.True(buffer.AsSpan(0, packet1.Length).SequenceEqual(packet1));
                Assert.True(buffer.AsSpan(packet1.Length, packet2.Length).SequenceEqual(packet2));
            });
        }
    }
}
