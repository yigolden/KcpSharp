namespace KcpEchoWithConnectionManagement.NetworkConnection2
{
    public interface IKcpNetworkConnectionCallback<T>
    {
        ValueTask PacketReceivedAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
        void NotifyStateChanged(KcpNetworkConnection connection, T state);
    }
}
