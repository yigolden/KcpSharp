using System;
using System.Buffers;

namespace KcpSharp
{
    /// <summary>
    /// The allocator used to allocate large chunks of memory.
    /// </summary>
    [Obsolete("IKcpBufferAllocator is deprecated because it has design flaws. Implement your custom allocator using IKcpBufferPool interface instead.")]
    public interface IKcpBufferAllocator
    {
        /// <summary>
        /// Allocate at least <paramref name="size"/> bytes of memory.
        /// </summary>
        /// <param name="size">The requested byte count.</param>
        /// <returns>The memory owner with memory allocated.</returns>
        public IMemoryOwner<byte> Allocate(int size);
    }
}
