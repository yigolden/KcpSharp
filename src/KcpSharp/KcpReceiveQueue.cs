using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using System.Diagnostics;

#if NEED_LINKEDLIST_SHIM
using LinkedListOfQueueItem = KcpSharp.NetstandardShim.LinkedList<(KcpSharp.KcpBuffer Data, byte Fragment)>;
using LinkedListNodeOfQueueItem = KcpSharp.NetstandardShim.LinkedListNode<(KcpSharp.KcpBuffer Data, byte Fragment)>;
#else
using LinkedListOfQueueItem = System.Collections.Generic.LinkedList<(KcpSharp.KcpBuffer Data, byte Fragment)>;
using LinkedListNodeOfQueueItem = System.Collections.Generic.LinkedListNode<(KcpSharp.KcpBuffer Data, byte Fragment)>;
#endif

namespace KcpSharp
{
    internal sealed class KcpReceiveQueue : IValueTaskSource<KcpConversationReceiveResult>, IDisposable
    {
        private ManualResetValueTaskSourceCore<KcpConversationReceiveResult> _mrvtsc;

        private readonly LinkedListOfQueueItem _queue;
        private readonly bool _stream;
        private readonly KcpSendReceiveQueueItemCache _cache;
        private int _completedPacketsCount;

        private bool _transportClosed;
        private bool _disposed;

        private bool _operationOngoing;
        private bool _bufferProvided;
        private Memory<byte> _buffer;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;

        public KcpReceiveQueue(bool stream, KcpSendReceiveQueueItemCache cache)
        {
            _mrvtsc = new ManualResetValueTaskSourceCore<KcpConversationReceiveResult>()
            {
                RunContinuationsAsynchronously = true
            };
            _queue = new LinkedListOfQueueItem();
            _stream = stream;
            _cache = cache;
        }

        KcpConversationReceiveResult IValueTaskSource<KcpConversationReceiveResult>.GetResult(short token) => _mrvtsc.GetResult(token);
        ValueTaskSourceStatus IValueTaskSource<KcpConversationReceiveResult>.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource<KcpConversationReceiveResult>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _mrvtsc.OnCompleted(continuation, state, token, flags);

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

                if (_completedPacketsCount == 0)
                {
                    result = new KcpConversationReceiveResult(0);
                    return false;
                }

                LinkedListNodeOfQueueItem? node = _queue.First;
                if (node is null)
                {
                    result = new KcpConversationReceiveResult(0);
                    return false;
                }

                if (CalculatePacketSize(node, out int packetSize))
                {
                    result = new KcpConversationReceiveResult(packetSize);
                    return true;
                }

