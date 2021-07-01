using System;

namespace KcpSharp.Tests
{
    internal static class KcpConversationFactory
    {
        public static KcpConversationPipe CreatePerfectPipe(uint conversationId)
        {
            return new PerfectKcpConversationPipe(conversationId, null, null);
        }

        public static KcpConversationPipe CreatePerfectPipe(uint conversationId, KcpConversationOptions? options)
        {
            return new PerfectKcpConversationPipe(conversationId, options, options);
        }

        public static KcpConversationPipe CreatePerfectPipe()
        {
            return new PerfectKcpConversationPipe(null, null, null);
        }

        public static KcpConversationPipe CreatePerfectPipe(KcpConversationOptions? options)
        {
            return new PerfectKcpConversationPipe(null, options, options);
        }

        public static KcpConversationPipe CreateBadPipe(uint conversationId, BadOneWayConnectionOptions connectionOptions)
        {
            return new BadKcpConversationPipe(conversationId, connectionOptions, null, null);
        }

        public static KcpConversationPipe CreateBadPipe(uint conversationId, BadOneWayConnectionOptions connectionOptions, KcpConversationOptions? options)
        {
            return new BadKcpConversationPipe(conversationId, connectionOptions, options, options);
        }
    }

    internal abstract class KcpConversationPipe : IDisposable
    {
        public abstract KcpConversation Alice { get; }
        public abstract KcpConversation Bob { get; }

        public abstract void Dispose();
    }

}
