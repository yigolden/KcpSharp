using System;
using System.Buffers;
using System.Collections.Concurrent;

namespace KcpSharp.ThroughputBanchmarks
{
    /// <summary>
    /// Used to allocate and distribute re-usable blocks of memory.
    /// </summary>
    internal sealed class ArrayBlockMemoryPool : MemoryPool<byte>, IKcpBufferAllocator
    {
        /// <summary>
        /// The size of a block.
        /// </summary>
        private readonly int _blockSize;

        /// <summary>
        /// Max allocation block size for pooled blocks,
        /// larger values can be leased but they will be disposed after use rather than returned to the pool.
        /// </summary>
        public override int MaxBufferSize => _blockSize;

        /// <summary>
        /// Thread-safe collection of blocks which are currently in the pool. A slab will pre-allocate all of the block tracking objects
        /// and add them to this collection. When memory is requested it is taken from here first, and when it is returned it is re-added.
        /// </summary>
        private readonly ConcurrentQueue<MemoryPoolBlock> _blocks = new ConcurrentQueue<MemoryPoolBlock>();

        /// <summary>
        /// This is part of implementing the IDisposable pattern.
        /// </summary>
        private bool _isDisposed; // To detect redundant calls

        private readonly object _disposeSync = new object();

        /// <summary>
        /// This default value passed in to Rent to use the default value for the pool.
        /// </summary>
        private const int AnySize = -1;

        public ArrayBlockMemoryPool(int blockSize)
        {
            if (blockSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockSize));
            }
            _blockSize = blockSize;
        }

        public override IMemoryOwner<byte> Rent(int size = AnySize)
        {
            if (size > _blockSize)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ArrayBlockMemoryPool));
            }

            if (_blocks.TryDequeue(out MemoryPoolBlock? block))
            {
                // block successfully taken from the stack - return it
                return block;
            }
            return new MemoryPoolBlock(this, _blockSize);
        }

        /// <summary>
        /// Called to return a block to the pool. Once Return has been called the memory no longer belongs to the caller, and
        /// Very Bad Things will happen if the memory is read of modified subsequently. If a caller fails to call Return and the
        /// block tracking object is garbage collected, the block tracking object's finalizer will automatically re-create and return
        /// a new tracking object into the pool. This will only happen if there is a bug in the server, however it is necessary to avoid
        /// leaving "dead zones" in the slab due to lost block tracking objects.
        /// </summary>
        /// <param name="block">The block to return. It must have been acquired by calling Lease on the same memory pool instance.</param>
        internal void Return(MemoryPoolBlock block)
        {
            if (!_isDisposed)
            {
                _blocks.Enqueue(block);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            lock (_disposeSync)
            {
                _isDisposed = true;

                if (disposing)
                {
                    // Discard blocks in pool
                    while (_blocks.TryDequeue(out _))
                    {

                    }
                }
            }
        }

        public IMemoryOwner<byte> Allocate(int size) => Rent(size);
    }
}
