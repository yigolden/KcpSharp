using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace KcpSharp
{
    internal sealed class KcpRawSendOperation : IValueTaskSource, IDisposable
    {
        private readonly AsyncAutoResetEvent<int> _notification;
        private ManualResetValueTaskSourceCore<bool> _mrvtsc;

        private bool _transportClosed;
        private bool _disposed;

        private bool _operationOngoing;
        private ReadOnlyMemory<byte> _buffer;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;

        public KcpRawSendOperation(AsyncAutoResetEvent<int> notification)
        {
            _notification = notification;

            _mrvtsc = new ManualResetValueTaskSourceCore<bool>()
            {
                RunContinuationsAsynchronously = true
            };
        }

        void IValueTaskSource.GetResult(short token) => _mrvtsc.GetResult(token);
        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _mrvtsc.OnCompleted(continuation, state, token, flags);

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            short token;
            lock (this)
            {
                if (_disposed)
                {
                    return new ValueTask(Task.FromException(ThrowHelper.NewObjectDisposedExceptionForKcpConversation()));
                }
                if (_transportClosed)
                {
                    return new ValueTask(Task.FromException(ThrowHelper.NewTransportClosedException()));
                }
                if (_operationOngoing)
                {
                    return new ValueTask(Task.FromException(ThrowHelper.NewConcurrentSendException()));
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask(Task.FromCanceled(cancellationToken));
                }

                _mrvtsc.Reset();
                _operationOngoing = true;
                _buffer = buffer;
                _cancellationToken = cancellationToken;
                token = _mrvtsc.Version;
            }

            _cancellationRegistration = cancellationToken.UnsafeRegister(state => ((KcpRawSendOperation?)state)!.SetCanceled(), this);

            _notification.Set(buffer.Length);
            return new ValueTask(this, token);

        }

        private void SetCanceled()
        {
            lock (this)
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
            _buffer = default;
            _cancellationToken = default;
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
        }

        public bool TryConsume(Memory<byte> buffer, out int bytesWritten)
        {
            lock (this)
            {
                if (_transportClosed || _disposed)
                {
                    bytesWritten = 0;
                    return false;
                }
                if (!_operationOngoing)
                {
                    bytesWritten = 0;
                    return false;
                }
                ReadOnlyMemory<byte> source = _buffer;
                if (source.Length > buffer.Length)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetException(ThrowHelper.NewMessageTooLarge());
                    bytesWritten = 0;
                    return false;
                }
                source.CopyTo(buffer);
                bytesWritten = source.Length;
                ClearPreviousOperation();
                _mrvtsc.SetResult(true);
                return true;
            }
        }

        public void SetTransportClosed()
        {
            lock (this)
            {
                if (_transportClosed || _disposed)
                {
                    return;
                }
                if (_operationOngoing)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetException(ThrowHelper.NewTransportClosedException());
                }
                _transportClosed = true;
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
                if (_operationOngoing)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetException(ThrowHelper.NewTransportClosedException());
                }
                _disposed = true;
                _transportClosed = true;
            }
        }
    }
}
