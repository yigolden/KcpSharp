#nullable enable

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KcpSharp;

namespace KcpChatWasm
{
    internal sealed class PerfectKcpConversationPipe
    {
        private readonly PerfectOneWayConnection _alice;
        private readonly PerfectOneWayConnection _bob;
        private readonly Channel<byte[]> _aliceToBobChannel;
        private readonly Channel<byte[]> _bobToAliceChannel;
        private readonly CancellationTokenSource _cts;

        public KcpConversation Alice => _alice.Conversation;
        public KcpConversation Bob => _bob.Conversation;

        public PerfectKcpConversationPipe(uint conversationId, KcpConversationOptions? aliceOptions, KcpConversationOptions? bobOptions)
        {
            _aliceToBobChannel = Channel.CreateUnbounded<byte[]>();
            _bobToAliceChannel = Channel.CreateUnbounded<byte[]>();
            _alice = new PerfectOneWayConnection(conversationId, _aliceToBobChannel.Writer, aliceOptions);
            _bob = new PerfectOneWayConnection(conversationId, _bobToAliceChannel.Writer, bobOptions);
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

    internal class PerfectOneWayConnection : IKcpTransport
    {
        private KcpConversation _conversation;
        private readonly ChannelWriter<byte[]> _output;

        public PerfectOneWayConnection(uint conversationId, ChannelWriter<byte[]> output, KcpConversationOptions? options = null)
        {
            _conversation = new KcpConversation(this, (int)conversationId, options);
            _output = output;
        }

        public KcpConversation Conversation => _conversation;

        public void CloseConnection()
        {
            _conversation.SetTransportClosed();
            _conversation.Dispose();
        }

        async ValueTask IKcpTransport.SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
        {
            await _output.WriteAsync(packet.ToArray(), cancellationToken).ConfigureAwait(false);
        }

        public async Task PutPacketAsync(byte[] packet, CancellationToken cancellationToken)
        {
            await _conversation.InputPakcetAsync(packet, cancellationToken).ConfigureAwait(false);
        }
    }
}
