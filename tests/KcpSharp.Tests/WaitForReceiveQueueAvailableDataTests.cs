using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace KcpSharp.Tests
{
    public class WaitForReceiveQueueAvailableDataTests
    {
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [Theory]
        public async Task TestTransportClosedBeforeWait(bool streamMode, bool dispose)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var conversation = new KcpConversation(blackholeConnection.Object, new KcpConversationOptions { StreamMode = streamMode });
            if (dispose)
            {
                conversation.Dispose();
            }
            else
            {
                conversation.SetTransportClosed();
            }

            Task<bool> waitTask = conversation.WaitForReceiveQueueAvailableDataAsync(0).AsTask();
            Assert.True(waitTask.IsCompletedSuccessfully);
            Assert.False(await waitTask);
        }

        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [Theory]
        public async Task TestTransportClosedAfterWait(bool streamMode, bool dispose)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var conversation = new KcpConversation(blackholeConnection.Object, new KcpConversationOptions { StreamMode = streamMode });
            Task<bool> waitTask = conversation.WaitForReceiveQueueAvailableDataAsync(1).AsTask();
            Assert.False(waitTask.IsCompleted);
            if (dispose)
            {
                conversation.Dispose();
            }
            else
            {
                conversation.SetTransportClosed();
            }
            await Task.Delay(200);
            Assert.True(waitTask.IsCompletedSuccessfully);
            Assert.False(await waitTask);
        }

        [InlineData(false, 500)]
        [InlineData(true, 500)]
        [InlineData(false, 3200)]
        [InlineData(true, 3200)]
        [Theory]
        public Task TestWaitForData(bool streamMode, int size)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10000), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(0x12345678, new KcpConversationOptions { StreamMode = streamMode });
                Assert.True(pipe.Alice.TrySend(new byte[size]));

                byte[] buffer = new byte[size];
                await pipe.Bob.WaitForReceiveQueueAvailableDataAsync(size, cancellationToken);
                Assert.True(pipe.Bob.TryReceive(buffer, out KcpConversationReceiveResult result));
                Assert.Equal(size, result.BytesReceived);
                Assert.False(result.TransportClosed);
            });
        }
    }
}
