using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace KcpSharp.Tests
{
    public class WaitForSendQueueAvailableSpaceTests
    {
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [Theory]
        public async Task TestTransportClosed(bool streamMode, bool dispose)
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

            await TestHelper.RunWithTimeout(TimeSpan.FromSeconds(2), async cancellationToken =>
            {
                Assert.False(await conversation.WaitForSendQueueAvailableSpaceAsync(0, 0, CancellationToken.None));
            });
        }

        [InlineData(false, false, 1, 0)]
        [InlineData(false, false, 0, 1)]
        [InlineData(false, true, 1, 0)]
        [InlineData(false, true, 0, 1)]
        [InlineData(true, false, 1, 0)]
        [InlineData(true, false, 0, 1)]
        [InlineData(true, true, 1, 0)]
        [InlineData(true, true, 0, 1)]
        [Theory]
        public async Task ZeroRequestOnFullQueue(bool streamMode, bool dispose, int byteCount, int fragmentCount)
        {
            const int mtu = 1400;
            const int mss = mtu - 24;

            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var conversation = new KcpConversation(blackholeConnection.Object, 1, new KcpConversationOptions { StreamMode = streamMode, UpdateInterval = 30, SendQueueSize = 4, SendWindow = 2, RemoteReceiveWindow = 2, DisableCongestionControl = true });

            Assert.True(conversation.TrySend(new byte[4 * mss]));
            await Task.Delay(150);
            Assert.True(conversation.TrySend(new byte[2 * mss]));
            Assert.False(conversation.TrySend(new byte[mss]));

            await TestHelper.RunWithTimeout(TimeSpan.FromSeconds(2), async cancellationToken =>
            {
                Assert.True(await conversation.WaitForSendQueueAvailableSpaceAsync(0, 0, cancellationToken));
                Task<bool> waitTask = conversation.WaitForSendQueueAvailableSpaceAsync(byteCount, fragmentCount, cancellationToken).AsTask();
                if (dispose)
                {
                    conversation.Dispose();
                }
                else
                {
                    conversation.SetTransportClosed();
                }
                await Task.Delay(100, cancellationToken);
                Assert.True(waitTask.IsCompleted);
                Assert.False(await waitTask);
            });
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TestSendAndWait(bool streamMode)
        {
            const int mtu = 1400;
            const int mss = mtu - 24;
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(0x12345678, new KcpConversationOptions { StreamMode = streamMode, UpdateInterval = 30, SendWindow = 2, ReceiveWindow = 16, RemoteReceiveWindow = 16, SendQueueSize = 8, DisableCongestionControl = true });
                Task receiveTask = ComsumeAllSlowlyAsync(pipe.Bob, 16 * mss, cancellationToken);
                Assert.True(pipe.Alice.TrySend(new byte[8 * mss]));

                Task<bool> waitTask = pipe.Alice.WaitForSendQueueAvailableSpaceAsync(7 * mss, 0, cancellationToken).AsTask();
                Assert.False(waitTask.IsCompleted);
                Assert.True(await waitTask);
                Assert.True(pipe.Alice.TrySend(new byte[7 * mss]));

                waitTask = pipe.Alice.WaitForSendQueueAvailableSpaceAsync(0, 7, cancellationToken).AsTask();
                Assert.False(waitTask.IsCompleted);
                Assert.True(await waitTask);
                Assert.True(pipe.Alice.TrySend(new byte[7 * mss]));

                await pipe.Alice.FlushAsync(cancellationToken);
                pipe.Bob.SetTransportClosed();
                await receiveTask;
            });
        }


        private static async Task ComsumeAllSlowlyAsync(KcpConversation conversation, int bufferSize, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[bufferSize];
            while (!cancellationToken.IsCancellationRequested)
            {
                KcpConversationReceiveResult result = await conversation.ReceiveAsync(buffer, cancellationToken);
                if (result.TransportClosed)
                {
                    break;
                }
                await Task.Delay(50, cancellationToken);
            }
        }
    }
}
