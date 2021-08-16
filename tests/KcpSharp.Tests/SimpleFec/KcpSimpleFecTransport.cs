namespace KcpSharp.Tests.SimpleFec
{
    internal sealed class KcpSimpleFecTransport : IKcpTransport<KcpConversation>, IKcpConversation
    {
        private const int CommandType = 85;

        private readonly IKcpTransport _transport;
        private readonly bool _hasConversationId;
        private readonly KcpConversation _conversation;

        private readonly int _mtu;
        private readonly int _preBufferSize;
        private readonly int _postBufferSize;

        private KcpSimpleFecSendBuffer _sendBuffer;

        private Func<Exception, IKcpTransport<KcpConversation>, object?, bool>? _exceptionHandler;
        private object? _exceptionHandlerState;
        private bool? _started;

        public KcpSimpleFecTransport(IKcpTransport transport, int? conversationId, KcpConversationOptions? options, int rank)
        {
            _transport = transport;
            _hasConversationId = conversationId.HasValue;
            _conversation = conversationId.HasValue ? new KcpConversation(this, conversationId.GetValueOrDefault(), options) : new KcpConversation(this, options);

            _mtu = options?.Mtu ?? 1400;
            _preBufferSize = (options?.PreBufferSize ?? 0) + (conversationId.HasValue ? 4 : 0);
            _postBufferSize = options?.PostBufferSize ?? 0;

            _sendBuffer = new KcpSimpleFecSendBuffer(transport, rank, conversationId, _mtu, _preBufferSize, _postBufferSize, options?.BufferPool ?? DefaultArrayPoolBufferPool.Default);
        }

        public KcpConversation Connection => _conversation;

        public void SetExceptionHandler(Func<Exception, IKcpTransport<KcpConversation>, object?, bool> handler, object? state)
        {
            _exceptionHandler = handler;
            _exceptionHandlerState = state;
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
            _conversation.Dispose();
        }

        ValueTask IKcpTransport.SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
        {
            if (!_started.GetValueOrDefault())
            {
                return default;
            }

            return _sendBuffer.SendPacketAsync(packet, cancellationToken);
        }

        ValueTask IKcpConversation.InputPakcetAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            if (!_started.GetValueOrDefault())
            {
                return default;
            }

            // TODO process
            return default;
        }
    }
}
