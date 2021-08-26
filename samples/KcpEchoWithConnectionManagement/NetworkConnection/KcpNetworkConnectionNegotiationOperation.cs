using System.Diagnostics;
using System.Threading.Tasks.Sources;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal sealed class KcpNetworkConnectionNegotiationOperation : IValueTaskSource
    {
        private readonly KcpNetworkConnection _networkConnection;
        private readonly IKcpConnectionAuthenticationContext? _authenticationContext;

        private ManualResetValueTaskSourceCore<bool> _mrvtsc;
        private bool _isActive;
        private bool _isCanceled;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;

        private CancellationTokenSource? _cts;
        private int _state; // 0-Send CONNECT 1-Wait for CONNECT 2-Send ACCEPT 3-Send Auth 
        private uint _nextSendTicks;
        private uint _authSerial; // TODO should we split?
        private KcpRentedBuffer? _unprocessedPacket;
        private KcpRentedBuffer? _authDataPacket;

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _mrvtsc.OnCompleted(continuation, state, token, flags);
        void IValueTaskSource.GetResult(short token)
        {
            try
            {
                _mrvtsc.GetResult(token);
            }
            finally
            {
                _mrvtsc.Reset();
            }
        }

        public KcpNetworkConnectionNegotiationOperation(KcpNetworkConnection networkConnection, IKcpConnectionAuthenticationContext? authenticationContext)
        {
            _networkConnection = networkConnection;
            _authenticationContext = authenticationContext;

            _mrvtsc = new ManualResetValueTaskSourceCore<bool>
            {
                RunContinuationsAsynchronously = true
            };
        }

        public ValueTask NegotiateAsync(CancellationToken cancellationToken)
        {
            short token;
            lock (this)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return ValueTask.FromCanceled(cancellationToken);
                }
                if (_isActive)
                {
                    return ValueTask.FromException(new InvalidOperationException());
                }

                _isActive = true;
                _isCanceled = false;
                _cancellationToken = cancellationToken;
                token = _mrvtsc.Version;
            }

            _cancellationRegistration = cancellationToken.UnsafeRegister(s => ((KcpNetworkConnectionNegotiationOperation?)s!).SetCanceled(), this);
            return new ValueTask(this, token);
        }

        private void SetCanceled()
        {
            CancellationTokenSource? cts = null;
            lock (this)
            {
                if (_isActive)
                {
                    _isCanceled = true;
                    cts = _cts;
                    _cts = null;
                }
            }
            cts?.Cancel();
        }

        private void ClearPreviousOperations()
        {
            _isActive = false;
            _isCanceled = false;
            _cancellationToken = default;
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
        }

        private void Run()
        {
            // set up cancellation callback
            CancellationTokenSource? cts;
            CancellationToken cancellationToken;
            lock (this)
            {
                if (!_isActive)
                {
                    return;
                }
                if (_isCanceled)
                {
                    cancellationToken = _cancellationToken;
                    ClearPreviousOperations();
                    _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                    return;
                }

                _cts = cts = new CancellationTokenSource();
                cancellationToken = cts.Token;

                _state = 0;
                Debug.Assert(!_unprocessedPacket.HasValue);
            }

            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));

                timer.WaitForNextTickAsync();
            }
            catch (OperationCanceledException)
            {
                lock (this)
                {
                    cancellationToken = _cancellationToken;
                    ClearPreviousOperations();
                    _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                }
            }
            finally
            {
                cts.Dispose();
            }
        }

        public void InputPacket(ReadOnlyMemory<byte> packet)
        {
            // validate packet
            if (packet.Length < 4)
            {
                return;
            }
        }
    }
}
