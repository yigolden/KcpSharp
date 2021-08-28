namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public interface IKcpConnectionKeepAliveContext
    {
        void UpdateSample(uint packetsSent, uint packetsAcknowledged, ReadOnlySpan<byte> payload);
        byte PreparePayload(Span<byte> buffer);
    }
}
