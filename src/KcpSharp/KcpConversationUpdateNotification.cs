using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace KcpSharp
{
    internal sealed class KcpConversationUpdateNotification : IValueTaskSource<bool>, IDisposable
    {
        private ManualResetValueTaskSourceCore<bool> _mrvtsc;

        private bool _activeWait;
        private bool _signaled;
        private bool _isSet;

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
        }

        bool IValueTaskSource<bool>.GetResult(short token)
        {
            _cancellationRegistration.Dispose();
            try
            {
                return _mrvtsc.GetResult(token);
            }
            finally
            {
                _mrvtsc.Reset();
                lock (this)
                {
                    _activeWait = false;
                    _signaled = false;
                    _cancellationRegistration = default;
                }

            }

        }

        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _mrvtsc.OnCompleted(continuation, state, token, flags);

        public ValueTask<bool> WaitAsync(CancellationToken cancellationToken)
        {
            short token;
            lock (this)
            {
                if (_isDisposed)
                {
                    return new ValueTask<bool>(Task.FromException<bool>(new ObjectDisposedException(nameof(KcpConversationUpdateNotification))));
                }
                if (_activeWait)
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

                _activeWait = true;
                Debug.Assert(!_signaled);
                _cancellationToken = cancellationToken;
                token = _mrvtsc.Version;
            }

            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((KcpConversationUpdateNotification?)state)!.SetCanceled(), this);

            return new ValueTask<bool>(this, token);
        }

        private void SetCanceled()
        {
            lock (this)
            {
                if (_activeWait && !_signaled)
                {
                    CancellationToken cancellationToken = _cancellationToken;
                    ClearPreviousOperation();
                    _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                }
            }
        }

        private void ClearPreviousOperation()
        {
            _signaled = true;
            _cancellationToken = default;
        }

        public bool TrySet(bool isPeriodic) => TrySet(isPeriodic, out _);

        public bool TrySet(bool isPeriodic, out bool previouslySet)
        {
            lock (this)
            {
                if (_isDisposed)
                {
                    previouslySet = false;
                    return false;
                }

                if (_activeWait && !_signaled)
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
        }


        public void Dispose()
        {
            lock (this)
            {
                _isDisposed = true;

                if (_activeWait && !_signaled)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetException(new ObjectDisposedException(nameof(KcpConversationUpdateNotification)));
                    return;
                }
            }
        }
    }
}
