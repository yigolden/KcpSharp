using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace KcpSharp
{
    internal class AsyncAutoResetEvent<T> : IValueTaskSource<T>
    {
        private ManualResetValueTaskSourceCore<T> _rvtsc;
        private SpinLock _spin;
        private bool _isSet;
        private bool _isWaiting;
        private T? _value;

        public AsyncAutoResetEvent()
        {
            _rvtsc = new ManualResetValueTaskSourceCore<T>()
            {
                RunContinuationsAsynchronously = true
            };
            _spin = new SpinLock();
        }

        T IValueTaskSource<T>.GetResult(short token) => _rvtsc.GetResult(token);
        ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token) => _rvtsc.GetStatus(token);
        void IValueTaskSource<T>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _rvtsc.OnCompleted(continuation, state, token, flags);

        public ValueTask<T> WaitAsync()
        {
            bool lockTaken = false;
            try
            {
                _spin.Enter(ref lockTaken);

                if (_isWaiting)
                {
                    return new ValueTask<T>(Task.FromException<T>(new InvalidOperationException("Another thread is already waiting.")));
                }
                if (_isSet)
                {
                    _isSet = false;
                    T value = _value!;
                    _value = default;
                    return new ValueTask<T>(value);
                }

                _rvtsc.Reset();
                _isWaiting = true;

                return new ValueTask<T>(this, _rvtsc.Version);
            }
            finally
            {
                if (lockTaken)
                {
                    _spin.Exit();
                }
            }
        }

        public void Set(T value)
        {
            bool lockTaken = false;
            try
            {
                _spin.Enter(ref lockTaken);

                if (_isWaiting)
                {
                    _isWaiting = false;
                    _rvtsc.SetResult(value);
                    return;
                }

                _isSet = true;
                _value = value;
            }
            finally
            {
                if (lockTaken)
                {
                    _spin.Exit();
                }
            }
        }
    }

}
