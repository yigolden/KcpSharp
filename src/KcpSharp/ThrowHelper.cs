using System;

namespace KcpSharp
{
    internal static class ThrowHelper
    {
        public static Exception NewMessageTooLargeForBufferArgument()
        {
            return new ArgumentException("Message is too large.", "buffer");
        }
        public static Exception NewBufferTooSmallForBufferArgument()
        {
            return new ArgumentException("Buffer is too small.", "buffer");
        }
        public static Exception ThrowBufferTooSmall()
        {
            throw new ArgumentException("Buffer is too small.", "buffer");
        }
        public static Exception NewConcurrentSendException()
        {
            return new InvalidOperationException("Concurrent send operations are not allowed.");
        }
        public static Exception NewConcurrentReceiveException()
        {
            return new InvalidOperationException("Concurrent receive operations are not allowed.");
        }
        public static void ThrowConcurrentReceiveException()
        {
            throw new InvalidOperationException("Concurrent receive operations are not allowed.");
        }
    }
}
