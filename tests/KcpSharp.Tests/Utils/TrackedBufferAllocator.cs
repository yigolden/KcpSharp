using System;
using System.Buffers;
using System.Threading;

namespace KcpSharp.Tests
{
    internal sealed class TrackedBufferAllocator : IKcpBufferPool
    {
        private int _inuseBufferCount;

        public KcpRentedBuffer Rent(KcpBufferPoolRentOptions options)
        {
            Interlocked.Increment(ref _inuseBufferCount);
            return KcpRentedBuffer.FromMemoryOwner(new TrackedBufferOwner(this, options.Size));
        }

        internal void Return()
        {
            Interlocked.Decrement(ref _inuseBufferCount);
        }

        public int InuseBufferCount => _inuseBufferCount;
    }

    internal sealed class TrackedBufferOwner : IMemoryOwner<byte>
    {
        private readonly TrackedBufferAllocator _allocator;
        private bool _isFreed;
        private byte[] _buffer;

        public TrackedBufferOwner(TrackedBufferAllocator allocator, int size)
        {
            _allocator = allocator;
            _buffer = new byte[size];
        }

        public Memory<byte> Memory
        {
            get
            {
                if (_isFreed)
                {
                    throw new InvalidOperationException("Trying to access freed memory.");
                }
                return _buffer;
            }
        }

        public void Dispose()
        {
            if (_isFreed)
            {
                throw new InvalidOperationException("Trying to free the same buffer twice.");
            }
            _isFreed = true;
            _allocator.Return();
        }
    }
}
