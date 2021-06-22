using System.Net;
using System.Net.Sockets;

namespace KcpSharp
{
    /// <summary>
    /// Socket transport for KCP conversation.
    /// </summary>
    internal sealed class KcpSocketTransportForConversation : KcpSocketTransport<KcpConversation>, IKcpTransport<KcpConversation>
    {
        private readonly int _conversationId;
        private readonly KcpConversationOptions? _options;

        internal KcpSocketTransportForConversation(Socket socket, EndPoint endPoint, int conversationId, KcpConversationOptions? options)
            : base(socket, endPoint, options?.Mtu ?? KcpConversationOptions.MtuDefaultValue)
        {
            _conversationId = conversationId;
            _options = options;
        }

        /// <inheritdoc />
        protected override KcpConversation Activate() => new KcpConversation(this, _conversationId, _options);
    }
}
