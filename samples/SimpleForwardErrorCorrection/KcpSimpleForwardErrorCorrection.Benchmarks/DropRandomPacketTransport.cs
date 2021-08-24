using KcpSharp;

namespace KcpSimpleForwardErrorCorrection.Benchmarks
{
    internal sealed class DropRandomPacketTransport : UnidirectionalTransport
    {
        private readonly Random _rand;
        private float _passRate;

        public DropRandomPacketTransport(IKcpBufferPool bufferPool, int capacity, int seed) : base(bufferPool, capacity)
        {
            _rand = new Random(seed);
            _passRate = 1f;
        }

        public void SetPassRate(float passRate)
        {
            _passRate = passRate;
        }

        protected override bool IsPacketAllowed(ReadOnlySpan<byte> packet)
        {
            return _rand.NextDouble() <= _passRate;
        }
    }
}
