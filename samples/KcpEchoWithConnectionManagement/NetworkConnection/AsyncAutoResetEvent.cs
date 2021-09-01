using System.Threading.Tasks.Sources;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal sealed class AsyncAutoResetEvent : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> _mrvts;

        private bool _isWaiting;
        private bool _isAvailable;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _mrvts.GetStatus(token);
        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _mrvts.OnCompleted(continuation, state, token, flags);
        void IValueTaskSource.GetResult(short token)
        {
            try
            {
                _mrvts.GetResult(token);
            }
            finally
            {
                _mrvts.Reset();
                lock (this)
                {
                    _isWaiting = false;
                    _isAvailable = false;
                }
            }
        }

        public AsyncAutoResetEvent()
        {
            _mrvts = new ManualResetValueTaskSourceCore<bool>
            {
                RunContinuationsAsynchronously = true
            };
        }

        public ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            short token;
            lock (this)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return ValueTask.FromCanceled(cancellationToken);
                }
                if (_isWaiting)
                {
                    return ValueTask.FromException(new InvalidOperationException());
                }
                if (_isAvailable)
                {
                    _isAvailable = false;
                    return default;
                }

                _isWaiting = true;
                _cancellationToken = cancellationToken;
                token = _mrvts.Version;
            }

            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((AsyncAutoResetEvent?)state)!.SetCanceled(), this);
            return new ValueTask(this, token);
        }

        private void SetCanceled()
        {
            lock (this)
            {
                if (_isAvailable)
                {
                    return;
                }
                if (_isWaiting)
                {
                    CancellationToken cancellationToken = _cancellationToken;
                    ClearPreviousOperation();
                    _mrvts.SetException(new OperationCanceledException(cancellationToken));
                }
            }
        }

        private void ClearPreviousOperation()
        {
            _cancellationToken = default;
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
        }

        public bool TryWait()
        {
            lock (this)
            {
                if (_isWaiting)
                {
                    throw new InvalidOperationException();
                }
                if (_isAvailable)
                {
                    _isAvailable = false;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool TrySet()
        {
            lock (this)
            {
                if (_isAvailable)
                {
                    return false;
                }

                _isAvailable = true;

                if (_isWaiting)
                {
                    _mrvts.SetResult(true);
                }
                return true;
            }

        }
    }
}
