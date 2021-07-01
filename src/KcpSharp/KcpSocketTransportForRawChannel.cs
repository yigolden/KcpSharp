using System;
using System.Net;
using System.Net.Sockets;

namespace KcpSharp
{
    internal sealed class KcpSocketTransportForRawChannel : KcpSocketTransport<KcpRawChannel>, IKcpTransport<KcpRawChannel>
    {
        private readonly int? _conversationId;
        private readonly KcpRawChannelOptions? _options;

        private Func<Exception, IKcpTransport<KcpRawChannel>, object?, bool>? _exceptionHandler;
        private object? _exceptionHandlerState;


        internal KcpSocketTransportForRawChannel(Socket socket, EndPoint endPoint, int? conversationId, KcpRawChannelOptions? options)
            : base(socket, endPoint, options?.Mtu ?? KcpConversationOptions.MtuDefaultValue)
        {
            _conversationId = conversationId;
            _options = options;
        }

        protected override KcpRawChannel Activate() => _conversationId.HasValue ? new KcpRawChannel(this, _conversationId.GetValueOrDefault(), _options) : new KcpRawChannel(this, _options);

        protected override bool HandleException(Exception ex)
        {
            if (_exceptionHandler is not null)
            {
                return _exceptionHandler.Invoke(ex, this, _exceptionHandlerState);
            }
            return false;
        }

        public void SetExceptionHandler(Func<Exception, IKcpTransport<KcpRawChannel>, object?, bool> handler, object? state)
        {
            _exceptionHandler = handler;
            _exceptionHandlerState = state;
        }
    }
}
