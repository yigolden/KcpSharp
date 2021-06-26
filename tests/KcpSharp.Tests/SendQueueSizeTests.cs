using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace KcpSharp.Tests
{
    public class SendQueueSizeTests
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
            Assert.False(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(0, byteCount);
            Assert.Equal(0, fragmentCount);
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestFullQueueWithNoActiveOperation(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, Mtu = mtu, StreamMode = streamMode, SendWindow = windowSize, SendQueueSize = queueSize, RemoteReceiveWindow = windowSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource();
            Task<bool> sendTask = conversation.SendAsync(new byte[mss * (windowSize + queueSize)], cts.Token).AsTask();
            await Task.Delay(1000);
            Assert.True(sendTask.IsCompletedSuccessfully);
            Assert.True(sendTask.GetAwaiter().GetResult());

            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(0, byteCount);
            Assert.Equal(0, fragmentCount);
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestFullQueueWithActiveOperation(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, Mtu = mtu, StreamMode = streamMode, SendWindow = windowSize, SendQueueSize = queueSize, RemoteReceiveWindow = windowSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource();
            Task<bool> sendTask = conversation.SendAsync(new byte[mss * (windowSize + queueSize) + 1], cts.Token).AsTask();
            await Task.Delay(1000);
            Assert.False(sendTask.IsCompleted);

            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(0, byteCount);
            Assert.Equal(0, fragmentCount);

            Assert.False(sendTask.IsCompleted);
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await sendTask);
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestEmptyQueueWithNoneEmptySendWindow(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, Mtu = mtu, StreamMode = streamMode, SendWindow = windowSize, SendQueueSize = queueSize, RemoteReceiveWindow = windowSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource();
            Task<bool> sendTask = conversation.SendAsync(new byte[windowSize * mss], cts.Token).AsTask();
            await Task.Delay(1000);
            Assert.True(sendTask.IsCompletedSuccessfully);
            Assert.True(sendTask.GetAwaiter().GetResult());

            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(queueSize * mss, byteCount);
            Assert.Equal(queueSize, fragmentCount);
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestHalfQueueAligned(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, Mtu = mtu, StreamMode = streamMode, SendWindow = windowSize, SendQueueSize = queueSize, RemoteReceiveWindow = windowSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource();
            Task<bool> sendTask = conversation.SendAsync(new byte[mss * (windowSize + queueSize / 2)], cts.Token).AsTask();
            await Task.Delay(1000);
            Assert.True(sendTask.IsCompletedSuccessfully);
            Assert.True(sendTask.GetAwaiter().GetResult());

            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(queueSize / 2 * mss, byteCount);
            Assert.Equal(queueSize / 2, fragmentCount);
        }

        [Fact]
        public async Task TestHalfQueuePacketsNotAligned()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, Mtu = mtu, SendWindow = windowSize, SendQueueSize = queueSize, RemoteReceiveWindow = windowSize, DisableCongestionControl = true });

            const int sendSize = mss * (windowSize + queueSize / 2) + mss / 2;

            using var cts = new CancellationTokenSource();
            Task<bool> sendTask = conversation.SendAsync(new byte[sendSize], cts.Token).AsTask();
            await Task.Delay(1000);
            Assert.True(sendTask.IsCompletedSuccessfully);
            Assert.True(sendTask.GetAwaiter().GetResult());

            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal((queueSize / 2 - 1) * mss, byteCount);
            Assert.Equal(queueSize / 2 - 1, fragmentCount);
        }

        [Fact]
        public async Task TestHalfQueueStreamNotAligned()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, Mtu = mtu, StreamMode = true, SendWindow = windowSize, SendQueueSize = queueSize, RemoteReceiveWindow = windowSize, DisableCongestionControl = true });

            const int sendSize = mss * (windowSize + queueSize / 2) + mss / 2;

            using var cts = new CancellationTokenSource();
            Task<bool> sendTask = conversation.SendAsync(new byte[sendSize], cts.Token).AsTask();
            await Task.Delay(1000);
            Assert.True(sendTask.IsCompletedSuccessfully);
            Assert.True(sendTask.GetAwaiter().GetResult());

            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal((queueSize / 2 - 1) * mss + mss / 2, byteCount);
            Assert.Equal(queueSize / 2 - 1, fragmentCount);
        }
    }
}
