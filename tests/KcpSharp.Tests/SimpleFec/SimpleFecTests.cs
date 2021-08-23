using System.Buffers.Binary;
using System.Threading.Channels;
using Xunit;

namespace KcpSharp.Tests.SimpleFec
{
    public class SimpleFecTests
    {

        [Fact]
        public async Task Test1()
        {
            const int Rank = 1;

            using var aliceToBobTransport = new DropLastDataPacketTransport(128, Rank);
            using var bobToAliceTransport = new UnidirectionalTransport(128);

            var options = new KcpConversationOptions
            {
                SendWindow = 128,
                ReceiveWindow = 128,
                RemoteReceiveWindow = 128,
                UpdateInterval = 30,
            };

            using var aliceTransport = new KcpSimpleFecTransport(aliceToBobTransport, null, options, Rank);
            using var bobTransport = new KcpSimpleFecTransport(bobToAliceTransport, null, options, Rank);

            aliceToBobTransport.SetTarget(bobTransport);
            bobToAliceTransport.SetTarget(aliceTransport);

            aliceTransport.Start();
            bobTransport.Start();

            KcpConversation alice = aliceTransport.Connection;
            KcpConversation bob = bobTransport.Connection;


            byte[] sourceData = new byte[1300 * 96];
            byte[] destinationBuffer = new byte[1300 * 96 + 1];
            Random.Shared.NextBytes(sourceData);
            Task<bool> sendTask = alice.SendAsync(sourceData).AsTask();
            KcpConversationReceiveResult receiveResult = await bob.ReceiveAsync(destinationBuffer);

            Assert.False(receiveResult.TransportClosed);
            Assert.Equal(sourceData.Length, receiveResult.BytesReceived);
            Assert.True(await sendTask);

            Assert.True(destinationBuffer.AsSpan(0, sourceData.Length).SequenceEqual(sourceData));
        }

        class DropLastDataPacketTransport : UnidirectionalTransport
        {
            private readonly uint _mask;

            public DropLastDataPacketTransport(int capacity, int rank) : base(capacity)
            {
                _mask = (1u << rank) - 1;
            }

            protected override bool IsPacketAllow(ReadOnlySpan<byte> packet) => packet.Length >= 20 && (packet[0] != 81 || (BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(8)) & _mask) != _mask);
        }

        class DropErrorCorrectionTransport : UnidirectionalTransport
        {
            public DropErrorCorrectionTransport(int capacity) : base(capacity) { }

            protected override bool IsPacketAllow(ReadOnlySpan<byte> packet) => !packet.IsEmpty && packet[0] != 85;
        }

        class UnidirectionalTransport : IKcpTransport, IDisposable
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
                        if (IsPacketAllow(packet))
                        {
                            await target.InputPakcetAsync(packet, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }

            protected virtual bool IsPacketAllow(ReadOnlySpan<byte> packet) => true;

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
}
