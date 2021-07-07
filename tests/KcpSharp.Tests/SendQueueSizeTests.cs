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
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
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
            Assert.Equal(0, conversation.UnflushedBytes);
        }

        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [Theory]
        public async Task TestTransportClosedAfterSending(bool disposeOrCloseTransport, bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { StreamMode = streamMode });
            Assert.True(await conversation.SendAsync(new byte[1600]));
            Assert.Equal(1600, conversation.UnflushedBytes);
            if (disposeOrCloseTransport)
            {
                conversation.Dispose();
            }
            else
            {
                conversation.SetTransportClosed();
            }
            Assert.Equal(0, conversation.UnflushedBytes);

        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestFullQueueWithNoActiveOperation(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, Mtu = mtu, StreamMode = streamMode, SendWindow = windowSize, SendQueueSize = queueSize, RemoteReceiveWindow = windowSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource();
            int bufferSize = mss * (windowSize + queueSize);
            Task<bool> sendTask = conversation.SendAsync(new byte[bufferSize], cts.Token).AsTask();
            await Task.Delay(1000);
            Assert.True(sendTask.IsCompletedSuccessfully);
            Assert.True(sendTask.GetAwaiter().GetResult());

            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(0, byteCount);
            Assert.Equal(0, fragmentCount);
            Assert.Equal(bufferSize, conversation.UnflushedBytes);
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestFullQueueWithActiveOperation(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, Mtu = mtu, StreamMode = streamMode, SendWindow = windowSize, SendQueueSize = queueSize, RemoteReceiveWindow = windowSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource();
            int bufferSize = mss * (windowSize + queueSize);
            Task<bool> sendTask = conversation.SendAsync(new byte[bufferSize + 1], cts.Token).AsTask();
            await Task.Delay(1000);
            Assert.False(sendTask.IsCompleted);

            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(0, byteCount);
            Assert.Equal(0, fragmentCount);
            Assert.Equal(bufferSize, conversation.UnflushedBytes);

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
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, Mtu = mtu, StreamMode = streamMode, SendWindow = windowSize, SendQueueSize = queueSize, RemoteReceiveWindow = windowSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource();
            int bufferSize = windowSize * mss;
            Task<bool> sendTask = conversation.SendAsync(new byte[bufferSize], cts.Token).AsTask();
            await Task.Delay(1000);
            Assert.True(sendTask.IsCompletedSuccessfully);
            Assert.True(sendTask.GetAwaiter().GetResult());

            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(queueSize * mss, byteCount);
            Assert.Equal(queueSize, fragmentCount);
            Assert.Equal(windowSize * mss, conversation.UnflushedBytes);
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestHalfQueueAligned(bool streamMode)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            const int mtu = 200;
            const int mss = mtu - 24;
            const int windowSize = 4;
            const int queueSize = 8;
            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { UpdateInterval = 30, Mtu = mtu, StreamMode = streamMode, SendWindow = windowSize, SendQueueSize = queueSize, RemoteReceiveWindow = windowSize, DisableCongestionControl = true });

            using var cts = new CancellationTokenSource();
            int bufferSize = mss * (windowSize + queueSize / 2);
            Task<bool> sendTask = conversation.SendAsync(new byte[bufferSize], cts.Token).AsTask();
            await Task.Delay(1000);
            Assert.True(sendTask.IsCompletedSuccessfully);
            Assert.True(sendTask.GetAwaiter().GetResult());

            Assert.True(conversation.TryGetSendQueueAvailableSpace(out int byteCount, out int fragmentCount));
            Assert.Equal(queueSize / 2 * mss, byteCount);
            Assert.Equal(queueSize / 2, fragmentCount);
            Assert.Equal(bufferSize, conversation.UnflushedBytes);
        }

        [Fact]
        public async Task TestHalfQueuePacketsNotAligned()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
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
            Assert.Equal(sendSize, conversation.UnflushedBytes);
        }

        [Fact]
        public async Task TestHalfQueueStreamNotAligned()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
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
            Assert.Equal(sendSize, conversation.UnflushedBytes);
        }
    }
}
