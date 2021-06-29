using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp.ThroughputBanchmarks.PacketsThroughputBenchmark
{
    internal sealed class PacketsThroughputBenchmarkService : IUdpService, IKcpTransport, IDisposable
    {
        private readonly IUdpServiceDispatcher _sender;
        private readonly EndPoint _endPoint;
        private readonly KcpConversation _conversation;
        private readonly int _mtu;
        private CancellationTokenSource? _cts;

        public PacketsThroughputBenchmarkService(IUdpServiceDispatcher sender, EndPoint endPoint, KcpConversationOptions options)
        {
            _sender = sender;
            _endPoint = endPoint;
            _conversation = new KcpConversation(this, 0, options);
            _mtu = options.Mtu;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoop(_cts));
            Console.WriteLine($"{DateTime.Now:O}: Connected from {endPoint}");
        }

        ValueTask IKcpTransport.SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
            => _sender.SendPacketAsync(_endPoint, packet, cancellationToken);

        ValueTask IUdpService.InputPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
            => _conversation.InputPakcetAsync(packet, cancellationToken);

        void IUdpService.SetTransportClosed()
            => _conversation.SetTransportClosed();

        private async Task ReceiveLoop(CancellationTokenSource cts)
        {
            CancellationToken cancellationToken = cts.Token;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(_mtu);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    KcpConversationReceiveResult result = await _conversation.ReceiveAsync(buffer, cancellationToken);
                    if (result.TransportClosed)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Do nothing
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                cts.Dispose();
            }
        }

        public void Dispose()
        {
            Console.WriteLine($"{DateTime.Now:O}: Connection from {_endPoint} eliminated.");
            Interlocked.Exchange(ref _cts, null)?.Dispose();
            _conversation.Dispose();
        }
    }
}
