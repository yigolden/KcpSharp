using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace KcpSharp.Tests
{
    public class StreamTests
    {
        [Fact]
        public void TestNullConversation()
        {
            Assert.Throws<ArgumentNullException>("conversation", () => new KcpStream(null!, true));
        }

        [Fact]
        public void TestNonStreamMode()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            using var conversation = new KcpConversation(blackholeConnection.Object, null);
            Assert.Throws<ArgumentException>("conversation", () => new KcpStream(conversation, true));
        }

        [Fact]
        public async Task TestTransportClosed()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            using var conversation = new KcpConversation(blackholeConnection.Object, new KcpConversationOptions { StreamMode = true });
            using var stream = new KcpStream(conversation, true);
            conversation.SetTransportClosed();

            byte[] buffer = new byte[100];
            await Assert.ThrowsAsync<IOException>(async () => await stream.ReadAsync(buffer));
            await Assert.ThrowsAsync<IOException>(async () => await stream.WriteAsync(buffer));
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TaskStreamDispose(bool dispose)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using var conversation = new KcpConversation(blackholeConnection.Object, new KcpConversationOptions { StreamMode = true });
                {
                    using var stream = new KcpStream(conversation, dispose);
                    await stream.WriteAsync(new byte[100], cancellationToken);
                }
                if (dispose)
                {
                    Assert.False(conversation.TryGetSendQueueAvailableSpace(out _, out _));
                }
                else
                {
                    Assert.True(conversation.TryGetSendQueueAvailableSpace(out _, out _));
                }
            });
        }

        [Fact]
        public Task TestZeroByteReceive()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { StreamMode = true });
                using var stream = new KcpStream(pipe.Bob, true);
                Task<int> readTask = stream.ReadAsync(default, cancellationToken).AsTask();
                Assert.False(readTask.IsCompleted);
                await Task.Delay(800, cancellationToken);
                Assert.False(readTask.IsCompleted);

                byte[] bytes = new byte[100];
                Random.Shared.NextBytes(bytes);
                await pipe.Alice.SendAsync(bytes, cancellationToken);
                await pipe.Alice.FlushAsync(cancellationToken);
                await Task.Delay(100, cancellationToken);
                Assert.True(readTask.IsCompletedSuccessfully);
                int bytesRead = await readTask;
                Assert.Equal(0, bytesRead);

                byte[] buffer = new byte[500];
                bytesRead = await stream.ReadAsync(buffer, cancellationToken);
                Assert.Equal(100, bytesRead);
                Assert.True(buffer.AsSpan(0, 100).SequenceEqual(bytes));
            });
        }

        [InlineData(0)]
        [InlineData(100)]
        [InlineData(4096)]
        [Theory]
        public Task TestReadCanceled(int bufferSize)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { StreamMode = true });
                using var stream = new KcpStream(pipe.Bob, true);
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(1));
                    byte[] buffer = new byte[bufferSize];
                    OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await stream.ReadAsync(buffer, cts.Token));
                    Assert.Equal(cts.Token, exception.CancellationToken);
                }
            });
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TestReadAborted(bool dispose)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { StreamMode = true });
                var stream = new KcpStream(pipe.Bob, true);

                byte[] buffer = new byte[100];
                Task<int> readTask = stream.ReadAsync(buffer, cancellationToken).AsTask();
                await Task.Delay(500, cancellationToken);
                Assert.False(readTask.IsCompleted);

                if (dispose)
                {
                    pipe.Bob.SetTransportClosed();
                    Assert.Equal(0, await readTask);
                    await Assert.ThrowsAsync<IOException>(async () => await stream.ReadAsync(buffer, cancellationToken));
                }
                else
                {
                    var innerException = new InvalidTimeZoneException();
                    pipe.Bob.CancelPendingReceive(innerException, default);
                    OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await readTask);
                    Assert.Equal(innerException, exception.InnerException);
                }
            });
        }

        [InlineData(100)]
        [InlineData(1600)]
        [Theory]
        public Task TestDataTransfer(int bufferSize)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromHours(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { StreamMode = true, UpdateInterval = 30 });
                var stream = new KcpStream(pipe.Alice, true);

                byte[] buffer = new byte[bufferSize];
                Random.Shared.NextBytes(buffer);

                await stream.WriteAsync(buffer, cancellationToken);
                await stream.WriteAsync(default, cancellationToken);
                await stream.WriteAsync(buffer, cancellationToken);

                await stream.FlushAsync(cancellationToken);

                await Task.Delay(1000, cancellationToken);

                byte[] buffer2 = new byte[bufferSize * 3];
                KcpConversationReceiveResult result = await pipe.Bob.ReceiveAsync(buffer2, cancellationToken);
                Assert.Equal(bufferSize * 2, result.BytesReceived);
                Assert.True(buffer2.AsSpan(0, bufferSize).SequenceEqual(buffer));
                Assert.True(buffer2.AsSpan(bufferSize, bufferSize).SequenceEqual(buffer));
            });

        }

    }
}
