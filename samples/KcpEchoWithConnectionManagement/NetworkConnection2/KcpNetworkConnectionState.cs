namespace KcpEchoWithConnectionManagement.NetworkConnection2
{
    public enum KcpNetworkConnectionState
    {
        None = 0,
        Connecting = 1,
        Connected = 2,
        Failed = 3,
        Dead = 4,
    }
}
