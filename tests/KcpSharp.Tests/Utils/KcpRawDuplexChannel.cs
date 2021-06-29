using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace KcpSharp.Tests
{
    internal static class KcpRawDuplexChannelFactory
    {
        public static KcpRawDuplexChannel CreateDuplexChannel()
        {
            return new KcpRawDuplexChannel(1, null, null);
        }

        public static KcpRawDuplexChannel CreateDuplexChannel(KcpRawChannelOptions? options)
        {
            return new KcpRawDuplexChannel(1, options, options);
        }

        public static KcpRawDuplexChannel CreateDuplexChannel(KcpRawChannelOptions? aliceOptions, KcpRawChannelOptions? bobOptions)
        {
            return new KcpRawDuplexChannel(1, aliceOptions, bobOptions);
        }

    }

    internal class KcpRawDuplexChannel : IDisposable
    {
        private readonly KcpRawOneWayChannel _alice;
        private readonly KcpRawOneWayChannel _bob;
        private readonly Channel<byte[]> _aliceToBobChannel;
        private readonly Channel<byte[]> _bobToAliceChannel;
        private readonly CancellationTokenSource _cts;

        public KcpRawChannel Alice => _alice.Conversation;
        public KcpRawChannel Bob => _bob.Conversation;

        public KcpRawDuplexChannel(uint conversationId, KcpRawChannelOptions? aliceOptions, KcpRawChannelOptions? bobOptions)
        {
            _aliceToBobChannel = Channel.CreateUnbounded<byte[]>();
            _bobToAliceChannel = Channel.CreateUnbounded<byte[]>();
            _alice = new KcpRawOneWayChannel(conversationId, _aliceToBobChannel.Writer, aliceOptions);
            _bob = new KcpRawOneWayChannel(conversationId, _bobToAliceChannel.Writer, bobOptions);
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PipeFromAliceToBob(_cts.Token));
            _ = Task.Run(() => PipeFromBobToAlice(_cts.Token));
        }

        private async Task PipeFromAliceToBob(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] packet = await _aliceToBobChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                await _bob.PutPacketAsync(packet, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task PipeFromBobToAlice(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] packet = await _bobToAliceChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                await _alice.PutPacketAsync(packet, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _alice.CloseConnection();
            _bob.CloseConnection();
        }
    }

    internal class KcpRawOneWayChannel : IKcpTransport
    {
        private readonly KcpRawChannel _conversation;
        private readonly ChannelWriter<byte[]> _output;

        public KcpRawOneWayChannel(uint conversationId, ChannelWriter<byte[]> output, KcpRawChannelOptions? options = null)
        {
            _conversation = new KcpRawChannel(this, (int)conversationId, options);
            _output = output;
        }

        public KcpRawChannel Conversation => _conversation;

        public void CloseConnection()
        {
            _conversation.SetTransportClosed();
        }

        async ValueTask IKcpTransport.SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            await _output.WriteAsync(packet.ToArray(), cancellationToken).ConfigureAwait(false);
        }

        public async Task PutPacketAsync(byte[] packet, CancellationToken cancellationToken)
        {
            await ((IKcpConversation)_conversation).InputPakcetAsync(packet, cancellationToken).ConfigureAwait(false);
        }
    }
}
