using System.Diagnostics.CodeAnalysis;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public readonly struct KcpConnectionAuthenticationResult : IEquatable<KcpConnectionAuthenticationResult>
    {
        // failed, continue, success
        private readonly int _bytesWritten;
        private readonly byte _state; // 0-failed, 1-continue, 2-success

        public int BytesWritten => _bytesWritten;

        public bool IsFailed => _state == 0;
        public bool IsContinuationRequired => _state == 1;
        public bool IsSucceeded => _state == 2;

        public KcpConnectionAuthenticationResult(int bytesWritten)
        {
            _bytesWritten = bytesWritten;
            _state = 1;
        }

        private KcpConnectionAuthenticationResult(byte state, int bytesWritten)
        {
            _bytesWritten = bytesWritten;
            _state = state;
        }

        public static KcpConnectionAuthenticationResult Failed => new KcpConnectionAuthenticationResult(0, 0);
        public static KcpConnectionAuthenticationResult ContinuationRequired => new KcpConnectionAuthenticationResult(1, 0);
        public static KcpConnectionAuthenticationResult Succeeded => new KcpConnectionAuthenticationResult(2, 0);

        public bool Equals(KcpConnectionAuthenticationResult other)
            => other._bytesWritten == _bytesWritten && other._state == _state;

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is KcpConnectionAuthenticationResult other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_bytesWritten, _state);
    }
}
