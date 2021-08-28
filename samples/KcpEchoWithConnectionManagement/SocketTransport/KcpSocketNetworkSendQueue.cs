using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks.Sources;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.SocketTransport
{
    internal sealed class KcpSocketNetworkSendQueue : IDisposable
    {
        private readonly IKcpBufferPool _bufferPool;
        private readonly Socket _socket;
        private readonly int _capacity;

        private int _operationCount;
        private int _loopActive;
        private bool _disposed;
        private readonly LinkedList<SendOperation> _queue;
        private readonly LinkedList<SendOperation> _cache;

        private SpinLock _cacheLock;
        private int _nextId;

        public KcpSocketNetworkSendQueue(IKcpBufferPool bufferPool, Socket socket, int capacity)
        {
            _bufferPool = bufferPool;
            _socket = socket;
            _capacity = capacity;

            _queue = new LinkedList<SendOperation>();
            _cache = new LinkedList<SendOperation>();
        }

        private int GetNextId()
        {
            while (true)
            {
                int id = Interlocked.Increment(ref _nextId);
                if (id != 0)
                {
                    return id;
                }
            }
        }

        private SendOperation? AcquireFreeOperation()
        {
            if (Interlocked.Increment(ref _operationCount) > _capacity)
            {
                Interlocked.Decrement(ref _operationCount);
                return null;
            }

            LinkedListNode<SendOperation>? node;
            SendOperation operation;

            bool lockTaken = false;
            try
            {
                _cacheLock.Enter(ref lockTaken);

                node = _cache.First;
                if (node is not null)
                {
                    _cache.Remove(node);
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _cacheLock.Exit();
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

        public bool Queue(ReadOnlySpan<byte> packet, EndPoint endPoint)
        {
            SendOperation? operation = AcquireFreeOperation();
            if (operation is null)
            {
                return false;
            }

            operation.SetPacket(_bufferPool, packet, endPoint);

            // create loop thread or add to the queue
            lock (_queue)
            {
                _queue.AddLast(operation.Node);

                if (_loopActive == 0)
                {
                    _loopActive = GetNextId();

                    _ = Task.Run(() => SendLoopAsync(_loopActive), CancellationToken.None);
                }
            }

            return true;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, EndPoint endPoint, CancellationToken cancellationToken)
        {
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

            // create loop thread or add to the queue
            lock (_queue)
            {
                _queue.AddLast(operation.Node);

                if (_loopActive == 0)
                {
                    _loopActive = GetNextId();

                    _ = Task.Run(() => SendLoopAsync(_loopActive), CancellationToken.None);
                }
            }

            return task;
        }

        private void ReturnOperation(SendOperation operation)
        {
            if (_disposed)
            {
                return;
            }

            bool lockTaken = false;
            try
            {
                _cacheLock.Enter(ref lockTaken);

                _cache.AddLast(operation.Node);
            }
            finally
            {
                if (lockTaken)
                {
                    _cacheLock.Exit();
                }
            }
        }

        private async Task SendLoopAsync(int id)
        {
            Debug.Assert(_loopActive == id);
            try
            {
                while (!_disposed)
                {
                    LinkedListNode<SendOperation>? node;

                    lock (_queue)
                    {
                        node = _queue.First;

                        if (node is null)
                        {
                            _loopActive = 0;
                            return;
                        }
                        else
                        {
                            _queue.Remove(node);
                        }
                    }

                    try
                    {
                        await node.Value.SendAsync(_socket).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _operationCount);
                        node.Value.NotifySendComplete();
                    }
                }
            }
            finally
            {
                lock (_queue)
                {
                    if (_loopActive == id)
                    {
                        _loopActive = 0;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            lock (_queue)
            {
                foreach (SendOperation operation in _queue)
                {
                    operation.SetDisposed();
                }

                _queue.Clear();
            }

            bool lockTaken = false;
            try
            {
                _cacheLock.Enter(ref lockTaken);

                _cache.Clear();
            }
            finally
            {
                if (lockTaken)
                {
                    _cacheLock.Exit();
                }
            }
        }

        class SendOperation : IValueTaskSource
        {
            private readonly KcpSocketNetworkSendQueue _queue;
            private LinkedListNode<SendOperation> _node;
            private ManualResetValueTaskSourceCore<bool> _mrvtsc;
            private SpinLock _lock;

            private bool _isAsyncMode;
            private bool _isDetached;
            private EndPoint? _endPoint;
            private KcpRentedBuffer _rentedBuffer;
            private ReadOnlyMemory<byte> _buffer;
            private CancellationToken _cancellationToken;
            private CancellationTokenRegistration _cancellationRegistration;

            public LinkedListNode<SendOperation> Node => _node;

            ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _mrvtsc.GetStatus(token);
            void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
                => _mrvtsc.OnCompleted(continuation, state, token, flags);
            void IValueTaskSource.GetResult(short token)
            {
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    _mrvtsc.GetStatus(token);
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }

                    _mrvtsc.Reset();

                    if (_isDetached)
                    {
                        _isDetached = false;
                        _queue.ReturnOperation(this);
                    }
                }
            }

            public SendOperation(KcpSocketNetworkSendQueue queue)
            {
                _queue = queue;
                _node = new LinkedListNode<SendOperation>(this);
                _mrvtsc = new ManualResetValueTaskSourceCore<bool>
                {
                    RunContinuationsAsynchronously = true
                };
            }

            public void SetPacket(IKcpBufferPool bufferPool, ReadOnlySpan<byte> packet, EndPoint remoteEndPoint)
            {
                if (packet.IsEmpty)
                {
                    return;
                }

                KcpRentedBuffer rentedBuffer = bufferPool.Rent(new KcpBufferPoolRentOptions(packet.Length, true));

                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    if (_endPoint is not null)
                    {
                        throw new InvalidOperationException();
                    }

                    Memory<byte> memory = rentedBuffer.Memory.Slice(0, packet.Length);
                    packet.CopyTo(memory.Span);

                    Debug.Assert(_endPoint is null);
                    _isAsyncMode = false;
                    _isDetached = true;
                    _endPoint = remoteEndPoint;
                    _rentedBuffer = rentedBuffer;
                    _buffer = memory;
                    _cancellationToken = default;

                    rentedBuffer = default;
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
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

                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    if (_endPoint is not null)
                    {
                        return ValueTask.FromException(new InvalidOperationException());
                    }
                    Debug.Assert(_endPoint is null);
                    _isAsyncMode = true;
                    _isDetached = true;
                    _endPoint = remoteEndPoint;
                    _rentedBuffer = default;
                    _buffer = packet;
                    _cancellationToken = cancellationToken;

                    return new ValueTask(this, _mrvtsc.Version);
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();

                        _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((SendOperation?)state)!.SetCanceled(), this);
                    }
                }
            }

            private void ClearParameters()
            {
                Debug.Assert(_endPoint is not null);
                _isAsyncMode = false;
                _endPoint = null;
                _rentedBuffer.Dispose();
                _rentedBuffer = default;
                _buffer = default;
                _cancellationToken = default;
                _cancellationRegistration.Dispose();
                _cancellationRegistration = default;
            }

            private void SetCanceled()
            {
                bool lockTaken = false;
                bool shouldReturn = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    if (_endPoint is not null)
                    {
                        if (_isAsyncMode)
                        {
                            CancellationToken cancellationToken = _cancellationToken;
                            ClearParameters();
                            _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                        }
                        else if (_isDetached)
                        {
                            _isDetached = false;
                            shouldReturn = true;
                        }
                    }
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
                }

                if (shouldReturn)
                {
                    _queue.ReturnOperation(this);
                }
            }

            public void SetDisposed()
            {
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    if (_endPoint is not null)
                    {
                        bool isAsyncMode = _isAsyncMode;
                        ClearParameters();
                        if (isAsyncMode)
                        {
                            _mrvtsc.SetResult(false);
                        }
                    }
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
                }
            }

            public ValueTask<int> SendAsync(Socket socket)
            {
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    if (_endPoint is null)
                    {
                        return default;
                    }

                    return socket.SendToAsync(_buffer, SocketFlags.None, _endPoint, _cancellationToken);
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
                }
            }

            public void NotifySendComplete()
            {
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    if (_endPoint is null)
                    {
                        return;
                    }

                    if (_isAsyncMode)
                    {
                        ClearParameters();
                        _mrvtsc.SetResult(true);
                        return;
                    }
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
                }

                if (_isDetached)
                {
                    _isDetached = false;
                    _queue.ReturnOperation(this);
                }
            }
        }

    }
}
