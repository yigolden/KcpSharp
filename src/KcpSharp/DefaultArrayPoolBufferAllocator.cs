using System;
using System.Buffers;

namespace KcpSharp
{
    internal sealed class DefaultArrayPoolBufferAllocator : IKcpBufferAllocator
    {
        public static DefaultArrayPoolBufferAllocator Default { get; } = new DefaultArrayPoolBufferAllocator();

        public IMemoryOwner<byte> Allocate(int size)
        {
            return new ArrayPoolBufferOwner(size);
        }
    }

    internal sealed class ArrayPoolBufferOwner : IMemoryOwner<byte>
    {
        private byte[] _buffer;
        private int _size;

        public ArrayPoolBufferOwner(int size)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }
            if (size == 0)
            {
                _buffer = Array.Empty<byte>();
                _size = 0;
            }
            else
            {
                _buffer = ArrayPool<byte>.Shared.Rent(size);
                _size = size;
            }
        }

        public Memory<byte> Memory => _buffer.AsMemory(0, _size);

        public void Dispose()
        {
            if (_size != 0)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = Array.Empty<byte>();
                _size = 0;
            }
        }
    }
}
