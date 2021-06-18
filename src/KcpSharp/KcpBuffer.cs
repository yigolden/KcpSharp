using System;
using System.Buffers;

namespace KcpSharp
{
    internal readonly struct KcpBuffer
    {
        private readonly IMemoryOwner<byte> _owner;
        private readonly int _offset;
        private readonly int _length;

        public Memory<byte> Buffer => _owner.Memory;
        public ReadOnlyMemory<byte> DataRegion => _owner.Memory.Slice(_offset, _length);
        public int Length => _length;

        private KcpBuffer(IMemoryOwner<byte> owner, int offset, int length)
        {
            _owner = owner;
            _offset = offset;
            _length = length;
        }

        public static KcpBuffer CreateFromSpan(IMemoryOwner<byte> owner, ReadOnlySpan<byte> dataSource)
        {
            Memory<byte> memory = owner.Memory;
            if (dataSource.Length > memory.Length)
            {
                ThrowInvalidOperationException();
            }
            dataSource.CopyTo(memory.Span);
            return new KcpBuffer(owner, 0, dataSource.Length);
        }

        public KcpBuffer AppendData(ReadOnlySpan<byte> data)
        {
            Memory<byte> buffer = _owner.Memory.Slice(_offset);
            int expandedLength = _length + data.Length;
            if (expandedLength > buffer.Length)
            {
                ThrowInvalidOperationException();
            }
            data.CopyTo(buffer.Span.Slice(_length));
            return new KcpBuffer(_owner, _offset, expandedLength);
        }

        public KcpBuffer Advance(int length)
        {
            if ((uint)length > (uint)_length)
            {
                ThrowInvalidOperationException();
            }
            return new KcpBuffer(_owner, _offset + length, _length - length);
        }

        public void Release()
        {
            _owner.Dispose();
        }

        private static void ThrowInvalidOperationException()
        {
            throw new InvalidOperationException();
        }
    }
}
