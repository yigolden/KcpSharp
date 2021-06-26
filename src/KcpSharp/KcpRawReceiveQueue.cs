using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

#if NEED_LINKEDLIST_SHIM
using LinkedListOfQueueItem = KcpSharp.NetstandardShim.LinkedList<KcpSharp.KcpBuffer>;
using LinkedListNodeOfQueueItem = KcpSharp.NetstandardShim.LinkedListNode<KcpSharp.KcpBuffer>;
#else
using LinkedListOfQueueItem = System.Collections.Generic.LinkedList<KcpSharp.KcpBuffer>;
using LinkedListNodeOfQueueItem = System.Collections.Generic.LinkedListNode<KcpSharp.KcpBuffer>;
#endif

namespace KcpSharp
{
    internal sealed class KcpRawReceiveQueue : IValueTaskSource<KcpConversationReceiveResult>, IDisposable
    {
        private ManualResetValueTaskSourceCore<KcpConversationReceiveResult> _mrvtsc;

        private readonly IKcpBufferAllocator _allocator;
        private readonly int _capacity;
        private readonly LinkedListOfQueueItem _queue;
        private readonly LinkedListOfQueueItem _recycled;

        private bool _transportClosed;
        private bool _disposed;

        private bool _operationOngoing;
        private bool _bufferProvided;
        private Memory<byte> _buffer;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;

        public KcpRawReceiveQueue(IKcpBufferAllocator allocator, int capacity)
        {
            _allocator = allocator;
            _capacity = capacity;
            _queue = new LinkedListOfQueueItem();
            _recycled = new LinkedListOfQueueItem();
        }

        KcpConversationReceiveResult IValueTaskSource<KcpConversationReceiveResult>.GetResult(short token) => _mrvtsc.GetResult(token);
        ValueTaskSourceStatus IValueTaskSource<KcpConversationReceiveResult>.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource<KcpConversationReceiveResult>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _mrvtsc.OnCompleted(continuation, state, token, flags);

        public bool TryPeek(out KcpConversationReceiveResult result)
        {
            lock (_queue)
            {
                if (_disposed || _transportClosed)
                {
                    result = default;
                    return false;
                }
                if (_operationOngoing)
                {
                    ThrowHelper.ThrowConcurrentReceiveException();
                }
                LinkedListNodeOfQueueItem? first = _queue.First;
                if (first is null)
                {
                    result = new KcpConversationReceiveResult(0);
                    return false;
                }

                result = new KcpConversationReceiveResult(first.ValueRef.Length);
                return true;
            }
        }

        public ValueTask<KcpConversationReceiveResult> WaitToReceiveAsync(CancellationToken cancellationToken)
        {
            short token;
            lock (_queue)
            {
                if (_transportClosed || _disposed)
                {
                    return default;
                }
                if (_operationOngoing)
                {
                    return new ValueTask<KcpConversationReceiveResult>(Task.FromException<KcpConversationReceiveResult>(ThrowHelper.NewConcurrentReceiveException()));
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<KcpConversationReceiveResult>(Task.FromCanceled<KcpConversationReceiveResult>(cancellationToken));
                }

                LinkedListNodeOfQueueItem? first = _queue.First;
                if (first is not null)
                {
                    return new ValueTask<KcpConversationReceiveResult>(new KcpConversationReceiveResult(first.ValueRef.Length));
                }

                _mrvtsc.Reset();
                _operationOngoing = true;
                _bufferProvided = false;
                _buffer = default;
                _cancellationToken = cancellationToken;

                token = _mrvtsc.Version;
            }
            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((KcpRawReceiveQueue?)state)!.SetCanceled(), this);

            return new ValueTask<KcpConversationReceiveResult>(this, token);
        }

        public bool TryReceive(Span<byte> buffer, out KcpConversationReceiveResult result)
        {
            lock (_queue)
            {
                if (_disposed || _transportClosed)
                {
                    result = default;
                    return false;
                }
                if (_operationOngoing)
                {
                    ThrowHelper.ThrowConcurrentReceiveException();
                }
                LinkedListNodeOfQueueItem? first = _queue.First;
                if (first is null)
                {
                    result = new KcpConversationReceiveResult(0);
                    return false;
                }

                KcpBuffer source = first.ValueRef;
                if (buffer.Length < source.Length)
                {
                    ThrowHelper.ThrowBufferTooSmall();
                }

                source.DataRegion.Span.CopyTo(buffer);
                result = new KcpConversationReceiveResult(first.ValueRef.Length);

                _queue.RemoveFirst();
                first.ValueRef.Release();
                first.ValueRef = default;
                _recycled.AddLast(first);

                return true;
            }
        }

