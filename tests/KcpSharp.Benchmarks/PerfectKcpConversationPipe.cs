using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp.Benchmarks
{
    internal sealed class PerfectKcpConversationPipe : IDisposable
    {
        private readonly PerfectOneWayConnection _alice;
        private readonly PerfectOneWayConnection _bob;
        private readonly PreallocatedQueue<(KcpRentedBuffer Owner, int Length)> _aliceToBobChannel;
        private readonly PreallocatedQueue<(KcpRentedBuffer Owner, int Length)> _bobToAliceChannel;
        private readonly CancellationTokenSource _cts;

        public KcpConversation Alice => _alice.Conversation;
        public KcpConversation Bob => _bob.Conversation;

        public PerfectKcpConversationPipe(IKcpBufferPool bufferPool, int mtu, int capacity, KcpConversationOptions? options)
        {
            _aliceToBobChannel = new PreallocatedQueue<(KcpRentedBuffer Owner, int Length)>(capacity);
            _bobToAliceChannel = new PreallocatedQueue<(KcpRentedBuffer Owner, int Length)>(capacity);
            _alice = new PerfectOneWayConnection(bufferPool, mtu, _aliceToBobChannel, options);
            _bob = new PerfectOneWayConnection(bufferPool, mtu, _bobToAliceChannel, options);
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PipeFromAliceToBob(_cts.Token));
            _ = Task.Run(() => PipeFromBobToAlice(_cts.Token));
        }

        private async Task PipeFromAliceToBob(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                (KcpRentedBuffer owner, int length) = await _aliceToBobChannel.ReadAsync(cancellationToken).ConfigureAwait(false);
                await _bob.PutPacketAsync(owner.Memory.Slice(0, length), cancellationToken).ConfigureAwait(false);
                owner.Dispose();
            }
        }

        private async Task PipeFromBobToAlice(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                (KcpRentedBuffer owner, int length) = await _bobToAliceChannel.ReadAsync(cancellationToken).ConfigureAwait(false);
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
        private readonly PreallocatedQueue<(KcpRentedBuffer Owner, int Length)> _channel;
        private readonly KcpConversation _conversation;

        public PerfectOneWayConnection(IKcpBufferPool bufferPool, int mtu, PreallocatedQueue<(KcpRentedBuffer Owner, int Length)> channel, KcpConversationOptions? options = null)
        {
            _bufferPool = bufferPool;
            _mtu = mtu;
            _channel = channel;
            _conversation = new KcpConversation(this, options);
        }

        public KcpConversation Conversation => _conversation;

        public void CloseConnection()
        {
            _conversation.SetTransportClosed();
            _conversation.Dispose();
        }

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
        async ValueTask IKcpTransport.SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
        {
            await Task.Yield(); // The purpose is to simulate async writing so that we can validate our customized async method builder works.
            if (packet.Length > _mtu)
            {
                return;
            }
            KcpRentedBuffer owner = _bufferPool.Rent(new KcpBufferPoolRentOptions(_mtu, false));
            packet.CopyTo(owner.Memory);
            if (!_channel.TryWrite((owner, packet.Length)))
            {
                owner.Dispose();
            }
        }

        public ValueTask PutPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
            => _conversation.InputPakcetAsync(packet, cancellationToken);
    }
}
