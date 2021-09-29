using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Sources;

namespace KcpEchoWithConnectionManagement
{
    internal sealed class AsyncAutoResetEvent<T> : IValueTaskSource<T>
    {
        private ManualResetValueTaskSourceCore<T> _mrvts;

        private bool _isWaiting;
        private bool _isAvailable;
        private T _value;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;

        ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token) => _mrvts.GetStatus(token);
        void IValueTaskSource<T>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _mrvts.OnCompleted(continuation, state, token, flags);
        T IValueTaskSource<T>.GetResult(short token)
        {
            try
            {
                return _mrvts.GetResult(token);
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
            _mrvts = new ManualResetValueTaskSourceCore<T>
            {
                RunContinuationsAsynchronously = true
            };
            _value = default!;
        }

        public ValueTask<T> WaitAsync(CancellationToken cancellationToken)
        {
            short token;
            lock (this)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return ValueTask.FromCanceled<T>(cancellationToken);
                }
                if (_isWaiting)
                {
                    return ValueTask.FromException<T>(new InvalidOperationException());
                }
                if (_isAvailable)
                {
                    T value = _value;
                    _isAvailable = false;
                    _value = default!;
                    return new ValueTask<T>(value);
                }

                _isWaiting = true;
                _cancellationToken = cancellationToken;
                token = _mrvts.Version;
            }

            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((AsyncAutoResetEvent<T>?)state)!.SetCanceled(), this);
            return new ValueTask<T>(this, token);
        }

        public bool TryGet([MaybeNullWhen(false)] out T value)
        {
            lock (this)
            {
                if (_isWaiting)
                {
                    throw new InvalidOperationException();
                }
                if (_isAvailable)
                {
                    value = _value;
                    _value = default!;
                    _isAvailable = false;
                    return true;
                }

                value = default;
                return false;
            }

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
            _value = default!;
            _cancellationToken = default;
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
        }

        public bool TrySet(T value)
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
                    ClearPreviousOperation();
                    _mrvts.SetResult(value);
                }
                else
                {
                    _value = value;
                }
                return true;
            }

        }
    }
}
