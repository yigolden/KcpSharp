using System.Buffers.Binary;
using Xunit;

namespace KcpSimpleForwardErrorCorrection.Tests
{
    public class DropLastDataPacketTests
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
            return TestHelper.RunWithTimeout(TimeSpan.FromHours(10), async cancellationToken =>
            {
                using var aliceToBobTransport = new DropLastDataPacketTransport(256, rank, hasConversationId);
                using var bobToAliceTransport = new UnidirectionalTransport(256);

                await ErrorCorrectionTest.RunAsync(aliceToBobTransport, bobToAliceTransport, rank, hasConversationId, cancellationToken);
            });
        }

        class DropLastDataPacketTransport : UnidirectionalTransport
        {
            private readonly uint _mask;
            private readonly bool _hasConversationId;

            public DropLastDataPacketTransport(int capacity, int rank, bool hasConversationId) : base(capacity)
            {
                _mask = (1u << rank) - 1;
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

                return packet.Length >= 20 && (packet[0] != 81 || (BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(8)) & _mask) != _mask);
            }
        }
    }
}
