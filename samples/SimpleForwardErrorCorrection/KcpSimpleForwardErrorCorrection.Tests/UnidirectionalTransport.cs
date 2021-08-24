using System.Threading.Channels;
using KcpSharp;

namespace KcpSimpleForwardErrorCorrection.Tests
{
    internal class UnidirectionalTransport : IKcpTransport, IDisposable
    {
        private readonly Channel<byte[]> _channel;
        private CancellationTokenSource? _cts;
        private IKcpConversation? _target;

        public UnidirectionalTransport(int capacity)
        {
            _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropWrite });
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PumpLoop(_cts.Token));
        }

        public void SetTarget(IKcpConversation target)
        {
            _target = target;
        }

        public ValueTask SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
            => _channel.Writer.WriteAsync(packet.ToArray(), cancellationToken);

        private async Task PumpLoop(CancellationToken cancellationToken)
        {
            ChannelReader<byte[]> reader = _channel.Reader;
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] packet = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                IKcpConversation? target = _target;
                if (target is not null)
                {
                    if (IsPacketAllowed(packet))
                    {
                        await target.InputPakcetAsync(packet, cancellationToken).ConfigureAwait(false);
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
