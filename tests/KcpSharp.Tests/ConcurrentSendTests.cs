using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace KcpSharp.Tests
{
    public class ConcurrentSendTests
    {
        [Fact]
        public async Task TestConcurrentSend()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            using var conversation = new KcpConversation(blackholeConnection.Object, null);

            await TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                const int Count = 50;
                int waitingTask = 0;
                Task[] tasks = new Task[Count];
                for (int i = 0; i < Count; i++)
                {
                    tasks[i] = Task.Run(async () =>
                    {
                        ValueTask valueTask = conversation.InputPakcetAsync(new byte[500], cancellationToken);
                        if (!valueTask.IsCompleted)
                        {
                            Interlocked.Increment(ref waitingTask);
                        }
                        await valueTask;
                    });
                }
                await Task.WhenAll(tasks);

                Assert.True(waitingTask > 1);
            });
        }
    }
}
