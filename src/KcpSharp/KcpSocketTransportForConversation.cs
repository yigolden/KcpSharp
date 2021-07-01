using System;
using System.Net;
using System.Net.Sockets;

namespace KcpSharp
{
    /// <summary>
    /// Socket transport for KCP conversation.
    /// </summary>
    internal sealed class KcpSocketTransportForConversation : KcpSocketTransport<KcpConversation>, IKcpTransport<KcpConversation>
    {
        private readonly int? _conversationId;
        private readonly KcpConversationOptions? _options;

        private Func<Exception, IKcpTransport<KcpConversation>, object?, bool>? _exceptionHandler;
        private object? _exceptionHandlerState;


        internal KcpSocketTransportForConversation(Socket socket, EndPoint endPoint, int? conversationId, KcpConversationOptions? options)
            : base(socket, endPoint, options?.Mtu ?? KcpConversationOptions.MtuDefaultValue)
        {
            _conversationId = conversationId;
            _options = options;
        }

        protected override KcpConversation Activate() => _conversationId.HasValue ? new KcpConversation(this, _conversationId.GetValueOrDefault(), _options) : new KcpConversation(this, _options);

        protected override bool HandleException(Exception ex)
        {
            if (_exceptionHandler is not null)
            {
                return _exceptionHandler.Invoke(ex, this, _exceptionHandlerState);
            }
            return false;
        }

        public void SetExceptionHandler(Func<Exception, IKcpTransport<KcpConversation>, object?, bool> handler, object? state)
        {
            _exceptionHandler = handler;
            _exceptionHandlerState = state;
        }

    }
}
