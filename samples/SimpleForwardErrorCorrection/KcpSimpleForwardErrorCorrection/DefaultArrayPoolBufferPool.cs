using KcpSharp;

namespace KcpSimpleForwardErrorCorrection
{
    internal sealed class DefaultArrayPoolBufferPool : IKcpBufferPool
    {
        public static DefaultArrayPoolBufferPool Default { get; } = new DefaultArrayPoolBufferPool();

        public KcpRentedBuffer Rent(KcpBufferPoolRentOptions options)
        {
            return KcpRentedBuffer.FromSharedArrayPool(options.Size);
        }
    }
}
