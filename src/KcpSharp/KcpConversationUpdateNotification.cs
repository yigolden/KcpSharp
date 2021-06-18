using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace KcpSharp
{
    internal sealed class KcpConversationUpdateNotification : IValueTaskSource<bool>, IDisposable
    {
        private ManualResetValueTaskSourceCore<bool> _mrvtsc;
        private SpinLock _lock;
        private bool _isSet;

        private bool _isWaiting;
        private bool _isWaitingInitialized;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;

        private bool _isPeriodic;
        private bool _isDisposed;

        public KcpConversationUpdateNotification()
        {
            _mrvtsc = new ManualResetValueTaskSourceCore<bool>()
            {
                RunContinuationsAsynchronously = true
            };
            _lock = new SpinLock();
        }

        bool IValueTaskSource<bool>.GetResult(short token) => _mrvtsc.GetResult(token);
        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _mrvtsc.OnCompleted(continuation, state, token, flags);

        public ValueTask<bool> WaitAsync(CancellationToken cancellationToken)
        {
            short token;
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                if (_isDisposed)
                {
                    return new ValueTask<bool>(Task.FromException<bool>(new ObjectDisposedException(nameof(KcpConversationUpdateNotification))));
                }

                if (_isWaiting)
                {
                    return new ValueTask<bool>(Task.FromException<bool>(new InvalidOperationException("Another thread is already waiting.")));
                }

                if (_isSet)
                {
                    _isSet = false;
                    return new ValueTask<bool>(_isPeriodic);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken));
                }

                _mrvtsc.Reset();
                _isWaiting = true;
                _isWaitingInitialized = false;
                _cancellationToken = cancellationToken;
                _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((KcpConversationUpdateNotification?)state)!.SetCanceled(), this);
                _isWaitingInitialized = true;
                token = _mrvtsc.Version;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }

            return new ValueTask<bool>(this, token);
        }

        private void SetCanceled()
        {
            bool lockTaken = false;
            try
            {
                if (_isWaitingInitialized)
                {
                    _lock.Enter(ref lockTaken);
                }

                CancellationToken cancellationToken = _cancellationToken;
                ClearPreviousOperation();
                _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        private void ClearPreviousOperation()
        {
            _isWaiting = false;
            _isWaitingInitialized = false;
            _cancellationToken = default;
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
        }

        public bool TrySet(bool isPeriodic) => TrySet(isPeriodic, out _);

        public bool TrySet(bool isPeriodic, out bool previouslySet)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                if (_isDisposed)
                {
                    previouslySet = false;
                    return false;
                }

                if (_isWaiting)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetResult(isPeriodic);
                    previouslySet = false;
                    return true;
                }

                previouslySet = _isSet;
                _isSet = true;
                _isPeriodic = _isPeriodic || isPeriodic;
                return true;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }


        public void Dispose()
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                if (_isWaiting)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetException(new ObjectDisposedException(nameof(KcpConversationUpdateNotification)));
                    return;
                }

                _isDisposed = true;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }
    }
}
