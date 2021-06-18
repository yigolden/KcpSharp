using System;

namespace KcpSharp
{
    public readonly struct KcpConversationReceiveResult : IEquatable<KcpConversationReceiveResult>
    {
        private readonly int _bytesReceived;
        private readonly bool _connectionAlive;

        public int BytesReceived => _bytesReceived;
        public bool TransportClosed => !_connectionAlive;

        public KcpConversationReceiveResult(int bytesReceived)
        {
            _bytesReceived = bytesReceived;
            _connectionAlive = true;
        }

        public static bool operator ==(KcpConversationReceiveResult left, KcpConversationReceiveResult right) => left.Equals(right);
        public static bool operator !=(KcpConversationReceiveResult left, KcpConversationReceiveResult right) => !left.Equals(right);
        public bool Equals(KcpConversationReceiveResult other) => BytesReceived == other.BytesReceived && TransportClosed == other.TransportClosed;
        public override bool Equals(object? obj) => obj is KcpConversationReceiveResult other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(BytesReceived, TransportClosed);
    }
}
