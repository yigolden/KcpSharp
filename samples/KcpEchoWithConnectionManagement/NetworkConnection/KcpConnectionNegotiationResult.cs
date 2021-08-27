using System.Diagnostics.CodeAnalysis;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public readonly struct KcpConnectionNegotiationResult : IEquatable<KcpConnectionNegotiationResult>
    {
        // failed, continue, success
        private readonly int _bytesWritten;
        private readonly byte _state; // 0-failed, 1-continue, 2-success

        public int BytesWritten => _bytesWritten;

        public bool IsFailed => _state == 0;
        public bool IsContinuationRequired => _state == 1;
        public bool IsSucceeded => _state == 2;

        public KcpConnectionNegotiationResult(int bytesWritten)
        {
            _bytesWritten = bytesWritten;
            _state = 1;
        }

        private KcpConnectionNegotiationResult(byte state, int bytesWritten)
        {
            _bytesWritten = bytesWritten;
            _state = state;
        }

        public static KcpConnectionNegotiationResult Failed => new KcpConnectionNegotiationResult(0, 0);
        public static KcpConnectionNegotiationResult ContinuationRequired => new KcpConnectionNegotiationResult(1, 0);
        public static KcpConnectionNegotiationResult Succeeded => new KcpConnectionNegotiationResult(2, 0);

        public bool Equals(KcpConnectionNegotiationResult other)
            => other._bytesWritten == _bytesWritten && other._state == _state;

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is KcpConnectionNegotiationResult other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_bytesWritten, _state);
    }
}
