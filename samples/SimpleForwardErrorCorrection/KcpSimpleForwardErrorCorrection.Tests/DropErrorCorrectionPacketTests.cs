using Xunit;

namespace KcpSimpleForwardErrorCorrection.Tests
{
    public class DropErrorCorrectionPacketTests
    {
        [Theory]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, false)]
        [InlineData(4, false)]
        [InlineData(5, false)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        public Task Test(int rank, bool hasConversationId)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using var aliceToBobTransport = new DropErrorCorrectionTransport(256, hasConversationId);
                using var bobToAliceTransport = new UnidirectionalTransport(256);

                await ErrorCorrectionTest.RunAsync(aliceToBobTransport, bobToAliceTransport, rank, hasConversationId, cancellationToken);
            });
        }

        class DropErrorCorrectionTransport : UnidirectionalTransport
        {
            private readonly bool _hasConversationId;

            public DropErrorCorrectionTransport(int capacity, bool hasConversationId) : base(capacity)
            {
                _hasConversationId = hasConversationId;
            }

            protected override bool IsPacketAllowed(ReadOnlySpan<byte> packet)
            {
                if (_hasConversationId)
                {
                    if (packet.Length < 4)
                    {
                        return false;
                    }
                    packet = packet.Slice(4);
                }
                return !packet.IsEmpty && packet[0] != 85;
            }
        }
    }
}