        public ValueTask<KcpConversationReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            short token;
            lock (_queue)
            {
                if (_transportClosed || _disposed)
                {
                    return default;
                }
                if (_operationOngoing)
                {
                    return new ValueTask<KcpConversationReceiveResult>(Task.FromException<KcpConversationReceiveResult>(ThrowHelper.NewConcurrentReceiveException()));
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<KcpConversationReceiveResult>(Task.FromCanceled<KcpConversationReceiveResult>(cancellationToken));
                }

                LinkedListNodeOfQueueItem? first = _queue.First;
                if (first is not null)
                {
                    KcpBuffer source = first.ValueRef;
                    int length = source.Length;
                    if (buffer.Length < source.Length)
                    {
                        return new ValueTask<KcpConversationReceiveResult>(Task.FromException<KcpConversationReceiveResult>(ThrowHelper.NewBufferTooSmallForBufferArgument()));
                    }
                    _queue.Remove(first);

                    source.DataRegion.CopyTo(buffer);
                    source.Release();
                    first.ValueRef = default;
                    _recycled.AddLast(first);

                    return new ValueTask<KcpConversationReceiveResult>(new KcpConversationReceiveResult(length));
                }

                _mrvtsc.Reset();
                _operationOngoing = true;
                _bufferProvided = true;
                _buffer = buffer;
                _cancellationToken = cancellationToken;

                token = _mrvtsc.Version;
            }
            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((KcpRawReceiveQueue?)state)!.SetCanceled(), this);

            return new ValueTask<KcpConversationReceiveResult>(this, token);
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

        public void Enqueue(ReadOnlySpan<byte> buffer)
        {
            lock (_queue)
            {
                if (_transportClosed || _disposed)
                {
                    return;
                }

                int queueSize = _queue.Count;
                if (queueSize > 0 || !_operationOngoing)
                {
                    if (queueSize >= _capacity)
                    {
                        return;
                    }

                    IMemoryOwner<byte> owner = _allocator.Allocate(buffer.Length);
                    _queue.AddLast(AllocateNode(KcpBuffer.CreateFromSpan(owner, buffer)));
                    return;
                }

                if (!_bufferProvided)
                {
                    IMemoryOwner<byte> owner = _allocator.Allocate(buffer.Length);
                    _queue.AddLast(AllocateNode(KcpBuffer.CreateFromSpan(owner, buffer)));

                    ClearPreviousOperation();
                    _mrvtsc.SetResult(new KcpConversationReceiveResult(buffer.Length));
                    return;
                }

                if (buffer.Length > _buffer.Length)
                {
                    IMemoryOwner<byte> owner = _allocator.Allocate(buffer.Length);
                    _queue.AddLast(AllocateNode(KcpBuffer.CreateFromSpan(owner, buffer)));

                    ClearPreviousOperation();
                    _mrvtsc.SetException(ThrowHelper.NewBufferTooSmallForBufferArgument());
                    return;
                }

                buffer.CopyTo(_buffer.Span);
                ClearPreviousOperation();
                _mrvtsc.SetResult(new KcpConversationReceiveResult(buffer.Length));
            }
        }

        private LinkedListNodeOfQueueItem AllocateNode(KcpBuffer buffer)
        {
            LinkedListNodeOfQueueItem? node = _recycled.First;
            if (node is null)
            {
                node = new LinkedListNodeOfQueueItem(buffer);
            }
            else
            {
                node.ValueRef = buffer;
                _recycled.Remove(node);
            }
            return node;
        }

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
                    _mrvtsc.SetResult(default);
                }
                _recycled.Clear();
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
                    _mrvtsc.SetResult(default);
                }
                LinkedListNodeOfQueueItem? node = _queue.First;
                while (node is not null)
                {
                    node.ValueRef.Release();
                    node = node.Next;
                }
                _queue.Clear();
                _recycled.Clear();
                _disposed = true;
                _transportClosed = true;
            }
        }
    }
}
