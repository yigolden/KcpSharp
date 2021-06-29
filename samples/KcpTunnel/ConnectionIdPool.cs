using System;
using System.Collections.Concurrent;
using System.Threading;

namespace KcpTunnel
{
    internal class ConnectionIdPool
    {
        private const ushort _maxValue = ushort.MaxValue - 1;

        private ConcurrentQueue<ushort> _queue = new();
        private ushort _nextValue = 0;
        private int _activeIds;

        public ushort Rent()
        {
            if (Interlocked.Increment(ref _activeIds) > 64 && _queue.TryDequeue(out ushort id))
            {
                return id;
            }
            lock (_queue)
            {
                id = (ushort)(_nextValue + 1);
                if (id > _maxValue)
                {
                    ThrowInvalidOperationException();
                }
                _nextValue = id;
                return id;
            }
        }

        public void Return(ushort id)
        {
            Interlocked.Decrement(ref _activeIds);
            _queue.Enqueue(id);
        }

        private static void ThrowInvalidOperationException()
        {
            throw new InvalidOperationException();
        }

    }
}
