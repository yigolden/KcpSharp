using KcpSharp;

namespace KcpSimpleForwardErrorCorrection
{
    public sealed class KcpSimpleFecTransport : IKcpTransport<KcpConversation>, IKcpConversation
    {
        private readonly KcpConversation _conversation;

        private readonly int _mtu;
        private readonly int _preBufferSize;
        private readonly int _postBufferSize;
        private readonly bool _hasConversationId;

        private KcpSimpleFecSendBuffer _sendBuffer;
        private KcpSimpleFecReceiveBuffer _receiveBuffer;

        private bool? _started;

        public KcpSimpleFecTransport(IKcpTransport transport, int? conversationId, KcpConversationOptions? options, int rank)
        {
            _conversation = conversationId.HasValue ? new KcpConversation(this, conversationId.GetValueOrDefault(), options) : new KcpConversation(this, options);

            _mtu = options?.Mtu ?? 1400;
            _preBufferSize = options?.PreBufferSize ?? 0;
            _postBufferSize = options?.PostBufferSize ?? 0;
            _hasConversationId = conversationId.HasValue;

            _sendBuffer = new KcpSimpleFecSendBuffer(transport, rank, conversationId, _mtu, _preBufferSize, _postBufferSize, options?.BufferPool ?? DefaultArrayPoolBufferPool.Default);
            _receiveBuffer = new KcpSimpleFecReceiveBuffer(_conversation, rank, conversationId, _mtu - _preBufferSize - _postBufferSize, options?.ReceiveWindow ?? 128, options?.BufferPool ?? DefaultArrayPoolBufferPool.Default);
        }

        public KcpConversation Connection => _conversation;

        public void SetExceptionHandler(Func<Exception, IKcpTransport<KcpConversation>, object?, bool> handler, object? state)
        {
            // Do nothing
        }

        public void Start()
        {
            _started = true;
        }

        public void SetTransportClosed()
        {
            _conversation.SetTransportClosed();
        }

        public void Dispose()
        {
            if (_started.HasValue && !_started.GetValueOrDefault())
            {
                return;
            }
            _started = false;
            _sendBuffer.Dispose();
            _receiveBuffer.Dispose();
            _conversation.Dispose();
        }

        ValueTask IKcpTransport.SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
        {
            if (!_started.GetValueOrDefault())
            {
                return default;
            }

            int prePacketSize = _preBufferSize;
            if (_hasConversationId)
            {
                prePacketSize += 4;
            }

            if (packet.Length >= (prePacketSize + _postBufferSize))
            {
                Span<byte> packetSpan = packet.Span;
                _receiveBuffer.NotifyPacketSent(packetSpan.Slice(prePacketSize, packetSpan.Length - prePacketSize - _postBufferSize));
            }

            return _sendBuffer.SendPacketAsync(packet, cancellationToken);
        }

        ValueTask IKcpConversation.InputPakcetAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            if (!_started.GetValueOrDefault())
            {
                return default;
            }

            return _receiveBuffer.InputPakcetAsync(packet, cancellationToken);
        }
    }
}
