using System;

namespace KcpSharp.Tests
{
    internal static class KcpConversationFactory
    {
        public static KcpConversationPipe CreatePerfectPipe()
        {
            return new PerfectKcpConversationPipe(1, null, null);
        }

        public static KcpConversationPipe CreatePerfectPipe(KcpConversationOptions? options)
        {
            return new PerfectKcpConversationPipe(1, options, options);
        }

        public static KcpConversationPipe CreatePerfectPipe(KcpConversationOptions? aliceOptions, KcpConversationOptions? bobOptions)
        {
            return new PerfectKcpConversationPipe(1, aliceOptions, bobOptions);
        }

        public static KcpConversationPipe CreateBadPipe(BadOneWayConnectionOptions connectionOptions)
        {
            return new BadKcpConversationPipe(1, connectionOptions, null, null);
        }

        public static KcpConversationPipe CreateBadPipe(BadOneWayConnectionOptions connectionOptions, KcpConversationOptions? options)
        {
            return new BadKcpConversationPipe(1, connectionOptions, options, options);
        }

        public static KcpConversationPipe CreateBadPipe(BadOneWayConnectionOptions connectionOptions, KcpConversationOptions? aliceOptions, KcpConversationOptions? bobOptions)
        {
            return new BadKcpConversationPipe(1, connectionOptions, aliceOptions, bobOptions);
        }
    }

    internal abstract class KcpConversationPipe : IDisposable
    {
        public abstract KcpConversation Alice { get; }
        public abstract KcpConversation Bob { get; }

        public abstract void Dispose();
    }

}
