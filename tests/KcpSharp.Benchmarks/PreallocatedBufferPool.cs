using System;
using System.Buffers;
using System.Collections.Concurrent;

namespace KcpSharp.Benchmarks
{
    internal class PreallocatedBufferPool : IKcpBufferPool
    {
        private readonly ConcurrentQueue<ArrayBlock> _queue = new();
        private readonly int _mtu;

        public PreallocatedBufferPool(int mtu)
        {
            _mtu = mtu;
        }

        public void Fill(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _queue.Enqueue(new ArrayBlock(_queue, GC.AllocateUninitializedArray<byte>(_mtu)));
            }
        }

        public KcpRentedBuffer Rent(KcpBufferPoolRentOptions options)
        {
            if (options.Size > _mtu)
            {
                throw new InvalidOperationException();
            }
            if (_queue.TryDequeue(out ArrayBlock? block))
            {
                return KcpRentedBuffer.FromMemoryOwner(block);
            }

            return KcpRentedBuffer.FromMemoryOwner(new ArrayBlock(_queue, GC.AllocateUninitializedArray<byte>(_mtu)));
        }

        sealed class ArrayBlock : IMemoryOwner<byte>
        {
            private readonly ConcurrentQueue<ArrayBlock> _queue;
            private readonly byte[] _buffer;

            public ArrayBlock(ConcurrentQueue<ArrayBlock> queue, byte[] buffer)
            {
                _queue = queue;
                _buffer = buffer;
            }

            public Memory<byte> Memory => _buffer;

            public void Dispose() => _queue.Enqueue(this);
        }
    }
}
