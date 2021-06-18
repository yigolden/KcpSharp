using System;

namespace KcpSharp
{
    internal static class ThrowHelper
    {
        public static Exception NewTransportClosedException()
        {
            return new KcpException("The transport has already been closed.");
        }
        public static Exception NewPacketTooLarge()
        {
            return new InvalidOperationException("Packet is too large.");
        }
        public static Exception NewBufferTooSmall()
        {
            return new InvalidOperationException("Buffer is too small.");
        }
        public static Exception NewConcurrentSendException()
        {
            return new InvalidOperationException("Concurrent send operations are not allowed.");
        }
        public static Exception NewConcurrentReceiveException()
        {
            return new InvalidOperationException("Concurrent receive operations are not allowed.");
        }
        public static Exception NewObjectDisposedExceptionForKcpConversation()
        {
            return new ObjectDisposedException(nameof(KcpConversation));
        }
    }
}
