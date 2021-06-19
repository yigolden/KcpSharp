using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace KcpSharp.Tests
{
    public class ReceiveBufferPeekAndReceiveTests
    {
        [InlineData(true)]
        [InlineData(false)]
        [Theory]
        public void TestPeekAndReceiveOnDisposedConversation(bool disposeOrClose)
        {
            using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe();

            if (disposeOrClose)
            {
                pipe.Bob.Dispose();
            }
            else
            {
                pipe.Bob.SetTransportClosed();
            }

            KcpConversationReceiveResult result;
            Assert.False(pipe.Bob.TryPeek(out result));
            Assert.True(result.TransportClosed, "Transport should be closed.");
            Assert.Equal(0, result.BytesReceived);

            Assert.False(pipe.Bob.TryReceive(default, out result));
            Assert.True(result.TransportClosed, "Transport should be closed.");
            Assert.Equal(0, result.BytesReceived);
        }

        [InlineData(true)]
        [InlineData(false)]
        [Theory]
        public void TestPeekAndReceiveEmptyQueue(bool useEmptyBuffer)
        {
            using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe();

            Assert.False(pipe.Bob.TryPeek(out KcpConversationReceiveResult result));
            Assert.False(result.TransportClosed, "Transport should not be closed.");
            Assert.Equal(0, result.BytesReceived);

            Assert.False(pipe.Bob.TryReceive(useEmptyBuffer ? default : new byte[1], out result));
            Assert.False(result.TransportClosed, "Transport should not be closed.");
            Assert.Equal(0, result.BytesReceived);
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TestPeekAndReceiveZeroBytePacket(bool useEmptyBuffer)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe();

                await pipe.Alice.SendAsync(default, cancellationToken);
                await Task.Delay(500, cancellationToken);

                KcpConversationReceiveResult result;
                Assert.True(pipe.Bob.TryPeek(out result));
                Assert.False(result.TransportClosed, "Transport should not be closed.");
                Assert.Equal(0, result.BytesReceived);

                byte[]? buffer = useEmptyBuffer ? default : new byte[1];
                Assert.True(pipe.Bob.TryReceive(buffer, out result));
                Assert.False(result.TransportClosed, "Transport should not be closed.");
                Assert.Equal(0, result.BytesReceived);

                AssertNoMoreData(pipe.Bob, buffer);
            });
        }

        [InlineData(100)]
        [InlineData(400)]
        [InlineData(256 * 100)]
        [Theory]
        public Task TestSinglePacketReceive(int packetSize)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { Mtu = 124, UpdateInterval = 30, SendWindow = 256, ReceiveWindow = 256, RemoteReceiveWindow = 256, NoDelay = true });

                byte[] packet = new byte[packetSize];
                Random.Shared.NextBytes(packet);

                await pipe.Alice.SendAsync(packet, cancellationToken);
                await Task.Delay(2000, cancellationToken);

                KcpConversationReceiveResult result;
                Assert.True(pipe.Bob.TryPeek(out result));
                Assert.False(result.TransportClosed, "Transport should not be closed.");
                Assert.Equal(packetSize, result.BytesReceived);

                byte[] buffer = new byte[result.BytesReceived];
                Assert.True(pipe.Bob.TryReceive(buffer, out result));
                Assert.False(result.TransportClosed, "Transport should not be closed.");
                Assert.Equal(packetSize, result.BytesReceived);

                Assert.True(buffer.AsSpan(0, result.BytesReceived).SequenceEqual(packet));

                AssertNoMoreData(pipe.Bob, buffer);
            });
        }

        [Fact]
        public Task TestMultiplePacketsReceive()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { Mtu = 200 });

                byte[] packet1 = new byte[100];
                byte[] packet2 = new byte[400];
                byte[] packet3 = new byte[1000];

                Random.Shared.NextBytes(packet1);
                Random.Shared.NextBytes(packet2);
                Random.Shared.NextBytes(packet3);

                await pipe.Alice.SendAsync(packet1, cancellationToken);
                await pipe.Alice.SendAsync(packet2, cancellationToken);
                await pipe.Alice.SendAsync(packet3, cancellationToken);

                await Task.Delay(2000);

                byte[] buffer = new byte[1200];
                AssertPacketReceived(pipe.Bob, packet1, buffer);
                AssertPacketReceived(pipe.Bob, packet2, buffer);
                AssertPacketReceived(pipe.Bob, packet3, buffer);

                AssertNoMoreData(pipe.Bob, buffer);
            });

            static void AssertPacketReceived(KcpConversation conversation, byte[] packet, byte[] buffer)
            {
                KcpConversationReceiveResult result;
                Assert.True(conversation.TryPeek(out result));
                Assert.False(result.TransportClosed, "Transport should not be closed.");
                Assert.Equal(packet.Length, result.BytesReceived);
                Assert.True(conversation.TryReceive(buffer, out result));
                Assert.False(result.TransportClosed, "Transport should not be closed.");
                Assert.Equal(packet.Length, result.BytesReceived);
                Assert.True(buffer.AsSpan(0, result.BytesReceived).SequenceEqual(packet));
            }
        }

        [Fact]
        public Task TestStreamSendReceive()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                const int mtu = 500;
                using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(new KcpConversationOptions { Mtu = mtu, StreamMode = true });

                byte[] stream = new byte[4000];
                Random.Shared.NextBytes(stream);

                await pipe.Alice.SendAsync(stream, cancellationToken);

                await Task.Delay(2000, cancellationToken);

                byte[] buffer = new byte[800];
                ReadOnlyMemory<byte> remaining = stream.AsMemory();
                KcpConversationReceiveResult result;

                while (!remaining.IsEmpty)
                {
                    Assert.True(pipe.Bob.TryPeek(out result));
                    Assert.False(result.TransportClosed, "Transport should not be closed.");

                    Assert.True(pipe.Bob.TryReceive(buffer, out result));
                    Assert.False(result.TransportClosed, "Transport should not be closed.");
                    Assert.NotEqual(0, result.BytesReceived);

                    Assert.True(result.BytesReceived <= remaining.Length);
                    Assert.True(buffer.AsSpan(0, result.BytesReceived).SequenceEqual(remaining.Span.Slice(0, result.BytesReceived)));
                    remaining = remaining.Slice(result.BytesReceived);
                }

                AssertNoMoreData(pipe.Bob, buffer);
            });
        }

        private static void AssertNoMoreData(KcpConversation conversation, byte[]? buffer = null)
        {
            buffer ??= new byte[1];

            KcpConversationReceiveResult result;
            Assert.False(conversation.TryPeek(out result));
            Assert.False(result.TransportClosed, "Transport should not be closed.");
            Assert.Equal(0, result.BytesReceived);

            Assert.False(conversation.TryReceive(buffer, out result));
            Assert.False(result.TransportClosed, "Transport should not be closed.");
            Assert.Equal(0, result.BytesReceived);
        }
    }
}
