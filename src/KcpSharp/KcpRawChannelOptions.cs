namespace KcpSharp
{
    /// <summary>
    /// Options used to control the behaviors of <see cref="KcpRawChannelOptions"/>.
    /// </summary>
    public sealed class KcpRawChannelOptions
    {
        /// <summary>
        /// The buffer allocator used to allocate large chunks of memory.
        /// </summary>
        public IKcpBufferAllocator? BufferAllocator { get; set; }

        /// <summary>
        /// The maximum packet size that can be transmitted over the underlying transport.
        /// </summary>
        public int Mtu { get; set; } = 1400;

        /// <summary>
        /// The number of packets in the receive queue.
        /// </summary>
        public int ReceiveQueueSize { get; set; } = 32;
    }
}
