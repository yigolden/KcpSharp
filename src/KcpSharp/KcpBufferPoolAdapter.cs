namespace KcpSharp
{
#pragma warning disable CS0618 // IKcpBufferAllocator is obsolete
    internal sealed class KcpBufferPoolAdapter : IKcpBufferPool
    {
        private readonly IKcpBufferAllocator _allocator;

        public KcpBufferPoolAdapter(IKcpBufferAllocator allocator)
        {
            _allocator = allocator;
        }

        public KcpRentedBuffer Rent(KcpBufferPoolRentOptions options) => KcpRentedBuffer.FromMemoryOwner(_allocator.Allocate(options.Size));
    }
#pragma warning restore CS0618
}
