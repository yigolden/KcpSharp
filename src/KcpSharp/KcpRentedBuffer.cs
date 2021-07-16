using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KcpSharp
{
    /// <summary>
    /// The buffer rented and owned by KcpSharp.
    /// </summary>
    public struct KcpRentedBuffer : IEquatable<KcpRentedBuffer>, IDisposable
    {
        private readonly object? _owner;
        private readonly Memory<byte> _memory;

        internal object? Owner => _owner;
        internal Memory<byte> Memory => _memory;

        internal KcpRentedBuffer(object? owner, Memory<byte> buffer)
        {
            _owner = owner;
            _memory = buffer;
        }

        /// <summary>
        /// Create the buffer from the specified <see cref="Memory{T}"/>.
        /// </summary>
        /// <param name="memory">The memory region of this buffer.</param>
        /// <returns>The rented buffer.</returns>
        public static KcpRentedBuffer FromMemory(Memory<byte> memory)
        {
            return new KcpRentedBuffer(null, memory);
        }

        /// <summary>
        /// Create the buffer from the shared array pool.
        /// </summary>
        /// <param name="size">The minimum size of the buffer required.</param>
        /// <returns>The rented buffer.</returns>
        public static KcpRentedBuffer FromSharedArrayPool(int size)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }
            byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
            return new KcpRentedBuffer(ArrayPool<byte>.Shared, buffer);
        }

        /// <summary>
        /// Create the buffer from the specified array pool.
        /// </summary>
        /// <param name="pool">The array pool to use.</param>
        /// <param name="buffer">The byte array rented from the specified pool.</param>
        /// <returns>The rented buffer.</returns>
        public static KcpRentedBuffer FromArrayPool(ArrayPool<byte> pool, byte[] buffer)
        {
            if (pool is null)
            {
                throw new ArgumentNullException(nameof(pool));
            }
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            return new KcpRentedBuffer(pool, buffer);
        }

        /// <summary>
        /// Create the buffer from the specified array pool.
        /// </summary>
        /// <param name="pool">The array pool to use.</param>
        /// <param name="size">The minimum size of the buffer required.</param>
        /// <returns>The rented buffer.</returns>
        public static KcpRentedBuffer FromArrayPool(ArrayPool<byte> pool, int size)
        {
            if (pool is null)
            {
                throw new ArgumentNullException(nameof(pool));
            }
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }
            return new KcpRentedBuffer(pool, pool.Rent(size));
        }

        /// <summary>
        /// Create the buffer from the memory owner.
        /// </summary>
        /// <param name="memoryOwner">The owner of this memory region.</param>
        /// <returns>The rented buffer.</returns>
        public static KcpRentedBuffer FromMemoryOwner(IMemoryOwner<byte> memoryOwner)
        {
            if (memoryOwner is null)
            {
                throw new ArgumentNullException(nameof(memoryOwner));
            }
            return new KcpRentedBuffer(memoryOwner, memoryOwner.Memory);
        }


        /// <summary>
        /// Create the buffer from the memory owner.
        /// </summary>
        /// <param name="memoryOwner">The owner of this memory region.</param>
        /// <param name="memory">The memory region of the buffer.</param>
        /// <returns>The rented buffer.</returns>
        public static KcpRentedBuffer FromMemoryOwner(IDisposable memoryOwner, Memory<byte> memory)
        {
            if (memoryOwner is null)
            {
                throw new ArgumentNullException(nameof(memoryOwner));
            }
            return new KcpRentedBuffer(memoryOwner, memory);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Debug.Assert(_owner is null || _owner is ArrayPool<byte> || _owner is IDisposable);

            if (_owner is null)
            {
                return;
            }
            if (_owner is ArrayPool<byte> arrayPool)
            {
                if (MemoryMarshal.TryGetArray(_memory, out ArraySegment<byte> arraySegment))
                {
                    arrayPool.Return(arraySegment.Array!);
                }
            }
            if (_owner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <inheritdoc />
        public bool Equals(KcpRentedBuffer other) => ReferenceEquals(_owner, other._owner) && _memory.Equals(other._memory);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is KcpRentedBuffer other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _owner is null ? _memory.GetHashCode() : HashCode.Combine(RuntimeHelpers.GetHashCode(_owner), _memory);

        /// <inheritdoc />
        public override string ToString() => $"KcpSharp.KcpRentedBuffer[{_memory.Length}]";
    }
}
