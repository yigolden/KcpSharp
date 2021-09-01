using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal sealed class KcpNetworkConnectionAcceptQueue : IValueTaskSource<KcpNetworkConnection>, IDisposable
    {
        private ManualResetValueTaskSourceCore<KcpNetworkConnection> _mrvtsc;
        private readonly int _capacity;

        private readonly LinkedList<KcpNetworkConnection> _queue = new();
        private readonly LinkedList<KcpNetworkConnection> _cache = new();
        private bool _disposed;

        private bool _isActive;
        private bool _isExpectingResult;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;

        ValueTaskSourceStatus IValueTaskSource<KcpNetworkConnection>.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource<KcpNetworkConnection>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _mrvtsc.OnCompleted(continuation, state, token, flags);
        KcpNetworkConnection IValueTaskSource<KcpNetworkConnection>.GetResult(short token)
        {
            try
            {
                return _mrvtsc.GetResult(token);
            }
            finally
            {
                _mrvtsc.Reset();

                lock (_queue)
                {
                    if (_isActive)
                    {
                        _isActive = false;
                        _isExpectingResult = false;
                    }
                }
            }
        }

        public KcpNetworkConnectionAcceptQueue(int capacity)
        {
            _mrvtsc = new ManualResetValueTaskSourceCore<KcpNetworkConnection>
            {
                RunContinuationsAsynchronously = true
            };
            _capacity = capacity;
        }

        public ValueTask<KcpNetworkConnection> AcceptAsync(CancellationToken cancellationToken)
        {
            short token;
            lock (_queue)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return ValueTask.FromCanceled<KcpNetworkConnection>(cancellationToken);
                }
                if (_disposed)
                {
                    return ValueTask.FromException<KcpNetworkConnection>(new ObjectDisposedException(nameof(KcpNetworkConnectionAcceptQueue)));
                }
                LinkedListNode<KcpNetworkConnection>? node = _queue.First;
                if (node is not null)
                {
                    KcpNetworkConnection connection = node.Value;
                    node.Value = null!;
                    _queue.Remove(node);
                    _cache.AddLast(node);
                    return new ValueTask<KcpNetworkConnection>(connection);
                }

                _isActive = true;
                _isExpectingResult = true;
                _cancellationToken = cancellationToken;
                token = _mrvtsc.Version;
            }

            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((KcpNetworkConnectionAcceptQueue?)state)!.SetCanceled(), this);
            return new ValueTask<KcpNetworkConnection>(this, token);
        }

        private void SetCanceled()
        {
            lock (this)
            {
                if (_isExpectingResult)
                {
                    Debug.Assert(_isActive);
                    CancellationToken cancellationToken = _cancellationToken;
                    ClearPreviousOperations();
                    _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                }
            }
        }

        public void Dispose()
        {
            lock (this)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                if (_isExpectingResult)
                {
                    Debug.Assert(_isActive);
                    ClearPreviousOperations();
                    _mrvtsc.SetException(new ObjectDisposedException(nameof(KcpNetworkConnectionAcceptQueue)));
                }
            }
            foreach (KcpNetworkConnection item in _queue)
            {
                item.Dispose();
            }
            _queue.Clear();
        }

        private void ClearPreviousOperations()
        {
            _isExpectingResult = false;
            _cancellationToken = default;
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
        }

        public bool IsQueueAvailable()
        {
            if (_disposed)
            {
                return false;
            }
            return _queue.Count < _capacity;
        }

        public bool TryQueue(KcpNetworkConnection connection)
        {
            lock (_queue)
            {
                if (_disposed)
                {
                    return false;
                }
                if (_isExpectingResult)
                {
                    Debug.Assert(_isActive);
                    ClearPreviousOperations();
                    _mrvtsc.SetResult(connection);
                    return true;
                }
                LinkedListNode<KcpNetworkConnection>? node = _cache.First;
                if (node is null)
                {
                    node = new LinkedListNode<KcpNetworkConnection>(connection);
                }
                else
                {
                    node.Value = connection;
                    _cache.Remove(node);
                }
                _queue.AddLast(node);
                return true;
            }
        }

    }
}
