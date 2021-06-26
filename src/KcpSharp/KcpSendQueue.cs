using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

#if NEED_LINKEDLIST_SHIM
using LinkedListOfQueueItem = KcpSharp.NetstandardShim.LinkedList<(KcpSharp.KcpBuffer Data, byte Fragment)>;
using LinkedListNodeOfQueueItem = KcpSharp.NetstandardShim.LinkedListNode<(KcpSharp.KcpBuffer Data, byte Fragment)>;
#else
using LinkedListOfQueueItem = System.Collections.Generic.LinkedList<(KcpSharp.KcpBuffer Data, byte Fragment)>;
using LinkedListNodeOfQueueItem = System.Collections.Generic.LinkedListNode<(KcpSharp.KcpBuffer Data, byte Fragment)>;
#endif

namespace KcpSharp
{
    internal sealed class KcpSendQueue : IValueTaskSource<bool>, IDisposable
    {
        private readonly IKcpBufferAllocator _allocator;
        private readonly KcpConversationUpdateNotification _updateNotification;
        private readonly bool _stream;
        private readonly int _capacity;
        private readonly int _mss;
        private readonly KcpSendReceiveQueueItemCache _cache;
        private ManualResetValueTaskSourceCore<bool> _mrvtsc;

        private readonly LinkedListOfQueueItem _queue;
        private long _unflushedBytes;

        private bool _transportClosed;
        private bool _disposed;

        private bool _operationOngoing;
        private bool _bufferProvided; // true-send, false-flush
        private ReadOnlyMemory<byte> _buffer;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;

        public KcpSendQueue(IKcpBufferAllocator allocator, KcpConversationUpdateNotification updateNotification, bool stream, int capacity, int mss, KcpSendReceiveQueueItemCache cache)
        {
            _allocator = allocator;
            _updateNotification = updateNotification;
            _stream = stream;
            _capacity = capacity;
            _mss = mss;
            _cache = cache;
            _mrvtsc = new ManualResetValueTaskSourceCore<bool>()
            {
                RunContinuationsAsynchronously = true
            };

            _queue = new LinkedListOfQueueItem();
        }

        bool IValueTaskSource<bool>.GetResult(short token) => _mrvtsc.GetResult(token);
        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _mrvtsc.OnCompleted(continuation, state, token, flags);

        public bool TryGetAvailableSpace(out int byteCount, out int fragmentCount)
        {
            lock (_queue)
            {
                if (_transportClosed || _disposed)
                {
                    byteCount = 0;
                    fragmentCount = 0;
                    return false;
                }
                if (_operationOngoing && _bufferProvided)
                {
                    byteCount = 0;
                    fragmentCount = 0;
                    return true;
                }
                int mss = _mss;
                int availableFragments = _capacity - _queue.Count;
                if (availableFragments < 0)
                {
                    byteCount = 0;
                    fragmentCount = 0;
                    return true;
                }
                int availableBytes = availableFragments * mss;
                if (_stream)
                {
                    LinkedListNodeOfQueueItem? last = _queue.Last;
                    if (last is not null)
                    {
                        availableBytes += _mss - last.ValueRef.Data.Length;
                    }
                }
                byteCount = availableBytes;
                fragmentCount = availableFragments;
                return true;
            }
        }

