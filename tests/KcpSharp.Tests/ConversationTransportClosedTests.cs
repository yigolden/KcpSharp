using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace KcpSharp.Tests
{
    public class ConversationTransportClosedTests
    {
        [Fact]
        public Task WaitToReceiveBeforeTransportClosed()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var trackedAllocator = new TrackedBufferAllocator();
                {
                    using var conversation = new KcpConversation(blackholeConnection.Object, 42, new KcpConversationOptions { BufferAllocator = trackedAllocator });
                    Task unregisterTask = Task.Run(async () => { await Task.Delay(500); conversation.SetTransportClosed(); });
                    Assert.True((await conversation.WaitToReceiveAsync(cancellationToken)).TransportClosed);

                    await unregisterTask;
                    Assert.True((await conversation.WaitToReceiveAsync(cancellationToken)).TransportClosed);
                }
                Assert.Equal(0, trackedAllocator.InuseBufferCount);
            });
        }

        [Fact]
        public Task WaitToReceiveAfterTransportClosed()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var trackedAllocator = new TrackedBufferAllocator();
                {
                    using var conversation = new KcpConversation(blackholeConnection.Object, 42, new KcpConversationOptions { BufferAllocator = trackedAllocator });
                    conversation.SetTransportClosed();
                    Assert.True((await conversation.WaitToReceiveAsync(cancellationToken)).TransportClosed);
                }
                Assert.Equal(0, trackedAllocator.InuseBufferCount);
            });
        }

        [Fact]
        public Task ReceiveBeforeTransportClosed()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var trackedAllocator = new TrackedBufferAllocator();
                {
                    byte[] buffer = new byte[4096];
                    using var conversation = new KcpConversation(blackholeConnection.Object, 42, new KcpConversationOptions { BufferAllocator = trackedAllocator });
                    Task unregisterTask = Task.Run(async () => { await Task.Delay(500); conversation.SetTransportClosed(); });
                    Assert.True((await conversation.ReceiveAsync(buffer, cancellationToken)).TransportClosed);
                    await unregisterTask;
                    Assert.True((await conversation.ReceiveAsync(buffer, cancellationToken)).TransportClosed);
                }
                Assert.Equal(0, trackedAllocator.InuseBufferCount);
            });
        }

        [Fact]
        public Task ReceiveAfterTransportClosed()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var trackedAllocator = new TrackedBufferAllocator();
                {
                    byte[] buffer = new byte[4096];
                    using var conversation = new KcpConversation(blackholeConnection.Object, 42, new KcpConversationOptions { BufferAllocator = trackedAllocator });
                    conversation.SetTransportClosed();
                    Assert.True((await conversation.ReceiveAsync(buffer, cancellationToken)).TransportClosed);
                }
                Assert.Equal(0, trackedAllocator.InuseBufferCount);
            });
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task SendAfterTransportClosed(bool useEmptyBuffer)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var trackedAllocator = new TrackedBufferAllocator();
                {
                    byte[] buffer = useEmptyBuffer ? Array.Empty<byte>() : new byte[4096];
                    using var conversation = new KcpConversation(blackholeConnection.Object, 42, new KcpConversationOptions { BufferAllocator = trackedAllocator });
                    conversation.SetTransportClosed();
                    Assert.False(await conversation.SendAsync(buffer, cancellationToken));
                }
                Assert.Equal(0, trackedAllocator.InuseBufferCount);
            });
        }

        [InlineData(8, 4, 2)]
        [InlineData(16, 4, 4)]
        [Theory]
        public Task SendBeforeTransportClosed(int packetCount, int queueSize, int sendWindowSize)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var trackedAllocator = new TrackedBufferAllocator();
                {
                    using var conversation = new KcpConversation(blackholeConnection.Object, 42, new KcpConversationOptions { BufferAllocator = trackedAllocator, SendQueueSize = queueSize, SendWindow = sendWindowSize });
                    Task unregisterTask = Task.Run(async () => { await Task.Delay(500); conversation.SetTransportClosed(); });
                    Assert.False(await SendMultiplePacketsAsync(conversation, packetCount, cancellationToken));
                    await unregisterTask;
                    Assert.False(await SendMultiplePacketsAsync(conversation, packetCount, cancellationToken));
                }
                Assert.Equal(0, trackedAllocator.InuseBufferCount);
            });

            static async Task<bool> SendMultiplePacketsAsync(KcpConversation conversation, int packetCount, CancellationToken cancellationToken)
            {
                byte[] buffer = new byte[1000];
                for (int i = 0; i < packetCount; i++)
                {
                    if (!await conversation.SendAsync(buffer, cancellationToken))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        [InlineData(80 * 1024)]
        [Theory]
        public Task TransferStreamBeforeTransportClosed(int fileSize)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var trackedAllocator = new TrackedBufferAllocator();
                {
                    using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { BufferAllocator = trackedAllocator, SendQueueSize = 8, SendWindow = 4, ReceiveWindow = 4, UpdateInterval = 30, StreamMode = true });

                    byte[] bigFile = new byte[fileSize];
                    Random.Shared.NextBytes(bigFile);
                    Task<bool> sendTask = Task.Run(async () => await pipe.Alice.SendAsync(bigFile, cancellationToken));
                    await Task.Delay(2000, cancellationToken);

                    pipe.Alice.SetTransportClosed();
                    pipe.Bob.SetTransportClosed();

                    Assert.False(await sendTask);
                }
                Assert.Equal(0, trackedAllocator.InuseBufferCount);
            });
        }

        [InlineData(20, 1200)]
        [Theory]
        public Task TransferPacketsBeforeTransportClosed(int packetCount, int packetSize)
        {
            List<byte[]> packets = new(
                Enumerable.Range(1, packetCount)
                .Select(i => new byte[packetSize])
                );
            foreach (byte[] packet in packets)
            {
                Random.Shared.NextBytes(packet);
            }

            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var trackedAllocator = new TrackedBufferAllocator();
                {
                    using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { BufferAllocator = trackedAllocator, SendQueueSize = 8, SendWindow = 4, ReceiveWindow = 4, UpdateInterval = 30, StreamMode = true });

                    Task<bool> sendTask = SendMultplePacketsAsync(pipe.Alice, packets, cancellationToken);
                    await Task.Delay(2000, cancellationToken);

                    pipe.Alice.SetTransportClosed();
                    pipe.Bob.SetTransportClosed();

                    Assert.False(await sendTask);
                }
                Assert.Equal(0, trackedAllocator.InuseBufferCount);
            });


            static async Task<bool> SendMultplePacketsAsync(KcpConversation conversation, IEnumerable<byte[]> packets, CancellationToken cancellationToken)
            {
                foreach (byte[] packet in packets)
                {
                    if (!await conversation.SendAsync(packet, cancellationToken))
                    {
                        return false;
                    }
                }
                return true;
            }

        }
    }
}