                result = default;
                return false;
            }
        }

        public ValueTask<KcpConversationReceiveResult> WaitToReceiveAsync(CancellationToken cancellationToken)
        {
            short token;
            lock (_queue)
            {
                if (_disposed)
                {
                    return new ValueTask<KcpConversationReceiveResult>(Task.FromException<KcpConversationReceiveResult>(ThrowHelper.NewObjectDisposedExceptionForKcpConversation()));
                }
                if (_transportClosed)
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

                _mrvtsc.Reset();
                _operationOngoing = true;
                _bufferProvided = false;
                _buffer = default;
                _cancellationToken = cancellationToken;

                token = _mrvtsc.Version;
                if (_completedPacketsCount > 0)
                {
                    ConsumePacket(out KcpConversationReceiveResult result, out bool bufferTooSmall);
                    ClearPreviousOperation();
                    if (bufferTooSmall)
                    {
                        Debug.Assert(false, "This should never be reached.");
                        return new ValueTask<KcpConversationReceiveResult>(Task.FromException<KcpConversationReceiveResult>(ThrowHelper.NewBufferTooSmallForBufferArgument()));
                    }
                    else
                    {
                        return new ValueTask<KcpConversationReceiveResult>(result);
                    }
                }
            }
            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((KcpReceiveQueue?)state)!.SetCanceled(), this);

            return new ValueTask<KcpConversationReceiveResult>(this, token);
        }

        public bool TryReceive(Memory<byte> buffer, out KcpConversationReceiveResult result)
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

                if (_completedPacketsCount == 0)
                {
                    result = new KcpConversationReceiveResult(0);
                    return false;
                }

                _operationOngoing = true;
                _bufferProvided = true;
                _buffer = buffer;

                ConsumePacket(out result, out bool bufferTooSmall);
                ClearPreviousOperation();
                if (bufferTooSmall)
                {
                    ThrowHelper.ThrowBufferTooSmall();
                }
                return true;
            }
        }

        public ValueTask<KcpConversationReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            short token;
            lock (_queue)
            {
                if (_disposed)
                {
                    return new ValueTask<KcpConversationReceiveResult>(Task.FromException<KcpConversationReceiveResult>(ThrowHelper.NewObjectDisposedExceptionForKcpConversation()));
                }
                if (_transportClosed)
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

                _mrvtsc.Reset();
                _operationOngoing = true;
                _bufferProvided = true;
                _buffer = buffer;
                _cancellationToken = cancellationToken;

                token = _mrvtsc.Version;
                if (_completedPacketsCount > 0)
                {
                    ConsumePacket(out KcpConversationReceiveResult result, out bool bufferTooSmall);
                    ClearPreviousOperation();
                    if (bufferTooSmall)
                    {
                        return new ValueTask<KcpConversationReceiveResult>(Task.FromException<KcpConversationReceiveResult>(ThrowHelper.NewBufferTooSmallForBufferArgument()));
                    }
                    else
                    {
                        return new ValueTask<KcpConversationReceiveResult>(result);
                    }
                }
            }
            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((KcpReceiveQueue?)state)!.SetCanceled(), this);

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

        public void Enqueue(KcpBuffer buffer, byte fragment)
        {
            lock (_queue)
            {
                if (_transportClosed || _disposed)
                {
                    return;
                }

                if (_stream)
                {
                    fragment = 0;
                    _queue.AddLast(_cache.Rent(buffer, 0));
                }
                else
                {
                    LinkedListNodeOfQueueItem? lastNode = _queue.Last;
                    if (lastNode is null || lastNode.ValueRef.Fragment == 0 || (lastNode.ValueRef.Fragment - 1) == fragment)
                    {
                        _queue.AddLast(_cache.Rent(buffer, fragment));
                    }
                    else
                    {
                        fragment = 0;
                        _queue.AddLast(_cache.Rent(buffer, 0));
                    }
                }

                if (fragment == 0)
                {
                    _completedPacketsCount++;
                    if (_operationOngoing)
                    {
                        ConsumePacket(out KcpConversationReceiveResult result, out bool bufferTooSmall);
                        ClearPreviousOperation();
                        if (bufferTooSmall)
                        {
                            _mrvtsc.SetException(ThrowHelper.NewBufferTooSmallForBufferArgument());
                        }
                        else
                        {
                            _mrvtsc.SetResult(result);
                        }
                    }
                }
            }
        }

        private void ConsumePacket(out KcpConversationReceiveResult result, out bool bufferTooSmall)
        {
            Debug.Assert(_operationOngoing);

            LinkedListNodeOfQueueItem? node = _queue.First;
            if (node is null)
            {
                result = default;
                bufferTooSmall = false;
                return;
            }

            // peek
            if (!_bufferProvided)
            {
                if (CalculatePacketSize(node, out int bytesRecevied))
                {
                    result = new KcpConversationReceiveResult(bytesRecevied);
                }
                else
                {
                    result = default;
                }
                bufferTooSmall = false;
                return;
            }

            // ensure buffer is big enough
            int bytesInPacket = 0;
            if (!_stream)
            {
                while (node is not null)
                {
                    bytesInPacket += node.ValueRef.Data.Length;
                    if (node.ValueRef.Fragment == 0)
                    {
                        break;
                    }
                    node = node.Next;
                }

                if (node is null)
                {
                    // incomplete packet
                    result = default;
                    bufferTooSmall = false;
                    return;
                }

                if (bytesInPacket > _buffer.Length)
                {
                    result = default;
                    bufferTooSmall = true;
                    return;
                }
            }

            bool anyDataReceived = false;
            bytesInPacket = 0;
            node = _queue.First;
            Memory<byte> buffer = _buffer;
            LinkedListNodeOfQueueItem? next;
            while (node is not null)
            {
                next = node.Next;

                byte fragment = node.ValueRef.Fragment;
                KcpBuffer data = node.ValueRef.Data;

                int sizeToCopy = Math.Min(data.Length, buffer.Length);
                data.DataRegion.Slice(0, sizeToCopy).CopyTo(buffer);
                buffer = buffer.Slice(sizeToCopy);
                bytesInPacket += sizeToCopy;
                anyDataReceived = true;

                if (sizeToCopy != data.Length)
                {
                    // partial data is received.
                    node.ValueRef = (data.Advance(sizeToCopy), node.ValueRef.Fragment);
                }
                else
                {
                    // full fragment is consumed
                    data.Release();
                    _queue.Remove(node);
                    _cache.Return(node);
                    if (fragment == 0)
                    {
                        _completedPacketsCount--;
                    }
                }

                if (!_stream && fragment == 0)
                {
                    break;
                }

                if (sizeToCopy == 0)
                {
                    break;
                }

                node = next;
            }

            if (!anyDataReceived)
            {
                result = default;
                bufferTooSmall = false;
            }
            else
            {
                result = new KcpConversationReceiveResult(bytesInPacket);
                bufferTooSmall = false;
            }
        }

        private static bool CalculatePacketSize(LinkedListNodeOfQueueItem first, out int packetSize)
        {
            int bytesRecevied = first.ValueRef.Data.Length;
            if (first.ValueRef.Fragment == 0)
            {
                packetSize = bytesRecevied;
                return true;
            }

            LinkedListNodeOfQueueItem? node = first.Next;
            while (node is not null)
            {
                bytesRecevied += node.ValueRef.Data.Length;
                if (node.ValueRef.Fragment == 0)
                {
                    packetSize = bytesRecevied;
                    return true;
                }
                node = node.Next;
            }

            // deadlink
            packetSize = 0;
            return false;
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
                _transportClosed = true;
            }
        }

        public int GetQueueSize()
        {
            lock (_queue)
            {
                return _queue.Count;
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
