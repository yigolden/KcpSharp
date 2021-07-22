using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace KcpSharp.Benchmarks
{
    internal sealed class PreallocatedQueue<T>
    {
        private readonly LinkedList<T> _queue;
        private readonly LinkedList<T> _cache;
        private readonly DequeueOperation _dequeueOperation;

        public PreallocatedQueue(int size)
        {
            _queue = new LinkedList<T>();
            _cache = new LinkedList<T>();
            _dequeueOperation = new DequeueOperation(_queue, _cache);

            for (int i = 0; i < size; i++)
            {
                _cache.AddLast(default(T)!);
            }
        }

        public bool TryWrite(T value)
        {
            lock (_queue)
            {
                if (_dequeueOperation.TryComplete(value))
                {
                    return true;
                }
                LinkedListNode<T>? node = _cache.First;
                if (node is null)
                {
                    return false;
                }
                _cache.Remove(node);
                node.ValueRef = value;
                _queue.AddLast(node);
            }
            return true;
        }

        public ValueTask<T> ReadAsync(CancellationToken cancellationToken)
            => _dequeueOperation.DequeueAsync(cancellationToken);

        class DequeueOperation : IValueTaskSource<T>
        {
            private readonly LinkedList<T> _queue;
            private readonly LinkedList<T> _cache;
            private ManualResetValueTaskSourceCore<T> _mrvtsc;

            private bool _isActive;
            private CancellationToken _cancellationToken;
            private CancellationTokenRegistration _cancellationRegistration;

            public DequeueOperation(LinkedList<T> queue, LinkedList<T> cache)
            {
                _queue = queue;
                _cache = cache;
                _mrvtsc = new ManualResetValueTaskSourceCore<T>
                {
                    RunContinuationsAsynchronously = true
                };
            }

            T IValueTaskSource<T>.GetResult(short token) => _mrvtsc.GetResult(token);
            ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token) => _mrvtsc.GetStatus(token);
            void IValueTaskSource<T>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
                => _mrvtsc.OnCompleted(continuation, state, token, flags);

            public ValueTask<T> DequeueAsync(CancellationToken cancellationToken)
            {
                short token;
                lock (_queue)
                {
                    if (_isActive)
                    {
                        return ValueTask.FromException<T>(new InvalidOperationException());
                    }
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return ValueTask.FromCanceled<T>(cancellationToken);
                    }
                    LinkedListNode<T>? node = _queue.First;
                    if (node is not null)
                    {
                        T value = node.ValueRef;
                        node.ValueRef = default!;
                        _queue.Remove(node);
                        _cache.AddLast(node);
                        return new ValueTask<T>(value);
                    }

                    _mrvtsc.Reset();
                    _isActive = true;
                    _cancellationToken = cancellationToken;
                    token = _mrvtsc.Version;
                }

                _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((DequeueOperation?)state)!.SetCanceled(), this);

                return new ValueTask<T>(this, token);
            }

            private void SetCanceled()
            {
                lock (_queue)
                {
                    if (_isActive)
                    {
                        CancellationToken cancellationToken = _cancellationToken;
                        ClearPreviousOperation();
                        _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                    }
                }
            }

            private void ClearPreviousOperation()
            {
                _isActive = false;
                _cancellationToken = default;
                _cancellationRegistration.Dispose();
                _cancellationRegistration = default;
            }

            internal bool TryComplete(T value)
            {
                if (_isActive)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetResult(value);
                    return true;
                }
                return false;
            }
        }
    }
}
