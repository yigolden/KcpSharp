namespace KcpSharp
{
    public sealed class KcpRawChannelOptions
    {
        public IKcpBufferAllocator? BufferAllocator { get; set; }
        public int Mtu { get; set; } = 1400;
        public int ReceiveQueueSize { get; set; } = 32;
    }
}
