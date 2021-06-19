using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KcpSharp.Tests
{
    public class SendAndFlushTests
    {
        [Fact]
        public Task FlushEmptyQueueAsync()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe();
                Assert.True(await pipe.Alice.FlushAsync(cancellationToken));
            });
        }

        [Fact]
        public Task FlushAfterTransportClosed()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe();
                pipe.Alice.SetTransportClosed();
                Assert.False(await pipe.Alice.FlushAsync(cancellationToken));
            });
        }

        [Fact]
        public Task FlushWithLargeWindowSize()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe();
                for (int i = 0; i < 6; i++)
                {
                    await pipe.Alice.SendAsync(new byte[100], cancellationToken);
                }
                Assert.True(await pipe.Alice.FlushAsync(cancellationToken));
            });
        }

        [Fact]
        public Task FlushWithSmallWindowSize()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(15), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { SendWindow = 2, ReceiveWindow = 2, RemoteReceiveWindow = 2, SendQueueSize = 2, UpdateInterval = 10, NoDelay = true });

                await SendPackets(pipe.Alice, 4, cancellationToken);
                Task flushTask = pipe.Alice.FlushAsync(cancellationToken).AsTask();
                Assert.False(flushTask.IsCompleted);

                await ReceiveAllAsync(pipe.Bob, 1, cancellationToken);
                await Task.Delay(200, cancellationToken);
                Assert.False(flushTask.IsCompleted);

                await ReceiveAllAsync(pipe.Bob, 1, cancellationToken);
                Assert.False(flushTask.IsCompleted);

                await ReceiveAllAsync(pipe.Bob, 2, cancellationToken);
                await Task.Delay(200, cancellationToken);
                Assert.True(flushTask.IsCompleted);
                await flushTask;
            });

            static async Task SendPackets(KcpConversation conversation, int count, CancellationToken cancellationToken)
            {
                for (int i = 0; i < count; i++)
                {
                    Assert.True(await conversation.SendAsync(new byte[100], cancellationToken));
                }
            }

            static async Task ReceiveAllAsync(KcpConversation conversation, int count, CancellationToken cancellationToken)
            {
                byte[] buffer = new byte[100];
                for (int i = 0; i < count; i++)
                {
                    await conversation.ReceiveAsync(buffer, cancellationToken);
                }
            }
        }
    }
}
