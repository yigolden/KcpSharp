using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KcpSharp;

namespace KcpEcho
{
    internal class KcpEchoService : IUdpService, IKcpTransport, IDisposable
    {
        private readonly IUdpServiceDispatcher _sender;
        private readonly EndPoint _endPoint;
        private readonly KcpConversation _conversation;
        private readonly int _mtu;
        private CancellationTokenSource? _cts;

        public KcpEchoService(IUdpServiceDispatcher sender, EndPoint endPoint, KcpConversationOptions options, uint conversationId)
        {
            _sender = sender;
            _endPoint = endPoint;
            _conversation = new KcpConversation(this, (int)conversationId, options);
            _mtu = options.Mtu;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoop(_cts));
            Console.WriteLine($"{DateTime.Now:O}: Connected from {endPoint}");
        }

        ValueTask IKcpTransport.SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
            => _sender.SendPacketAsync(_endPoint, packet, cancellationToken);

        ValueTask IUdpService.InputPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
            => _conversation.OnReceivedAsync(packet, cancellationToken);

        void IUdpService.SetTransportClosed()
            => _conversation.SetTransportClosed();


        private async Task ReceiveLoop(CancellationTokenSource cts)
        {
            CancellationToken cancellationToken = cts.Token;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    KcpConversationReceiveResult result = await _conversation.WaitToReceiveAsync(cancellationToken);
                    if (result.TransportClosed)
                    {
                        break;
                    }

                    byte[] buffer = ArrayPool<byte>.Shared.Rent(result.BytesReceived);
                    try
                    {
                        result = await _conversation.ReceiveAsync(buffer, cancellationToken);
                        if (result.TransportClosed)
                        {
                            break;
                        }

                        Console.WriteLine($"Message received from {_endPoint}. Length = {result.BytesReceived} bytes.");

                        if (await _conversation.SendAsync(buffer.AsMemory(0, result.BytesReceived), cancellationToken))
                        {
                            break;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
            finally
            {
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