        public bool TrySend(ReadOnlySpan<byte> buffer, bool allowPartialSend, out int bytesWritten)
        {
            lock (_queue)
            {
                if (allowPartialSend && !_stream)
                {
                    ThrowHelper.ThrowAllowPartialSendArgumentException();
                }
                if (_transportClosed || _disposed)
                {
                    bytesWritten = 0;
                    return false;
                }
                int mss = _mss;
                // Make sure there is enough space.
                if (!allowPartialSend)
                {
                    int spaceAvailable = mss * (_capacity - _queue.Count);
                    if (spaceAvailable < 0)
                    {
                        bytesWritten = 0;
                        return false;
                    }
                    if (_stream)
                    {
                        LinkedListNodeOfQueueItem? last = _queue.Last;
                        if (last is not null)
                        {
                            spaceAvailable += mss - last.ValueRef.Data.Length;
                        }
                    }

                    if (buffer.Length > spaceAvailable)
                    {
                        bytesWritten = 0;
                        return false;
                    }
                }
                // Copy buffer content.
                bytesWritten = 0;
                if (_stream)
                {
                    LinkedListNodeOfQueueItem? node = _queue.Last;
                    if (node is not null)
                    {
                        ref KcpBuffer data = ref node.ValueRef.Data;
                        int expand = mss - data.Length;
                        expand = Math.Min(expand, buffer.Length);
                        if (expand > 0)
                        {
                            data = data.AppendData(buffer.Slice(0, expand));
                            buffer = buffer.Slice(expand);
                            Interlocked.Add(ref _unflushedBytes, expand);
                            bytesWritten = expand;
                        }
                    }

                    if (buffer.IsEmpty)
                    {
                        return true;
                    }
                }

                bool anyFragmentAdded = false;
                int count = (buffer.Length <= mss) ? 1 : (buffer.Length + mss - 1) / mss;
                Debug.Assert(count >= 1);
                while (count > 0 && _queue.Count < _capacity)
                {
                    int fragment = --count;

                    int size = buffer.Length > mss ? mss : buffer.Length;
                    var kcpBuffer = KcpBuffer.CreateFromSpan(_allocator.Allocate(mss), buffer.Slice(0, size));
                    buffer = buffer.Slice(size);

                    _queue.AddLast(_cache.Rent(kcpBuffer, _stream ? (byte)0 : (byte)fragment));
                    Interlocked.Add(ref _unflushedBytes, size);
                    bytesWritten += size;
                    anyFragmentAdded = true;
                }

                if (anyFragmentAdded)
                {
                    _updateNotification.TrySet(false);
                }
                return anyFragmentAdded;
            }
        }

        public ValueTask<bool> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            short token;
            lock (_queue)
            {
                if (_transportClosed || _disposed)
                {
                    return new ValueTask<bool>(false);
                }
                if (_operationOngoing)
                {
                    return new ValueTask<bool>(Task.FromException<bool>(ThrowHelper.NewConcurrentSendException()));
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken));
                }

                int mss = _mss;
                if (_stream)
                {
                    LinkedListNodeOfQueueItem? node = _queue.Last;
                    if (node is not null)
                    {
                        ref KcpBuffer data = ref node.ValueRef.Data;
                        int expand = mss - data.Length;
                        expand = Math.Min(expand, buffer.Length);
                        if (expand > 0)
                        {
                            data = data.AppendData(buffer.Span.Slice(0, expand));
                            buffer = buffer.Slice(expand);
                            Interlocked.Add(ref _unflushedBytes, expand);
                        }
                    }

                    if (buffer.IsEmpty)
                    {
                        return new ValueTask<bool>(true);
                    }
                }

                int count = (buffer.Length <= mss) ? 1 : (buffer.Length + mss - 1) / mss;
                Debug.Assert(count >= 1);

                if (!_stream && count > 256)
                {
                    return new ValueTask<bool>(Task.FromException<bool>(ThrowHelper.NewMessageTooLargeForBufferArgument()));
                }

                // synchronously put fragments into queue.
                while (count > 0 && _queue.Count < _capacity)
                {
                    int fragment = --count;

                    int size = buffer.Length > mss ? mss : buffer.Length;
                    var kcpBuffer = KcpBuffer.CreateFromSpan(_allocator.Allocate(mss), buffer.Span.Slice(0, size));
                    buffer = buffer.Slice(size);

                    _queue.AddLast(_cache.Rent(kcpBuffer, _stream ? (byte)0 : (byte)fragment));
                    Interlocked.Add(ref _unflushedBytes, size);
                }

                _updateNotification.TrySet(false);

                if (count == 0)
                {
                    return new ValueTask<bool>(true);
                }

