using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace KcpSharp.Benchmarks
{
    internal sealed class PerfectKcpConversationPipe : IDisposable
    {
        private readonly PerfectOneWayConnection _alice;
        private readonly PerfectOneWayConnection _bob;
        private readonly Channel<(KcpRentedBuffer Owner, int Length)> _aliceToBobChannel;
        private readonly Channel<(KcpRentedBuffer Owner, int Length)> _bobToAliceChannel;
        private readonly CancellationTokenSource _cts;

        public KcpConversation Alice => _alice.Conversation;
        public KcpConversation Bob => _bob.Conversation;

        public PerfectKcpConversationPipe(IKcpBufferPool bufferPool, int mtu, int capacity, KcpConversationOptions? options)
        {
            var channelOptions = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleWriter = true,
                SingleReader = true,
            };
            _aliceToBobChannel = Channel.CreateBounded<(KcpRentedBuffer Owner, int Length)>(channelOptions);
            _bobToAliceChannel = Channel.CreateBounded<(KcpRentedBuffer Owner, int Length)>(channelOptions);
            _alice = new PerfectOneWayConnection(bufferPool, mtu, _aliceToBobChannel.Writer, options);
            _bob = new PerfectOneWayConnection(bufferPool, mtu, _bobToAliceChannel.Writer, options);
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PipeFromAliceToBob(_cts.Token));
            _ = Task.Run(() => PipeFromBobToAlice(_cts.Token));
        }

        private async Task PipeFromAliceToBob(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                (KcpRentedBuffer owner, int length) = await _aliceToBobChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                await _bob.PutPacketAsync(owner.Memory.Slice(0, length), cancellationToken).ConfigureAwait(false);
                owner.Dispose();
            }
        }

        private async Task PipeFromBobToAlice(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                (KcpRentedBuffer owner, int length) = await _bobToAliceChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                await _alice.PutPacketAsync(owner.Memory.Slice(0, length), cancellationToken).ConfigureAwait(false);
                owner.Dispose();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _alice.CloseConnection();
            _bob.CloseConnection();
        }
    }

    internal class PerfectOneWayConnection : IKcpTransport
    {
        private readonly IKcpBufferPool _bufferPool;
        private readonly int _mtu;
        private readonly ChannelWriter<(KcpRentedBuffer Owner, int Length)> _output;
        private readonly KcpConversation _conversation;

        public PerfectOneWayConnection(IKcpBufferPool bufferPool, int mtu, ChannelWriter<(KcpRentedBuffer Owner, int Length)> output, KcpConversationOptions? options = null)
        {
            _bufferPool = bufferPool;
            _mtu = mtu;
            _output = output;
            _conversation = new KcpConversation(this, options);
        }

        public KcpConversation Conversation => _conversation;

        public void CloseConnection()
        {
            _conversation.SetTransportClosed();
            _conversation.Dispose();
        }

        ValueTask IKcpTransport.SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
        {
            int mtu = _mtu;
            if (packet.Length > mtu)
            {
                return default;
            }
            KcpRentedBuffer owner = _bufferPool.Rent(new KcpBufferPoolRentOptions(mtu, false));
            packet.CopyTo(owner.Memory);
            if (!_output.TryWrite((owner, packet.Length)))
            {
                owner.Dispose();
            }
            return default;
        }

        public ValueTask PutPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
            => _conversation.InputPakcetAsync(packet, cancellationToken);
    }
}
