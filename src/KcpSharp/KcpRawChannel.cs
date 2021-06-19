using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp
{
    public sealed class KcpRawChannel : IKcpConversation
    {
        private readonly IKcpBufferAllocator _allocator;
        private readonly IKcpTransport _transport;
        private readonly uint _id;
        private readonly int _mtu;

        private CancellationTokenSource? _sendLoopCts;
        private KcpRawReceiveQueue _receiveQueue;
        private KcpRawSendOperation _sendOperation;
        private AsyncAutoResetEvent<int> _sendNotification;

        public KcpRawChannel(IKcpTransport transport, int conversationId, KcpRawChannelOptions? options)
        {
            _allocator = options?.BufferAllocator ?? DefaultArrayPoolBufferAllocator.Default;
            _transport = transport;
            _id = (uint)conversationId;

            if (options is null)
            {
                _mtu = KcpConversationOptions.MtuDefaultValue;
            }
            else if (options.Mtu < 50)
            {
                throw new ArgumentException("MTU must be at least 50.", nameof(options));
            }
            else
            {
                _mtu = options.Mtu;
            }

            int queueSize = options?.ReceiveQueueSize ?? 32;
            if (queueSize < 1)
            {
                throw new ArgumentException("QueueSize must be a positive integer.", nameof(options));
            }

            _sendLoopCts = new CancellationTokenSource();
            _sendNotification = new AsyncAutoResetEvent<int>();
            _receiveQueue = new KcpRawReceiveQueue(_allocator, queueSize);
            _sendOperation = new KcpRawSendOperation(_sendNotification);

            _ = Task.Run(() => SendLoopAsync(_sendLoopCts));
        }

        public ValueTask<bool> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            => _sendOperation.SendAsync(buffer, cancellationToken);

        private async Task SendLoopAsync(CancellationTokenSource cts)
        {
            CancellationToken cancellationToken = cts.Token;
            KcpRawSendOperation sendOperation = _sendOperation;
            AsyncAutoResetEvent<int> ev = _sendNotification;
            int mss = _mtu - 4;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesCount = await ev.WaitAsync().ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (bytesCount < 0 || bytesCount > mss)
                    {
                        _ = sendOperation.TryConsume(default, out _);
                        continue;
                    }

                    int packetSize = bytesCount + 4;
                    {
                        using IMemoryOwner<byte> owner = _allocator.Allocate(packetSize);
                        Memory<byte> memory = owner.Memory;
                        BinaryPrimitives.WriteUInt32LittleEndian(memory.Span, _id);
                        if (sendOperation.TryConsume(memory.Slice(4), out int bytesWritten))
                        {
                            packetSize = Math.Min(packetSize, bytesWritten + 4);
                            await _transport.SendPacketAsync(memory.Slice(0, packetSize), cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Do nothing
            }
            finally
            {
                cts.Dispose();
            }
        }


        ValueTask IKcpConversation.OnReceivedAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            ReadOnlySpan<byte> span = packet.Span;
            if (span.Length < 4 || span.Length > _mtu)
            {
                return default;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(span) != _id)
            {
                return default;
            }
            _receiveQueue.Enqueue(span.Slice(4));
            return default;
        }

        public bool TryPeek(out int packetSize)
            => _receiveQueue.TryPeek(out packetSize);

        public ValueTask<KcpConversationReceiveResult> WaitToReceiveAsync(CancellationToken cancellationToken)
            => _receiveQueue.WaitToReceiveAsync(cancellationToken);

        public ValueTask<KcpConversationReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            => _receiveQueue.ReceiveAsync(buffer, cancellationToken);

        public void SetTransportClosed()
        {
            Interlocked.Exchange(ref _sendLoopCts, null)?.Cancel();
            _receiveQueue.SetTransportClosed();
            _sendOperation.SetTransportClosed();
            _sendNotification.Set(0);
        }

        public void Dispose()
        {
            SetTransportClosed();
            _receiveQueue.Dispose();
            _sendOperation.Dispose();
        }
    }
}
