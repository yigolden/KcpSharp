using KcpSharp;
using Xunit;

namespace KcpSimpleForwardErrorCorrection.Tests
{
    internal static class ErrorCorrectionTest
    {
        public static async Task RunAsync(UnidirectionalTransport aliceToBobTransport, UnidirectionalTransport bobToAliceTransport, int rank, bool hasConversationId, CancellationToken cancellationToken)
        {
            var allocator = new TrackedBufferAllocator();

            var options = new KcpConversationOptions
            {
                BufferPool = allocator,
                SendWindow = 256,
                ReceiveWindow = 256,
                RemoteReceiveWindow = 256,
                UpdateInterval = 30,
                NoDelay = true
            };

            {
                using var aliceTransport = new KcpSimpleFecTransport(aliceToBobTransport, hasConversationId ? 0x12345678 : null, options, rank);
                using var bobTransport = new KcpSimpleFecTransport(bobToAliceTransport, hasConversationId ? 0x12345678 : null, options, rank);

                aliceToBobTransport.SetTarget(bobTransport);
                bobToAliceTransport.SetTarget(aliceTransport);

                aliceTransport.Start();
                bobTransport.Start();

                KcpConversation alice = aliceTransport.Connection;
                KcpConversation bob = bobTransport.Connection;

                byte[] sourceData = new byte[1350 * 192];
                byte[] destinationBuffer = new byte[sourceData.Length + 1];

                Random.Shared.NextBytes(sourceData);
                Task<bool> sendTask = alice.SendAsync(sourceData, cancellationToken).AsTask();
                KcpConversationReceiveResult receiveResult = await bob.ReceiveAsync(destinationBuffer, cancellationToken);

                Assert.False(receiveResult.TransportClosed);
                Assert.Equal(sourceData.Length, receiveResult.BytesReceived);
                Assert.True(await sendTask);

                Assert.True(destinationBuffer.AsSpan(0, sourceData.Length).SequenceEqual(sourceData));

                Assert.True(await alice.FlushAsync(cancellationToken));
                Assert.True(await bob.FlushAsync(cancellationToken));
            }

            await Task.Delay(200, cancellationToken);

            Assert.Equal(0, allocator.InuseBufferCount);
        }
    }

}
