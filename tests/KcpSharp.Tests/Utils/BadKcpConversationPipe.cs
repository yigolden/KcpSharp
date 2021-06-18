using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace KcpSharp.Tests
{
    internal sealed class BadKcpConversationPipe : KcpConversationPipe
    {
        private readonly BadOneWayConnection _alice;
        private readonly BadOneWayConnection _bob;
        private readonly Channel<byte[]> _aliceToBobChannel;
        private readonly Channel<byte[]> _bobToAliceChannel;
        private readonly CancellationTokenSource _cts;

        public override KcpConversation Alice => _alice.Conversation;
        public override KcpConversation Bob => _bob.Conversation;

        public BadKcpConversationPipe(int conversationId, BadOneWayConnectionOptions connectionOptions, KcpConversationOptions? aliceOptions, KcpConversationOptions? bobOptions)
        {
            _aliceToBobChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(connectionOptions.ConcurrentCount)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropWrite,
            });
            _bobToAliceChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(connectionOptions.ConcurrentCount)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropWrite,
            });
            _alice = new BadOneWayConnection(conversationId, _aliceToBobChannel.Writer, connectionOptions, aliceOptions);
            _bob = new BadOneWayConnection(conversationId, _bobToAliceChannel.Writer, connectionOptions, bobOptions);
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

        public override void Dispose()
        {
            _cts.Cancel();
            _alice.CloseConnection();
            _bob.CloseConnection();
        }

    }

    internal class BadOneWayConnectionOptions
    {
        public double DropProbability { get; set; }
        public int BaseLatency { get; set; }
        public int RandomRelay { get; set; }
        public int ConcurrentCount { get; set; } = 4;
        public Random Random { get; set; } = Random.Shared;
    }

    internal class BadOneWayConnection : IKcpTransport
    {
        private KcpConversation _conversation;
        private readonly ChannelWriter<byte[]> _output;
        private readonly BadOneWayConnectionOptions _connectionOptions;
        private bool _closed;

        public BadOneWayConnection(int conversationId, ChannelWriter<byte[]> output, BadOneWayConnectionOptions connectionOptions, KcpConversationOptions? options = null)
        {
            _conversation = new KcpConversation(this, conversationId, options);
            _output = output;
            _connectionOptions = connectionOptions;
        }

        public KcpConversation Conversation => _conversation;

        public void CloseConnection()
        {
            _closed = true;
            _conversation.SetTransportClosed();
            _conversation.Dispose();
            _output.Complete();
        }

        ValueTask IKcpTransport.SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            double drop = _connectionOptions.Random.NextDouble();
            if (drop < _connectionOptions.DropProbability)
            {
                //Console.WriteLine("Pakcet dropped intentionally.");
                return default;
            }

            byte[] arr = packet.ToArray();
            _ = Task.Run(() => SendAsync(arr, cancellationToken));
            return default;
        }

        private async Task SendAsync(byte[] packet, CancellationToken cancellationToken)
        {
            await Task.Delay(_connectionOptions.BaseLatency + _connectionOptions.Random.Next(0, _connectionOptions.RandomRelay), cancellationToken);
            if (!_closed)
            {
                await _output.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task PutPacketAsync(byte[] packet, CancellationToken cancellationToken)
        {
            await _conversation.OnReceivedAsync(packet, cancellationToken).ConfigureAwait(false);
        }
    }


}
