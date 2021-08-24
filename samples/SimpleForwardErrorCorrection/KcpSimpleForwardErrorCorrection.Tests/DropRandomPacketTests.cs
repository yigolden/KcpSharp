using Xunit;

namespace KcpSimpleForwardErrorCorrection.Tests
{
    public class DropRandomPacketTests
    {
        [Theory]
        [InlineData(1, 0.7f, false)]
        [InlineData(2, 0.8f, false)]
        [InlineData(3, 0.9f, false)]
        [InlineData(4, 0.95f, false)]
        [InlineData(5, 0.98f, false)]
        [InlineData(1, 0.8f, true)]
        [InlineData(2, 0.9f, true)]
        public Task Test(int rank, float passRate, bool hasConversationId)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                using var aliceToBobTransport = new DropRandomPacketTransport(256, 42, passRate);
                using var bobToAliceTransport = new UnidirectionalTransport(256);

                await ErrorCorrectionTest.RunAsync(aliceToBobTransport, bobToAliceTransport, rank, hasConversationId, cancellationToken);
            });
        }

        class DropRandomPacketTransport : UnidirectionalTransport
        {
            private readonly Random _rand;
            private readonly float _passRate;

            public DropRandomPacketTransport(int capacity, int seed, float passRate) : base(capacity)
            {
                _rand = new Random(seed);
                _passRate = passRate;
            }

            protected override bool IsPacketAllowed(ReadOnlySpan<byte> packet)
            {
                return _rand.NextDouble() <= _passRate;
            }
        }
    }
}
