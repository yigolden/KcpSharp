using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading.Tasks.Sources;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal sealed class KcpNetworkConnectionNegotiationOperation : IValueTaskSource<bool>, IThreadPoolWorkItem
    {
        private readonly KcpNetworkConnectionNegotiationOperationPool? _pool;
        private KcpNetworkConnection? _networkConnection;
        private IKcpConnectionNegotiationContext? _negotiationContext;

        private ManualResetValueTaskSourceCore<bool> _mrvtsc;
        private bool _isActive;
        private bool _isRunning;
        private bool _isCanceled;
        private bool _isDisposed;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;
        private object _lock = new object();

        private object _activityLock = new object();
        private Timer? _timer;
        private uint? _sessionId;

        private byte _sendSerial;
        private uint _sendTicks;
        private KcpRentedBuffer _sendBuffer;
        private byte _sendRetryCount;

        private byte _receiveSerial;
        private KcpRentedBuffer _receiveBuffer;
        private uint _receivedSessionId;

        private int _tickOperationActive; // 0-no 1-yes

        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _mrvtsc.OnCompleted(continuation, state, token, flags);
        bool IValueTaskSource<bool>.GetResult(short token)
        {
            bool shouldReturnToPool = false;
            try
            {
                return _mrvtsc.GetResult(token);
            }
            finally
            {
                _mrvtsc.Reset();

                lock (_lock)
                {
                    _networkConnection = null;
                    _negotiationContext = null;

                    if (_isActive)
                    {
                        Debug.Assert(!_isRunning);
                        _isActive = false;
                        shouldReturnToPool = true;
                    }
                }

                if (shouldReturnToPool)
                {
                    _pool?.Return(this);
                }
            }
        }

        public KcpNetworkConnectionNegotiationOperation(KcpNetworkConnectionNegotiationOperationPool? pool)
        {
            _pool = pool;

            _mrvtsc = new ManualResetValueTaskSourceCore<bool>
            {
                RunContinuationsAsynchronously = true
            };
        }

        public void Initialize(KcpNetworkConnection networkConnection, IKcpConnectionNegotiationContext negotiationContext)
        {
            _networkConnection = networkConnection;
            _negotiationContext = negotiationContext;
        }

        public ValueTask<bool> NegotiateAsync(KcpRentedBuffer cachedPacket, CancellationToken cancellationToken)
        {
            KcpNetworkConnection? networkConnection = _networkConnection;
            if (networkConnection is null)
            {
                cachedPacket.Dispose();
                return new ValueTask<bool>(false);
            }

            short token;
            lock (_lock)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    if (!_isActive)
                    {
                        _pool?.Return(this);
                    }
                    cachedPacket.Dispose();
                    return ValueTask.FromCanceled<bool>(cancellationToken);
                }
                if (_isActive)
                {
                    cachedPacket.Dispose();
                    return ValueTask.FromException<bool>(new InvalidOperationException());
                }

                _isActive = true;
                _isRunning = true;
                _isCanceled = false;
                _isDisposed = false;
                _cancellationToken = cancellationToken;
                token = _mrvtsc.Version;

                lock (_activityLock)
                {
                    _sendSerial = 0;
                    _sendTicks = (uint)Environment.TickCount;
                    _sendBuffer = default;
                    _sendRetryCount = 0;

                    _receiveSerial = 0;
                    _receiveBuffer = default;
                    _receivedSessionId = 0;

                    if (_timer is null)
                    {
                        _timer = new Timer(static state =>
                        {
                            var reference = (WeakReference<KcpNetworkConnectionNegotiationOperation>)state!;
                            if (reference.TryGetTarget(out var target))
                            {
                                target.Execute();
                            }
                        }, new WeakReference<KcpNetworkConnectionNegotiationOperation>(this), TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
                    }
                }
            }

            _cancellationRegistration = cancellationToken.UnsafeRegister(s => ((KcpNetworkConnectionNegotiationOperation?)s!).SetCanceled(), this);

            if (cachedPacket.IsAllocated)
            {
                InputPacket(cachedPacket.Span);
                cachedPacket.Dispose();
            }

            return new ValueTask<bool>(this, token);
        }

        private void SetCanceled()
        {
            lock (_lock)
            {
                if (_isActive && _isRunning)
                {
                    _isCanceled = true;
                }
            }
        }
        public void SetDisposed()
        {
            lock (_lock)
            {
                if (_isActive && _isRunning)
                {
                    _isDisposed = true;
                }
            }
        }

        private void ClearPreviousOperation()
        {
            _isRunning = false;
            _isCanceled = false;
            _isDisposed = false;
            _cancellationToken = default;
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;

            lock (_activityLock)
            {
                if (_timer is not null)
                {
                    _timer.Dispose();
                    _timer = null;
                }

                _sendSerial = 0;
                _sendTicks = 0;
                _sendBuffer.Dispose();
                _sendBuffer = default;
                _sendRetryCount = 0;

                _receiveSerial = 0;
                _receiveBuffer.Dispose();
                _receivedSessionId = 0;
            }
        }

        public void Execute()
        {
            if (Interlocked.Exchange(ref _tickOperationActive, 1) != 0)
            {
                return;
            }
            lock (_lock)
            {
                if (!_isActive || !_isRunning)
                {
                    Interlocked.Exchange(ref _tickOperationActive, 0);
                    return;
                }
                if (_isCanceled)
                {
                    CancellationToken cancellationToken = _cancellationToken;
                    ClearPreviousOperation();
                    _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                    Interlocked.Exchange(ref _tickOperationActive, 0);
                    return;
                }
                if (_isDisposed)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetResult(false);
                    Interlocked.Exchange(ref _tickOperationActive, 0);
                    return;
                }
            }
            bool? result = null;
            try
            {
                result = UpdateCore();
            }
            catch (Exception)
            {
                // don't leak exceptions
                result = false;
            }
            finally
            {
                Exception? exceptionToThrow = null;
                lock (_lock)
                {
                    if (_isActive)
                    {
                        Debug.Assert(_isRunning);

                        if (_isDisposed)
                        {
                            ClearPreviousOperation();
                            _mrvtsc.SetResult(false);
                        }
                        else if (_isCanceled)
                        {
                            // TODO do callback outside lock
                            CancellationToken cancellationToken = _cancellationToken;
                            ClearPreviousOperation();
                            exceptionToThrow = new OperationCanceledException(cancellationToken);
                        }
                        else if (result.HasValue)
                        {
                            // TODO do callback outside lock
                            ClearPreviousOperation(); ;
                        }
                    }
                }

                if (exceptionToThrow is not null)
                {
                    _networkConnection?.NotifyNegotiationResult(this, false, null);
                    _mrvtsc.SetException(exceptionToThrow);
                }
                else if (result.HasValue)
                {
                    _networkConnection?.NotifyNegotiationResult(this, result.GetValueOrDefault(), _negotiationContext?.NegotiatedMtu);
                    _mrvtsc.SetResult(result.GetValueOrDefault());
                }

                Interlocked.Exchange(ref _tickOperationActive, 0);
            }
        }

        private bool? UpdateCore()
        {
            const int HeaderSize = 12;

            KcpNetworkConnection? networkConnection = _networkConnection;
            IKcpConnectionNegotiationContext? negotiationContext = _negotiationContext;
            if (networkConnection is null || negotiationContext is null)
            {
                return false;
            }
            IKcpBufferPool? bufferPool = networkConnection.GetAllocator();

            lock (_activityLock)
            {
                Debug.Assert(_isActive && _isRunning);
                if (_isCanceled || _isDisposed)
                {
                    return false;
                }

                // receiving side
                if (_receiveBuffer.IsAllocated)
                {
                    negotiationContext.SetSessionId(_receivedSessionId);
                    KcpConnectionNegotiationResult result = negotiationContext.PutNegotiationData(_receiveBuffer.Span);
                    _receiveBuffer.Dispose();
                    _receiveBuffer = default;

                    if (result.IsSucceeded)
                    {
                        return true;
                    }
                    if (result.IsFailed)
                    {
                        return false;
                    }
                }

            }

            lock (_activityLock)
            {
                Debug.Assert(_isActive && _isRunning);
                if (_isCanceled || _isDisposed)
                {
                    return false;
                }

                // sending side
                if (!_sessionId.HasValue)
                {
                    if (negotiationContext.TryGetSessionId(out uint value))
                    {
                        _sessionId = value;
                    }
                }

                if ((int)(_sendTicks - GetCurrentTicks()) > 0)
                {
                    if (!_sessionId.HasValue)
                    {
                        if (negotiationContext.TryGetSessionId(out uint value))
                        {
                            _sessionId = value;
                        }
                    }

                    if (_sessionId.HasValue)
                    {
                        KcpRentedBuffer rentedBuffer = _sendBuffer;
                        if (!rentedBuffer.IsAllocated)
                        {
                            // fill send buffer
                            rentedBuffer = bufferPool.Rent(new KcpBufferPoolRentOptions(HeaderSize + 256, false));
                            KcpConnectionNegotiationResult result = negotiationContext.GetNegotiationData(rentedBuffer.Span.Slice(HeaderSize, 256));
                            if (result.IsSucceeded)
                            {
                                rentedBuffer.Dispose();
                                return true;
                            }
                            if (result.IsFailed)
                            {
                                rentedBuffer.Dispose();
                                return false;
                            }
                            rentedBuffer = rentedBuffer.Slice(0, HeaderSize + result.BytesWritten);
                            _sendBuffer = rentedBuffer;
                            Debug.Assert(_sendRetryCount == 0);

                            // fill headers
                            FillSendHeaders(_sendBuffer.Span, _sessionId.GetValueOrDefault(), _sendSerial, _receiveSerial);
                        }

                        // send this
                        bool sendResult = networkConnection.QueueRawPacket(rentedBuffer.Span);

                        if (!sendResult)
                        {
                            _sendTicks = GetCurrentTicks() + 1000;
                        }
                        else
                        {
                            _sendTicks = GetCurrentTicks() + DetermineSendInterval(ref _sendRetryCount);
                        }
                    }
                }
            }

            return null;
        }

        private static void FillSendHeaders(Span<byte> buffer, uint sessionId, byte localSerial, byte remoteSerial)
        {
            if (buffer.Length < 12)
            {
                Debug.Fail("Invalid buffer.");
                return;
            }

            buffer[0] = 1;
            buffer[1] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2), (ushort)buffer.Length);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(4), sessionId);
            buffer[8] = localSerial;
            buffer[9] = remoteSerial;
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(10), (ushort)(buffer.Length - 4));
        }

        private static uint DetermineSendInterval(ref byte retryCount)
        {
            if (retryCount <= 1)
            {
                retryCount++;
                return 1000;
            }
            if (retryCount <= 4)
            {
                retryCount++;
                return (1u << (retryCount - 1)) * 1000;
            }
            return 10 * 1000;
        }

        public bool InputPacket(ReadOnlySpan<byte> packet)
        {
            // validate packet
            if (packet.Length < 12)
            {
                return false;
            }
            if (packet[0] != 1 && packet[1] != 0)
            {
                return false;
            }
            ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2));
            if ((packet.Length - 4) < payloadLength)
            {
                return false;
            }
            uint packetSessionId = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4));
            packet = packet.Slice(8);

            byte remoteSerial = packet[0];
            byte localSerial = packet[1];
            if (packet[2] != 0)
            {
                return false;
            }
            ushort negotiationLength = packet[3];

            packet = packet.Slice(4);
            if (packet.Length < negotiationLength)
            {
                return false;
            }
            packet = packet.Slice(0, negotiationLength);

            lock (_lock)
            {
                if (!_isActive)
                {
                    return false;
                }
                if (_isCanceled)
                {
                    CancellationToken cancellationToken = _cancellationToken;
                    ClearPreviousOperation();
                    _mrvtsc.SetException(new OperationCanceledException(cancellationToken));
                    return false;
                }
                if (_isDisposed)
                {
                    ClearPreviousOperation();
                    _mrvtsc.SetResult(false);
                    return false;
                }
            }

            IKcpBufferPool? bufferPool = _networkConnection?.GetAllocator();
            if (bufferPool is null)
            {
                return false;
            }

            lock (_activityLock)
            {
                if (remoteSerial == (1 + _sendSerial))
                {
                    _sendSerial++;
                    _sendTicks = GetCurrentTicks();
                    if (_sendBuffer.IsAllocated)
                    {
                        _sendBuffer.Dispose();
                        _sendBuffer = default;
                    }
                    _sendRetryCount = 0;
                }

                if (localSerial == _receiveSerial)
                {
                    _receiveSerial++;
                    if (!_receiveBuffer.IsAllocated || _receiveBuffer.Span.Length < negotiationLength)
                    {
                        _receiveBuffer.Dispose();
                        _receiveBuffer = bufferPool.Rent(new KcpBufferPoolRentOptions(negotiationLength, false));
                    }
                    packet.CopyTo(_receiveBuffer.Span);
                    _receiveBuffer = _receiveBuffer.Slice(0, negotiationLength);
                    _receivedSessionId = packetSessionId;
                }
            }

            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
            return true;
        }

        private static uint GetCurrentTicks() => (uint)Environment.TickCount;
    }
}
