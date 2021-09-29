using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.SocketTransport
{
    internal sealed class KcpSocketNetworkSendQueue : IDisposable
    {
        private readonly IKcpBufferPool _bufferPool;
        private readonly Socket _socket;
        private readonly int _capacity;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        private readonly AsyncAutoResetEvent<SendOperation?> _sendEvent = new();
        private readonly object _queueLock = new object();
        private SimpleLinkedList<SendOperation> _queue = new();

        private readonly SimpleLinkedList<SendOperation> _cache = new();
        private int _operationCount;

        public KcpSocketNetworkSendQueue(IKcpBufferPool bufferPool, Socket socket, int capacity)
        {
            _bufferPool = bufferPool;
            _socket = socket;
            _capacity = capacity;

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => SendLoop(_cts.Token));
        }

        private async Task SendLoop(CancellationToken cancellationToken)
        {
            SendOperation? sendOperation;
            while (!cancellationToken.IsCancellationRequested)
            {
                // wait
                try
                {
                    sendOperation = await _sendEvent.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (sendOperation is null)
                {
                    break;
                }

                // send
                bool succeed = false;
                try
                {
                    await sendOperation.SendAsync(_socket, cancellationToken).ConfigureAwait(false);
                    succeed = true;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    // TODO log and ignore
                }
                finally
                {
                    sendOperation.NotifySendComplete(succeed);
                }

                // consumed queue and send
                while (true)
                {
                    SimpleLinkedListNode<SendOperation>? node;
                    lock (_queueLock)
                    {
                        node = _queue.First;
                        if (node is null)
                        {
                            break;
                        }

                        _queue.Remove(node);
                    }

                    succeed = false;
                    try
                    {
                        await node.Value.SendAsync(_socket, cancellationToken).ConfigureAwait(false);
                        succeed = true;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        // TODO log and ignore
                    }
                    finally
                    {
                        node.Value.NotifySendComplete(succeed);
                    }
                }
            }

            if (_sendEvent.TryGet(out sendOperation))
            {
                sendOperation?.Dispose();
            }

            // consume queue and dispose
            while (true)
            {
                SimpleLinkedListNode<SendOperation>? node;
                lock (_queueLock)
                {
                    node = _queue.First;
                    if (node is null)
                    {
                        break;
                    }
                    _queue.Remove(node);
                }

                node.Value.Dispose();
            }
        }

        public bool Queue(ReadOnlySpan<byte> packet, EndPoint endPoint)
        {
            if (packet.IsEmpty)
            {
                return false;
            }

            SendOperation? operation = AcquireFreeOperation();
            if (operation is null)
            {
                return default;
            }

            operation.SetPacket(_bufferPool, packet, endPoint);

            if (_sendEvent.TrySet(operation))
            {
                if (_disposed)
                {
                    if (_sendEvent.TryGet(out operation))
                    {
                        operation?.Dispose();
                    }
                    return false;
                }
            }
            else
            {
                lock (_queueLock)
                {
                    if (_disposed)
                    {
                        operation.Dispose();
                        return false;
                    }

                    _queue.AddLast(operation.Node);
                }
            }

            return true;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, EndPoint endPoint, CancellationToken cancellationToken)
        {
            if (packet.IsEmpty)
            {
                return default;
            }

            SendOperation? operation = AcquireFreeOperation();
            if (operation is null)
            {
                return default;
            }

            ValueTask task = operation.SetPacketAndWaitAsync(packet, endPoint, cancellationToken);
            if (task.IsCompleted)
            {
                return task;
            }

            if (_sendEvent.TrySet(operation))
            {
                if (_disposed)
                {
                    if (_sendEvent.TryGet(out operation))
                    {
                        operation?.Dispose();
                    }
                }
            }
            else
            {
                lock (_queueLock)
                {
                    if (_disposed)
                    {
                        operation.Dispose();
                    }
                    else
                    {
                        _queue.AddLast(operation.Node);
                    }
                }
            }

            return task;
        }

        private SendOperation? AcquireFreeOperation()
        {
            if (Interlocked.Increment(ref _operationCount) > _capacity)
            {
                Interlocked.Decrement(ref _operationCount);
                return null;
            }

            SimpleLinkedListNode<SendOperation>? node;
            SendOperation operation;

            lock (_cache)
            {
                if (_disposed)
                {
                    Interlocked.Decrement(ref _operationCount);
                    return null;
                }
                node = _cache.First;
                if (node is not null)
                {
                    _cache.Remove(node);
                }
            }

            if (node is null)
            {
                operation = new SendOperation(this);
            }
            else
            {
                operation = node.Value;
            }

            return operation;
        }

        private void ReturnOperationNode(SendOperation operation, bool isAborted)
        {
            if (_disposed)
            {
                return;
            }

            Interlocked.Decrement(ref _operationCount);

            bool shouldCache = true;

            if (isAborted)
            {
                lock (_queueLock)
                {
                    if (!_queue.TryRemove(operation.Node))
                    {
                        shouldCache = false;
                    }
                }
            }

            if (shouldCache)
            {
                lock (_cache)
                {
                    _cache.AddLast(operation.Node);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            Volatile.Write(ref _disposed, true);

            _sendEvent.TrySet(null);

            CancellationTokenSource? cts = Interlocked.Exchange(ref _cts, null);
            if (cts is not null)
            {
                cts.Cancel();
                cts.Dispose();
            }
        }


        class SendOperation : IValueTaskSource, IDisposable
        {
            private readonly KcpSocketNetworkSendQueue _queue;
            private readonly SimpleLinkedListNode<SendOperation> _node;
            private ManualResetValueTaskSourceCore<bool> _mrvtsc;
            private object _lock = new object();

            private bool _isAsyncActive;
            private bool _isAborted;
            private EndPoint? _endPoint;
            private KcpRentedBuffer _buffer;
            private CancellationToken _cancellationToken;
            private CancellationTokenRegistration _cancellationRegistration;

            public SimpleLinkedListNode<SendOperation> Node => _node;

            public SendOperation(KcpSocketNetworkSendQueue queue)
            {
                _queue = queue;
                _node = new SimpleLinkedListNode<SendOperation>(this);
                _mrvtsc = new ManualResetValueTaskSourceCore<bool>
                {
                    RunContinuationsAsynchronously = true
                };
            }

            public ValueTaskSourceStatus GetStatus(short token) => _mrvtsc.GetStatus(token);
            public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _mrvtsc.OnCompleted(continuation, state, token, flags);
            public void GetResult(short token)
            {
                try
                {
                    _mrvtsc.GetResult(token);
                }
                finally
                {
                    _mrvtsc.Reset();

                    bool isAsyncActive;
                    bool isAborted;
                    lock (_node)
                    {
                        isAsyncActive = _isAsyncActive;
                        isAborted = _isAborted;
                        if (isAsyncActive)
                        {
                            _isAsyncActive = false;
                            _isAborted = false;
                        }
                    }

                    if (isAsyncActive)
                    {
                        _queue.ReturnOperationNode(this, isAborted);
                    }
                }
            }

            public void SetPacket(IKcpBufferPool bufferPool, ReadOnlySpan<byte> packet, EndPoint _remoteEndPoint)
            {
                if (packet.IsEmpty)
                {
                    return;
                }

                KcpRentedBuffer rentedBuffer = bufferPool.Rent(new KcpBufferPoolRentOptions(packet.Length, true));
                try
                {
                    lock (_node)
                    {
                        Debug.Assert(!_isAsyncActive && _endPoint is null);

                        packet.CopyTo(rentedBuffer.Span);

                        _endPoint = _remoteEndPoint;
                        _buffer = rentedBuffer.Slice(0, packet.Length);
                        Debug.Assert(_cancellationToken == default);
                        Debug.Assert(_cancellationRegistration == default);
                        Debug.Assert(!_isAborted);

                        rentedBuffer = default;
                    }
                }
                finally
                {
                    rentedBuffer.Dispose();
                }
            }

            public ValueTask SetPacketAndWaitAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return ValueTask.FromCanceled(cancellationToken);
                }
                if (packet.IsEmpty)
                {
                    return default;
                }

                short token;
                lock (_node)
                {
                    Debug.Assert(!_isAsyncActive && _endPoint is null);

                    _isAsyncActive = true;
                    _endPoint = remoteEndPoint;
                    _buffer = KcpRentedBuffer.FromMemory(MemoryMarshal.AsMemory(packet));
                    _cancellationToken = cancellationToken;
                    Debug.Assert(!_isAborted);

                    token = _mrvtsc.Version;
                }

                _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((SendOperation?)state)!.SetCanceled(), this);

                return new ValueTask(this, token);
            }

            private void ClearPreviousOperation()
            {
                Debug.Assert(_endPoint is not null);
                _endPoint = null;
                _buffer.Dispose();
                _buffer = default;
                _cancellationToken = default;
                _cancellationRegistration.Dispose();
                _cancellationRegistration = default;
            }

            private void SetCanceled()
            {
                lock (_node)
                {
                    if (_endPoint is not null)
                    {
                        Debug.Assert(_isAsyncActive);

                        CancellationToken cancellationToken = _cancellationToken;
                        ClearPreviousOperation();
                        _isAborted = true;
                        _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                    }
                }
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    if (_endPoint is not null)
                    {
                        ClearPreviousOperation();
                        if (_isAsyncActive)
                        {
                            _mrvtsc.SetResult(false);
                        }
                    }
                }
            }

            public ValueTask<int> SendAsync(Socket socket, CancellationToken cancellationToken)
            {
                lock (_node)
                {
                    EndPoint? endPoint = _endPoint;
                    if (endPoint is null)
                    {
                        return default;
                    }

                    return socket.SendToAsync(_buffer.Memory, SocketFlags.None, endPoint, cancellationToken);
                }
            }

            public void NotifySendComplete(bool succeeded)
            {
                bool shouldReturnNode = false;
                lock (_node)
                {
                    if (_endPoint is null)
                    {
                        return;
                    }

                    ClearPreviousOperation();
                    if (_isAsyncActive)
                    {
                        _mrvtsc.SetResult(succeeded);
                    }
                    else
                    {
                        shouldReturnNode = true;
                    }
                }

                if (shouldReturnNode)
                {
                    _queue.ReturnOperationNode(this, false);
                }
            }

        }
    }
}
