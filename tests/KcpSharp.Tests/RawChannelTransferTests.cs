using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KcpSharp.Tests
{
    public class RawChannelTransferTests
    {
        [InlineData(64, 1000, 1200, false)]
        [InlineData(64, 1000, 1200, true)]
        [Theory]
        public Task TestMultiplePacketSendReceive(int packetCount, int minPacketSize, int maxPacketSize, bool waitToReceive)
        {
            List<byte[]> packets = new(
               Enumerable.Range(1, packetCount)
               .Select(i => new byte[Random.Shared.Next(minPacketSize, maxPacketSize + 1)])
               );
            foreach (byte[] packet in packets)
            {
                Random.Shared.NextBytes(packet);
            }

            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(40), async cancellationToken =>
            {
                using KcpRawDuplexChannel pipe = KcpRawDuplexChannelFactory.CreateDuplexChannel(0x12345678, new KcpRawChannelOptions { ReceiveQueueSize = packetCount });

                Assert.False(pipe.Bob.TryPeek(out KcpConversationReceiveResult result));
                Assert.False(result.TransportClosed, "Transport should not be closed.");
                Assert.Equal(0, result.BytesReceived);

                Task sendTask = SendMultplePacketsAsync(pipe.Alice, packets, cancellationToken);
                Task receiveTask = ReceiveMultiplePacketsAsync(pipe.Bob, packets, waitToReceive, cancellationToken);
                await Task.WhenAll(sendTask, receiveTask);

                await AssertReceiveNoDataAsync(pipe.Bob, waitToReceive, new byte[1400]);
            });


            static async Task SendMultplePacketsAsync(KcpRawChannel conversation, IEnumerable<byte[]> packets, CancellationToken cancellationToken)
            {
                foreach (byte[] packet in packets)
                {
                    Assert.True(await conversation.SendAsync(packet, cancellationToken));
                    await Task.Delay(50, cancellationToken);
                }
            }

            static async Task ReceiveMultiplePacketsAsync(KcpRawChannel conversation, IEnumerable<byte[]> packets, bool waitToReceive, CancellationToken cancellationToken)
            {
                byte[] buffer = new byte[1400];
                foreach (byte[] packet in packets)
                {
                    Assert.True(packet.Length <= buffer.Length);
                    KcpConversationReceiveResult result;
                    if (waitToReceive)
                    {
                        result = await conversation.WaitToReceiveAsync(cancellationToken);
                        Assert.False(result.TransportClosed, "Transport should not be closed.");
                        Assert.Equal(packet.Length, result.BytesReceived);

                        Assert.True(conversation.TryPeek(out result));
                        Assert.False(result.TransportClosed, "Transport should not be closed.");
                        Assert.Equal(packet.Length, result.BytesReceived);
                    }

                    result = await conversation.ReceiveAsync(buffer, cancellationToken);
                    Assert.False(result.TransportClosed, "Transport should not be closed.");
                    Assert.Equal(packet.Length, result.BytesReceived);
                    Assert.True(buffer.AsSpan(0, result.BytesReceived).SequenceEqual(packet));
                }
            }
        }

        private static async Task AssertReceiveNoDataAsync(KcpRawChannel channel, bool waitToReceive, byte[]? buffer = null)
        {
            buffer = buffer ?? new byte[1];

            if (waitToReceive)
            {
                using var cts = new CancellationTokenSource(1000);
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await channel.WaitToReceiveAsync(cts.Token));
            }

            {
                using var cts = new CancellationTokenSource(1000);
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await channel.ReceiveAsync(buffer, cts.Token));
            }
        }

    }
}
