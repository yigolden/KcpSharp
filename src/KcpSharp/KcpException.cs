using System;

namespace KcpSharp
{
    public sealed class KcpException : Exception
    {
        public KcpException() { }
        public KcpException(string? message) : base(message) { }
        public KcpException(string? message, Exception? innerException) : base(message, innerException) { }
    }
}
