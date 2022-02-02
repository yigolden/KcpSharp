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

            Task<bool> waitTask = conversation.WaitForReceiveQueueAvailableDataAsync(0, 0).AsTask();
            Assert.True(waitTask.IsCompletedSuccessfully);
            Assert.False(await waitTask);
        }

        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [Theory]
        public async Task TestTransportClosedAfterWait_Bytes(bool streamMode, bool dispose)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var conversation = new KcpConversation(blackholeConnection.Object, new KcpConversationOptions { StreamMode = streamMode });
            Task<bool> waitTask = conversation.WaitForReceiveQueueAvailableDataAsync(1, 0).AsTask();
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

        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [Theory]
        public async Task TestTransportClosedAfterWait_Segments(bool streamMode, bool dispose)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var conversation = new KcpConversation(blackholeConnection.Object, new KcpConversationOptions { StreamMode = streamMode });
            Task<bool> waitTask = conversation.WaitForReceiveQueueAvailableDataAsync(0, 1).AsTask();
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
        public Task TestWaitForData_Bytes(bool streamMode, int size)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(0x12345678, new KcpConversationOptions { StreamMode = streamMode });
                Task<bool> waitTask = pipe.Bob.WaitForReceiveQueueAvailableDataAsync(size, 0, cancellationToken).AsTask();
                await Task.Delay(500, cancellationToken);
                Assert.False(waitTask.IsCompleted);

                Assert.True(pipe.Alice.TrySend(new byte[size / 2]));
                await Task.Delay(500, cancellationToken);
                Assert.False(waitTask.IsCompleted);

                Assert.True(pipe.Alice.TrySend(new byte[size / 2]));
                await Task.Delay(1000, cancellationToken);
                Assert.True(waitTask.IsCompleted);

                Assert.True(await waitTask);

                byte[] buffer = new byte[size];
                if (streamMode)
                {
                    Assert.True(pipe.Bob.TryReceive(buffer, out KcpConversationReceiveResult result));
                    Assert.Equal(size, result.BytesReceived);
                    Assert.False(result.TransportClosed);
                }
                else
                {
                    Assert.True(pipe.Bob.TryReceive(buffer, out KcpConversationReceiveResult result));
                    Assert.Equal(size / 2, result.BytesReceived);
                    Assert.False(result.TransportClosed);
                }
            });
        }

        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        [Theory]
        public Task TestWaitForData_Segments(int segmentCount)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(0x12345678, new KcpConversationOptions { DisableCongestionControl = true });
                Task<bool> waitTask = pipe.Bob.WaitForReceiveQueueAvailableDataAsync(0, segmentCount, cancellationToken).AsTask();
                await Task.Delay(500, cancellationToken);
                Assert.False(waitTask.IsCompleted);

                for (int i = 0; i < segmentCount / 2; i++)
                {
                    Assert.True(pipe.Alice.TrySend(new byte[1000]));
                }
                await Task.Delay(500, cancellationToken);
                Assert.False(waitTask.IsCompleted);

                for (int i = 0; i < segmentCount / 2; i++)
                {
                    Assert.True(pipe.Alice.TrySend(new byte[1000]));
                }
                await Task.Delay(2000, cancellationToken);
                Assert.True(waitTask.IsCompleted);

                Assert.True(await waitTask);
            });
        }

        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        [Theory]
        public Task TestWaitForData_LargeSegments(int segmentCount)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(0x12345678, new KcpConversationOptions { DisableCongestionControl = true });
                Task<bool> waitTask = pipe.Bob.WaitForReceiveQueueAvailableDataAsync(0, segmentCount, cancellationToken).AsTask();
                await Task.Delay(500, cancellationToken);
                Assert.False(waitTask.IsCompleted);

                for (int i = 0; i < segmentCount / 2; i++)
                {
                    Assert.True(pipe.Alice.TrySend(new byte[3000]));
                }
                await Task.Delay(500, cancellationToken);
                Assert.False(waitTask.IsCompleted);

                for (int i = 0; i < segmentCount / 2; i++)
                {
                    Assert.True(pipe.Alice.TrySend(new byte[3000]));
                }
                await Task.Delay(1000, cancellationToken);
                Assert.True(waitTask.IsCompleted);

                Assert.True(await waitTask);
            });
        }



    }
}
