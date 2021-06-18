using System.Buffers;

namespace KcpSharp
{
    public interface IKcpBufferAllocator
    {
        public IMemoryOwner<byte> Allocate(int size);
    }
}