                _mrvtsc.Reset();
                _operationOngoing = true;
                _bufferProvided = true;
                _buffer = buffer;
                _cancellationToken = cancellationToken;
                token = _mrvtsc.Version;
            }

            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((KcpSendQueue?)state)!.SetCanceled(), this);

            return new ValueTask<bool>(this, token);
        }

        public ValueTask<bool> FlushAsync(CancellationToken cancellationToken)
        {
            short token;
            lock (_queue)
            {
                if (_transportClosed || _disposed)
                {
                    return new ValueTask<bool>(false);
                }
                if (_operationOngoing)
                {
                    return new ValueTask<bool>(Task.FromException<bool>(ThrowHelper.NewConcurrentSendException()));
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken));
                }

                _mrvtsc.Reset();
                _operationOngoing = true;
                _bufferProvided = false;
                _buffer = default;
                _cancellationToken = cancellationToken;
                token = _mrvtsc.Version;
            }

            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((KcpSendQueue?)state)!.SetCanceled(), this);

            return new ValueTask<bool>(this, token);
        }

        private void SetCanceled()
        {
            lock (_queue)
            {
                if (_operationOngoing)
                {
                    CancellationToken cancellationToken = _cancellationToken;
                    ClearPreviousOperation();
                    _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                }
            }
        }

        private void ClearPreviousOperation()
        {
            _operationOngoing = false;
            _bufferProvided = false;
            _buffer = default;
            _cancellationToken = default;
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
        }

        public bool TryDequeue(int itemsInSendWindow, out KcpBuffer data, out byte fragment)
        {
            lock (_queue)
            {
                LinkedListNodeOfQueueItem? node = _queue.First;
                if (node is null)
                {
                    if (itemsInSendWindow == 0)
                    {
                        CompleteFlush();
                    }
                    data = default;
                    fragment = default;
                    return false;
                }
                else
                {
                    (data, fragment) = node.ValueRef;
                    _queue.RemoveFirst();
                    node.ValueRef = default;
                    _cache.Return(node);

                    MoveOneFragmentIn();
                    return true;
                }
            }
        }

        private void MoveOneFragmentIn()
        {
            if (_operationOngoing && _bufferProvided)
            {
                ReadOnlyMemory<byte> buffer = _buffer;
                int mss = _mss;
                int count = (buffer.Length <= mss) ? 1 : (buffer.Length + mss - 1) / mss;

                int size = buffer.Length > mss ? mss : buffer.Length;
                var kcpBuffer = KcpBuffer.CreateFromSpan(_allocator.Allocate(mss), buffer.Span.Slice(0, size));
                _buffer = buffer.Slice(size);

                _queue.AddLast(_cache.Rent(kcpBuffer, _stream ? (byte)0 : (byte)(count - 1)));
                Interlocked.Add(ref _unflushedBytes, size);

                if (count == 1)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetResult(true);
                }
            }
        }

        private void CompleteFlush()
        {
            if (_operationOngoing && !_bufferProvided)
            {
                ClearPreviousOperation();
                _mrvtsc.SetResult(true);
            }
        }

        public void SubtractUnflushedBytes(int size)
            => Interlocked.Add(ref _unflushedBytes, -size);

        public long GetUnflushedBytes()
            => Interlocked.Read(ref _unflushedBytes);

        public void SetTransportClosed()
        {
            lock (_queue)
            {
                if (_transportClosed || _disposed)
                {
                    return;
                }
                if (_operationOngoing)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetResult(false);
                }
                _transportClosed = true;
            }
        }

        public void Dispose()
        {
            lock (_queue)
            {
                if (_disposed)
                {
                    return;
                }
                if (_operationOngoing)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetResult(false);
                }
                LinkedListNodeOfQueueItem? node = _queue.First;
                while (node is not null)
                {
                    node.ValueRef.Data.Release();
                    node = node.Next;
                }
                _queue.Clear();
                _disposed = true;
                _transportClosed = true;
            }
        }
    }
}
