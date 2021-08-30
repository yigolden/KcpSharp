using System.Threading.Tasks.Sources;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal sealed class KcpNetworkConnectionAcceptQueue : IValueTaskSource<KcpNetworkConnection>
    {
        private ManualResetValueTaskSourceCore<KcpNetworkConnectionListenerConnectionState> _mrvtsc;
        private readonly int _capacity;

        private readonly LinkedList<KcpNetworkConnectionListenerConnectionState> _queue = new();
        private readonly LinkedList<KcpNetworkConnectionListenerConnectionState> _cache = new();
        private bool _disposed;

        private bool _isActive;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;


        ValueTaskSourceStatus IValueTaskSource<KcpNetworkConnection>.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource<KcpNetworkConnection>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _mrvtsc.OnCompleted(continuation, state, token, flags);
        KcpNetworkConnection IValueTaskSource<KcpNetworkConnection>.GetResult(short token)
        {
            try
            {
                return _mrvtsc.GetResult(token).CreateNetworkConnection();
            }
            finally
            {
                _mrvtsc.Reset();
            }
        }

        public KcpNetworkConnectionAcceptQueue(int capacity)
        {
            _mrvtsc = new ManualResetValueTaskSourceCore<KcpNetworkConnectionListenerConnectionState>
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
                LinkedListNode<KcpNetworkConnectionListenerConnectionState>? node = _queue.First;
                if (node is not null)
                {
                    KcpNetworkConnectionListenerConnectionState connectionState = node.Value;
                    node.Value = null!;
                    _queue.Remove(node);
                    _cache.AddLast(node);
                    return new ValueTask<KcpNetworkConnection>(connectionState.CreateNetworkConnection());
                }

                _isActive = true;
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
                if (_isActive)
                {
                    CancellationToken cancellationToken = _cancellationToken;
                    ClearPreviousOperations();
                    _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                }
            }
        }

        public void SetDisposed()
        {
            lock (this)
            {
                if (_disposed)
                {
                    return;
                }
                if (_isActive)
                {
                    ClearPreviousOperations();
                    _mrvtsc.SetException(new ObjectDisposedException(nameof(KcpNetworkConnectionAcceptQueue)));
                }
            }
            foreach (KcpNetworkConnectionListenerConnectionState item in _queue)
            {
                item.SetDisposed();
            }
            _queue.Clear();
        }

        private void ClearPreviousOperations()
        {
            _isActive = false;
            _cancellationToken = default;
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
        }

        public bool IsQueueAvailable()
        {
            lock (_queue)
            {
                if (_disposed)
                {
                    return false;
                }
                return _queue.Count < _capacity;
            }
        }

        public bool TryQueue(KcpNetworkConnectionListenerConnectionState state)
        {
            lock (_queue)
            {
                if (_disposed)
                {
                    return false;
                }
                if (_isActive)
                {
                    ClearPreviousOperations();
                    _mrvtsc.SetResult(state);
                    return true;
                }
                LinkedListNode<KcpNetworkConnectionListenerConnectionState>? node = _cache.First;
                if (node is null)
                {
                    node = new LinkedListNode<KcpNetworkConnectionListenerConnectionState>(state);
                }
                else
                {
                    node.Value = state;
                    _cache.Remove(node);
                }
                _queue.AddLast(node);
                return true;
            }
        }

    }
}
