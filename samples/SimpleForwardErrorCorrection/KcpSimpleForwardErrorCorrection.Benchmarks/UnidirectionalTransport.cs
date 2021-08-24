using KcpSharp;

namespace KcpSimpleForwardErrorCorrection.Benchmarks
{
    internal class UnidirectionalTransport : IKcpTransport, IDisposable
    {
        private readonly IKcpBufferPool _bufferPool;
        private readonly PreallocatedQueue<(KcpRentedBuffer Buffer, int Length)> _channel;
        private CancellationTokenSource? _cts;
        private IKcpConversation? _target;

        public UnidirectionalTransport(IKcpBufferPool bufferPool, int capacity)
        {
            _bufferPool = bufferPool;
            _channel = new PreallocatedQueue<(KcpRentedBuffer Buffer, int Length)>(capacity);
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PumpLoop(_cts.Token));
        }

        public void SetTarget(IKcpConversation target)
        {
            _target = target;
        }

        public ValueTask SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
        {
            KcpRentedBuffer buffer = _bufferPool.Rent(new KcpBufferPoolRentOptions(packet.Length, false));
            packet.Span.CopyTo(buffer.Span);
            _channel.TryWrite((buffer, packet.Length));
            return default;
        }

        private async Task PumpLoop(CancellationToken cancellationToken)
        {
            PreallocatedQueue<(KcpRentedBuffer Buffer, int Length)> channel = _channel;
            while (!cancellationToken.IsCancellationRequested)
            {
                (KcpRentedBuffer buffer, int length) = await channel.ReadAsync(cancellationToken);
                IKcpConversation? target = _target;
                if (target is not null)
                {
                    if (IsPacketAllowed(buffer.Span.Slice(0, length)))
                    {
                        await target.InputPakcetAsync(buffer.Memory.Slice(0, length), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        protected virtual bool IsPacketAllowed(ReadOnlySpan<byte> packet) => true;

        public void Dispose()
        {
            CancellationTokenSource? cts = Interlocked.Exchange(ref _cts, null);
            if (cts is not null)
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }
}
