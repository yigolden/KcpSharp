namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public interface IKcpConnectionNegotiationContext
    {
        int? NegotiatedMtu { get; }
        bool TryGetSessionId(out uint sessionId);
        void SetSessionId(uint sessionId);
        KcpConnectionNegotiationResult PutNegotiationData(ReadOnlySpan<byte> data);
        KcpConnectionNegotiationResult GetNegotiationData(Span<byte> data);
    }
}
