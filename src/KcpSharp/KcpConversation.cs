using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

#if NEED_LINKEDLIST_SHIM
using LinkedListOfBufferItem = KcpSharp.NetstandardShim.LinkedList<KcpSharp.KcpSendReceiveBufferItem>;
using LinkedListNodeOfBufferItem = KcpSharp.NetstandardShim.LinkedListNode<KcpSharp.KcpSendReceiveBufferItem>;
#else
using LinkedListOfBufferItem = System.Collections.Generic.LinkedList<KcpSharp.KcpSendReceiveBufferItem>;
using LinkedListNodeOfBufferItem = System.Collections.Generic.LinkedListNode<KcpSharp.KcpSendReceiveBufferItem>;
#endif

namespace KcpSharp
{
    /// <summary>
    /// A reliable channel over an unreliable transport implemented in KCP protocol.
    /// </summary>
    public sealed class KcpConversation : IKcpConversation
    {
        private readonly IKcpBufferAllocator _allocator;
        private readonly IKcpTransport _connection;
        private readonly uint _id;

        private readonly int _mtu;
        private readonly int _mss;

        private uint _snd_una;
        private uint _snd_nxt;
        private uint _rcv_nxt;

        private uint _ssthresh;

        private int _rx_rttval;
        private int _rx_srtt;
        private uint _rx_rto;
        private uint _rx_minrto;

        private uint _snd_wnd;
        private uint _rcv_wnd;
        private uint _rmt_wnd;
        private uint _cwnd;
        private KcpProbeType _probe;
        private SpinLock _cwndUpdateLock;

        private uint _current;
        private uint _interval;
        private uint _ts_flush;
        private uint _xmit;
        private bool _updated;

        private bool _nodelay;
        private uint _ts_probe;
        private uint _probe_wait;

        private uint _dead_link;
        private uint _incr;

        private readonly KcpSendReceiveQueueItemCache _queueItemCache;
        private readonly KcpSendQueue _sendQueue;
        private readonly KcpReceiveQueue _receiveQueue;

        private readonly LinkedListOfBufferItem _sndBuf = new();
        private readonly LinkedListOfBufferItem _rcvBuf = new();
        private KcpSendReceiveBufferItemCache _cache = KcpSendReceiveBufferItemCache.Create();

        private readonly KcpAcknowledgeList _ackList;

        private int _fastresend;
        private int _fastlimit;
        private bool _nocwnd;
        private bool _stream;

        private KcpConversationUpdateNotification? _updateEvent;
        private CancellationTokenSource? _checkLoopCts;
        private CancellationTokenSource? _updateLoopCts;
        private bool _transportClosed;
        private bool _disposed;

        private Func<Exception, KcpConversation, object?, bool>? _exceptionHandler;
        private object? _exceptionHandlerState;

        private const uint IKCP_RTO_MAX = 60000;
        private const int IKCP_THRESH_MIN = 2;
        private const uint IKCP_PROBE_INIT = 7000;       // 7 secs to probe window size
        private const uint IKCP_PROBE_LIMIT = 120000;    // up to 120 secs to probe window

        /// <summary>
        /// Construct a reliable channel using KCP protocol.
        /// </summary>
        /// <param name="connection">The underlying transport.</param>
        /// <param name="conversationId">The conversation ID.</param>
        /// <param name="options">The options of the <see cref="KcpConversation"/>.</param>
        public KcpConversation(IKcpTransport connection, int conversationId, KcpConversationOptions? options)
        {
            _allocator = options?.BufferAllocator ?? DefaultArrayPoolBufferAllocator.Default;
            _connection = connection;
            _id = (uint)conversationId;

            if (options is null)
            {
                _mtu = KcpConversationOptions.MtuDefaultValue;
            }
            else if (options.Mtu < 50)
            {
                throw new ArgumentException("MTU must be at least 50.", nameof(options));
            }
            else
            {
                _mtu = options.Mtu;
            }

            _mss = _mtu - 24;

            _ssthresh = 2;

            _nodelay = options is not null && options.NoDelay;
            if (_nodelay)
            {
                _rx_minrto = 30;
            }
            else
            {
                _rx_rto = 200;
                _rx_minrto = 100;
            }

            _snd_wnd = options is null || options.SendWindow <= 0 ? KcpConversationOptions.SendWindowDefaultValue : (uint)options.SendWindow;
            _rcv_wnd = options is null || options.ReceiveWindow <= 0 ? KcpConversationOptions.ReceiveWindowDefaultValue : (uint)options.ReceiveWindow;
            _rmt_wnd = options is null || options.RemoteReceiveWindow <= 0 ? KcpConversationOptions.RemoteReceiveWindowDefaultValue : (uint)options.RemoteReceiveWindow;
            _rcv_nxt = 0;

            _interval = options is null || options.UpdateInterval < 10 ? KcpConversationOptions.UpdateIntervalDefaultValue : (uint)options.UpdateInterval;

            _fastresend = options is null ? 0 : options.FastResend;
            _fastlimit = 5;
            _nocwnd = options is not null && options.DisableCongestionControl;
            _stream = options is not null && options.StreamMode;

            _dead_link = 20;

            _ackList = new KcpAcknowledgeList((int)_snd_wnd);
            _updateEvent = new KcpConversationUpdateNotification();
            _queueItemCache = new KcpSendReceiveQueueItemCache();
            _sendQueue = new KcpSendQueue(_allocator, _updateEvent, _stream, options is null || options.SendQueueSize <= 0 ? KcpConversationOptions.SendQueueSizeDefaultValue : options.SendQueueSize, _mss, _queueItemCache);
            _receiveQueue = new KcpReceiveQueue(_stream, _queueItemCache);

            _checkLoopCts = new CancellationTokenSource();
            _updateLoopCts = new CancellationTokenSource();

            _ts_flush = _interval;

            _ = Task.Run(() => CheckAndSignalLoopAsync(_checkLoopCts));
            _ = Task.Run(() => RunUpdateOnEventLoopAsync(_updateLoopCts));
        }

        #region Exception handlers

        /// <summary>
        /// Set the handler to invoke when exception is thrown during flushing packets to the transport. Return true in the handler to ignore the error and continue running. Return false in the handler to abort the operation and mark the transport as closed.
        /// </summary>
        /// <param name="handler">The exception handler.</param>
        /// <param name="state">The state object to pass into the exception handler.</param>
        public void SetExceptionHandler(Func<Exception, KcpConversation, object?, bool> handler, object? state)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _exceptionHandler = handler;
            _exceptionHandlerState = state;
        }

        /// <summary>
        /// Set the handler to invoke when exception is thrown during flushing packets to the transport. Return true in the handler to ignore the error and continue running. Return false in the handler to abort the operation and mark the transport as closed.
        /// </summary>
        /// <param name="handler">The exception handler.</param>
        public void SetExceptionHandler(Func<Exception, KcpConversation, bool> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _exceptionHandler = (ex, conv, state) => ((Func<Exception, KcpConversation, bool>?)state)!.Invoke(ex, conv);
            _exceptionHandlerState = handler;
        }

        /// <summary>
        /// Set the handler to invoke when exception is thrown during flushing packets to the transport. Return true in the handler to ignore the error and continue running. Return false in the handler to abort the operation and mark the transport as closed.
        /// </summary>
        /// <param name="handler">The exception handler.</param>
        public void SetExceptionHandler(Func<Exception, bool> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _exceptionHandler = (ex, conv, state) => ((Func<Exception, bool>?)state)!.Invoke(ex);
            _exceptionHandlerState = handler;
        }

        /// <summary>
        /// Set the handler to invoke when exception is thrown during flushing packets to the transport. The transport will be marked as closed after the exception handler in invoked.
        /// </summary>
        /// <param name="handler">The exception handler.</param>
        /// <param name="state">The state object to pass into the exception handler.</param>
        public void SetExceptionHandler(Action<Exception, KcpConversation, object?> handler, object? state)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _exceptionHandler = (ex, conv, state) =>
            {
                var tuple = (Tuple<Action<Exception, KcpConversation, object?>, object?>)state!;
                tuple.Item1.Invoke(ex, conv, tuple.Item2);
                return false;
            };
            _exceptionHandlerState = Tuple.Create(handler, state);
        }

        /// <summary>
        /// Set the handler to invoke when exception is thrown during flushing packets to the transport. The transport will be marked as closed after the exception handler in invoked.
        /// </summary>
        /// <param name="handler">The exception handler.</param>
        public void SetExceptionHandler(Action<Exception, KcpConversation> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _exceptionHandler = (ex, conv, state) =>
            {
                var handler = (Action<Exception, KcpConversation>)state!;
                handler.Invoke(ex, conv);
                return false;
            };
            _exceptionHandlerState = handler;
        }

        /// <summary>
        /// Set the handler to invoke when exception is thrown during flushing packets to the transport. The transport will be marked as closed after the exception handler in invoked.
        /// </summary>
        /// <param name="handler">The exception handler.</param>
        public void SetExceptionHandler(Action<Exception> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _exceptionHandler = (ex, conv, state) =>
            {
                var handler = (Action<Exception>)state!;
                handler.Invoke(ex);
                return false;
            };
            _exceptionHandlerState = handler;
        }

        #endregion

        /// <summary>
        /// Get the ID of the current conversation.
        /// </summary>
        public int ConversationId => (int)_id;

        /// <summary>
        /// Get whether the transport is marked as closed.
        /// </summary>
        public bool TransportClosed => _transportClosed;

        /// <summary>
        /// Put message into the send queue.
        /// </summary>
        /// <param name="buffer">The content of the message</param>
        /// <param name="cancellationToken">The token to cancel this operation.</param>
        /// <exception cref="ArgumentException">The size of the message is larger than 256 * mtu, thus it can not be correctly fragmented and sent. This exception is never thrown in stream mode.</exception>
        /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> is fired before send operation is completed.</exception>
        /// <exception cref="InvalidOperationException">The send or flush operation is initiated concurrently.</exception>
        /// <exception cref="ObjectDisposedException">The <see cref="KcpConversation"/> instance is disposed.</exception>
        /// <returns>A <see cref="ValueTask{Boolean}"/> that completes when the entire message is put into the queue. The result of the task is false when the transport is closed.</returns>
        public ValueTask<bool> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            => _sendQueue.SendAsync(buffer, cancellationToken);

        /// <summary>
        /// Gets the count of bytes not yet sent to the remote host or not acknowledged by the remote host.
        /// </summary>
        public long UnflushedBytes => _sendQueue.GetUnflushedBytes();

        /// <summary>
        /// Wait until all messages are sent and acknowledged by the remote host.
        /// </summary>
        /// <param name="cancellationToken">The token to cancel this operation.</param>
        /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> is fired before send operation is completed.</exception>
        /// <exception cref="InvalidOperationException">The send or flush operation is initiated concurrently.</exception>
        /// <exception cref="ObjectDisposedException">The <see cref="KcpConversation"/> instance is disposed.</exception>
        /// <returns>A <see cref="ValueTask{Boolean}"/> that completes when the all messages are sent and acknowledged. The result of the task is false when the transport is closed.</returns>
        public ValueTask<bool> FlushAsync(CancellationToken cancellationToken)
            => _sendQueue.FlushAsync(cancellationToken);

        private int Check(uint current)
        {
            uint nextFlushTimestamp = _ts_flush;
            int minFlushDiff = int.MaxValue;

            if (TimeDiff(current, nextFlushTimestamp) >= 10000 || TimeDiff(current, nextFlushTimestamp) < -10000)
            {
                nextFlushTimestamp = current;
            }

            if (TimeDiff(current, nextFlushTimestamp) >= 0)
            {
                return 0;
            }

            int nextFlushDiff = TimeDiff(nextFlushTimestamp, current);

            lock (_sndBuf)
            {
                LinkedListNodeOfBufferItem? node = _sndBuf.First;
                while (node is not null)
                {
                    ref KcpSendReceiveBufferItem item = ref node.ValueRef;
                    ref KcpSendSegmentStats segment = ref item.Stats;

                    int diff = TimeDiff(segment.ResendTimestamp, current);
                    if (diff <= 0)
                    {
                        return 0;
                    }
                    if (diff < minFlushDiff)
                    {
                        minFlushDiff = diff;
                    }
                    node = node.Next;
                }
            }

            int minimal = minFlushDiff < nextFlushDiff ? minFlushDiff : nextFlushDiff;
            return Math.Min(minimal, (int)_interval);
        }

        private async Task FlushCoreAsync(CancellationToken cancellationToken)
        {
            ushort windowSize = (ushort)GetUnusedReceiveWindow();
            uint unacknowledged = _rcv_nxt;

            using IMemoryOwner<byte> bufferOwner = _allocator.Allocate(_mtu);
            Memory<byte> buffer = bufferOwner.Memory;
            int size = 0;

            // flush acknowledges
            {
                int index = 0;
                while (_ackList.TryGetAt(index++, out uint serialNumber, out uint timestamp))
                {
                    if ((size + 24) > _mtu)
                    {
                        await _connection.SendPacketAsync(buffer.Slice(0, size), cancellationToken).ConfigureAwait(false);
                        size = 0;
                    }
                    var header = new KcpPacketHeader(KcpCommand.Ack, 0, windowSize, timestamp, serialNumber, unacknowledged);
                    header.EncodeHeader(_id, 0, buffer.Span.Slice(size));
                    size += 24;
                }

                _ackList.Clear();
            }

            uint current = _current = GetTimestamp();

            // probe window size (if remote window size equals zero)
            if (_rmt_wnd == 0)
            {
                if (_probe_wait == 0)
                {
                    _probe_wait = IKCP_PROBE_INIT;
                    _ts_probe = current + _probe_wait;
                }
                else
                {
                    if (TimeDiff(current, _ts_probe) >= 0)
                    {
                        if (_probe_wait < IKCP_PROBE_INIT)
                        {
                            _probe_wait = IKCP_PROBE_INIT;
                        }
                        _probe_wait += _probe_wait / 2;
                        if (_probe_wait > IKCP_PROBE_LIMIT)
                        {
                            _probe_wait = IKCP_PROBE_LIMIT;
                        }
                        _ts_probe = current + _probe_wait;
                        _probe |= KcpProbeType.AskSend;
                    }
                }
            }
            else
            {
                _ts_probe = 0;
                _probe_wait = 0;
            }

            // flush window probing commands
            if ((_probe & KcpProbeType.AskSend) != 0)
            {
                if ((size + 24) > _mtu)
                {
                    await _connection.SendPacketAsync(buffer.Slice(0, size), cancellationToken).ConfigureAwait(false);
                    size = 0;
                }
                var header = new KcpPacketHeader(KcpCommand.WindowProbe, 0, windowSize, 0, 0, unacknowledged);
                header.EncodeHeader(_id, 0, buffer.Span.Slice(size));
                size += 24;
            }

            // flush window probing commands
            if ((_probe & KcpProbeType.AskTell) != 0)
            {
                if ((size + 24) > _mtu)
                {
                    await _connection.SendPacketAsync(buffer.Slice(0, size), cancellationToken).ConfigureAwait(false);
                    size = 0;
                }
                var header = new KcpPacketHeader(KcpCommand.WindowSize, 0, windowSize, 0, 0, unacknowledged);
                header.EncodeHeader(_id, 0, buffer.Span.Slice(size));
                size += 24;
            }

            _probe = KcpProbeType.None;

            // calculate window size
            uint cwnd = Math.Min(_snd_wnd, _rmt_wnd);
            if (!_nocwnd)
            {
                cwnd = Math.Min(_cwnd, cwnd);
            }

            // move data from snd_queue to snd_buf
            while (TimeDiff(_snd_nxt, _snd_una + cwnd) < 0)
            {
                if (!_sendQueue.TryDequeue(TimeDiff(_snd_una, _snd_nxt), out KcpBuffer data, out byte fragment))
                {
                    break;
                }

                lock (_sndBuf)
                {
                    if (_transportClosed)
                    {
                        data.Release();
                        return;
                    }

                    _sndBuf.AddLast(CreateSendBufferItem(ref data, fragment, current, windowSize, (uint)Interlocked.Increment(ref Unsafe.As<uint, int>(ref _snd_nxt)) - 1, unacknowledged, _rx_rto));
                }
            }

            // calculate resent
            uint resent = _fastresend > 0 ? (uint)_fastresend : 0xffffffff;
            uint rtomin = !_nodelay ? (_rx_rto >> 3) : 0;

            // flush data segments
            bool lost = false;
            bool change = false;
            LinkedListNodeOfBufferItem? segmentNode;
            lock (_sndBuf)
            {
                segmentNode = _sndBuf.First;
            }
            while (segmentNode is not null)
            {
                LinkedListNodeOfBufferItem? nextSegmentNode;
                lock (_sndBuf)
                {
                    if (_transportClosed)
                    {
                        return;
                    }
                    nextSegmentNode = segmentNode.Next;
                }

                bool needsend = false;
                KcpSendSegmentStats stats = segmentNode.ValueRef.Stats;

                if (segmentNode.ValueRef.Stats.TransmitCount == 0)
                {
                    needsend = true;
                    segmentNode.ValueRef.Stats = new KcpSendSegmentStats(current + segmentNode.ValueRef.Stats.Rto + rtomin, _rx_rto, stats.FastAck, stats.TransmitCount + 1);
                }
                else if (TimeDiff(current, stats.ResendTimestamp) >= 0)
                {
                    needsend = true;
                    _xmit++;
                    uint rto = stats.Rto;
                    if (!_nodelay)
                    {
                        rto += Math.Max(stats.Rto, _rx_rto);
                    }
                    else
                    {
                        uint step = rto; //_nodelay < 2 ? segment.rto : _rx_rto;
                        rto += step / 2;
                    }
                    segmentNode.ValueRef.Stats = new KcpSendSegmentStats(current + rto, rto, stats.FastAck, stats.TransmitCount + 1);
                    lost = true;
                }
                else if (stats.FastAck > resent)
                {
                    if (stats.TransmitCount <= _fastlimit || _fastlimit == 0)
                    {
                        needsend = true;
                        segmentNode.ValueRef.Stats = new KcpSendSegmentStats(current, stats.Rto, 0, stats.TransmitCount + 1);
                        change = true;
                    }
                }

                if (needsend)
                {
                    KcpBuffer data = segmentNode.ValueRef.Data;
                    KcpPacketHeader header = DeplicateHeader(ref segmentNode.ValueRef.Segment, current, windowSize, unacknowledged);

                    int need = 24 + data.Length;
                    if ((size + need) > _mtu)
                    {
                        await _connection.SendPacketAsync(buffer.Slice(0, size), CancellationToken.None).ConfigureAwait(false);
                        size = 0;
                    }

                    header.EncodeHeader(_id, data.Length, buffer.Span.Slice(size));

                    size += 24;

                    if (data.Length > 0)
                    {
                        data.DataRegion.CopyTo(buffer.Slice(size));
                        size += data.Length;
                    }

                    if (stats.TransmitCount >= _dead_link)
                    {
                        // TODO dead link
                        // state = -1;
                    }
                }

                segmentNode = nextSegmentNode;
            }

            // flush remaining segments
            if (size > 0)
            {
                await _connection.SendPacketAsync(buffer.Slice(0, size), cancellationToken).ConfigureAwait(false);
            }

            {
                bool lockTaken = false;
                try
                {
                    _cwndUpdateLock.Enter(ref lockTaken);

                    uint updatedCwnd = _cwnd;
                    uint incr = _incr;

                    // update sshthresh
                    if (change)
                    {
                        uint inflight = _snd_nxt - _snd_una;
                        _ssthresh = Math.Max(inflight / 2, IKCP_THRESH_MIN);
                        updatedCwnd = _ssthresh + resent;
                        incr = updatedCwnd * (uint)_mss;
                    }

                    if (lost)
                    {
                        _ssthresh = Math.Max(cwnd / 2, IKCP_THRESH_MIN);
                        updatedCwnd = 1;
                        incr = (uint)_mss;
                    }

                    if (updatedCwnd < 1)
                    {
                        updatedCwnd = 1;
                        incr = (uint)_mss;
                    }

                    _cwnd = updatedCwnd;
                    _incr = incr;
                }
                finally
                {
                    if (lockTaken)
                    {
                        _cwndUpdateLock.Exit();
                    }
                }
            }

        }

        private LinkedListNodeOfBufferItem CreateSendBufferItem(ref KcpBuffer data, byte fragment, uint current, ushort windowSize, uint serialNumber, uint unacknowledged, uint rto)
        {
            var newseg = new KcpSendReceiveBufferItem
            {
                Data = data,
                Segment = new KcpPacketHeader(KcpCommand.Push, fragment, windowSize, current, serialNumber, unacknowledged),
                Stats = new KcpSendSegmentStats(current, rto, 0, 0)
            };
            return _cache.Allocate(ref newseg);
        }

        private static KcpPacketHeader DeplicateHeader(ref KcpPacketHeader header, uint timestamp, ushort windowSize, uint unacknowledged)
        {
            return new KcpPacketHeader(header.Command, header.Fragment, windowSize, timestamp, header.SerialNumber, unacknowledged);
        }

        private uint GetUnusedReceiveWindow()
        {
            uint count = (uint)_receiveQueue.GetQueueSize();
            if (count < _rcv_wnd)
            {
                return _rcv_wnd - count;
            }
            return 0;
        }

        private async Task CheckAndSignalLoopAsync(CancellationTokenSource cts)
        {
            KcpConversationUpdateNotification? ev = _updateEvent;
            if (ev is null)
            {
                return;
            }
            CancellationToken cancellationToken = cts.Token;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    uint current = _current = GetTimestamp();

                    int wait = Check(current);
                    if (wait != 0)
                    {
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                        current = _current = GetTimestamp();
                        ev.TrySet(true);
                    }
                    else
                    {
                        if (!ev.TrySet(true, out bool previouslySet) || previouslySet)
                        {
                            await Task.Delay((int)_interval, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // do nothing
            }
            finally
            {
                cts.Dispose();
            }

        }

        private async Task RunUpdateOnEventLoopAsync(CancellationTokenSource cts)
        {
            CancellationToken cancellationToken = cts.Token;
            KcpConversationUpdateNotification? ev = _updateEvent;
            if (ev is null)
            {
                return;
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    bool isPeriodic = await ev.WaitAsync(cancellationToken).ConfigureAwait(false);

                    uint current;
                    if (!isPeriodic)
                    {
                        current = _current = GetTimestamp();
                        int wait = Check(current);
                        if (wait != 0)
                        {
                            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    current = _current = GetTimestamp();
                    try
                    {
                        await UpdateCoreAsync(current, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (!HandleFlushException(ex))
                        {
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Do nothing
            }
            catch (ObjectDisposedException)
            {
                // Do nothing
            }
            finally
            {
                cts.Dispose();
            }
        }

        private ValueTask UpdateCoreAsync(uint current, CancellationToken cancellationToken)
        {
            if (!_updated)
            {
                _updated = true;
                _ts_flush = current;
            }

            long slap = TimeDiff(current, _ts_flush);
            if (slap > 10000 || slap < -10000)
            {
                _ts_flush = current;
                slap = 0;
            }

            if (slap >= 0)
            {
                _ts_flush += _interval;
                if (TimeDiff(current, _ts_flush) >= 0)
                {
                    _ts_flush = current + _interval;
                }
                return new ValueTask(FlushCoreAsync(cancellationToken));
            }
            return default;
        }

        private bool HandleFlushException(Exception ex)
        {
            Func<Exception, KcpConversation, object?, bool>? handler = _exceptionHandler;
            object? state = _exceptionHandlerState;
            bool result = false;
            if (handler is not null)
            {
                try
                {
                    result = handler.Invoke(ex, this, state);
                }
                catch
                {
                    result = false;
                }
            }

            if (!result)
            {
                SetTransportClosed();
            }
            return result;
        }

        /// <inheritdoc />
        public ValueTask OnReceivedAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask(Task.FromCanceled(cancellationToken));
            }
            if (packet.Length < 24)
            {
                return default;
            }

            ReadOnlySpan<byte> packetSpan = packet.Span;
            uint conversationId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Span);
            if (conversationId != _id)
            {
                return default;
            }
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(packet.Span.Slice(20));
            if (length > (uint)(packetSpan.Length - 24)) // implicitly checked for (int)length < 0
            {
                return default;
            }

            OnReceivedCore(packetSpan);
            return default;
        }

        private void OnReceivedCore(ReadOnlySpan<byte> packet)
        {
            if (_transportClosed)
            {
                return;
            }

            try
            {
                SetInput(packet);
            }
            finally
            {
                _updateEvent?.TrySet(false);
            }
        }

        private void SetInput(ReadOnlySpan<byte> packet)
        {
            uint prev_una = _snd_una;
            uint maxack = 0, latest_ts = 0;
            bool flag = false;

            while (true)
            {
                if (packet.Length < 24)
                {
                    break;
                }

                if (BinaryPrimitives.ReadUInt32LittleEndian(packet) != _id)
                {
                    return;
                }
                var header = KcpPacketHeader.Parse(packet.Slice(4));
                int length = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(20));

                packet = packet.Slice(24);
                if ((uint)length > (uint)packet.Length)
                {
                    return;
                }

                if (header.Command != KcpCommand.Push &&
                    header.Command != KcpCommand.Ack &&
                    header.Command != KcpCommand.WindowProbe &&
                    header.Command != KcpCommand.WindowSize)
                {
                    return;
                }

                _rmt_wnd = header.WindowSize;
                HandleUnacknowledged(header.Unacknowledged);
                UpdateSendUnacknowledged();

                if (header.Command == KcpCommand.Ack)
                {
                    int rtt = TimeDiff(_current, header.Timestamp);
                    if (rtt >= 0)
                    {
                        UpdateRto(rtt);
                    }
                    HandleAck(header.SerialNumber);
                    UpdateSendUnacknowledged();

                    if (!flag)
                    {
                        flag = true;
                        maxack = header.SerialNumber;
                        latest_ts = header.Timestamp;
                    }
                    else
                    {
                        if (TimeDiff(_snd_nxt, maxack) > 0)
                        {
#if !IKCP_FASTACK_CONSERVE
                            maxack = header.SerialNumber;
                            latest_ts = header.Timestamp;
#else
                            if (TimeDiff(header.Timestamp, latest_ts) > 0) {
						        maxack = header.SerialNumber;
						        latest_ts = header.Timestamp;
					        }
#endif
                        }
                    }
                }
                else if (header.Command == KcpCommand.Push)
                {
                    if (TimeDiff(header.SerialNumber, _rcv_nxt + _rcv_wnd) < 0)
                    {
                        AckPush(header.SerialNumber, header.Timestamp);
                        if (TimeDiff(header.SerialNumber, _rcv_nxt) >= 0)
                        {
                            HandleData(header, packet.Slice(0, length));
                        }
                    }
                }
                else if (header.Command == KcpCommand.WindowProbe)
                {
                    _probe |= KcpProbeType.AskTell;
                }
                else if (header.Command == KcpCommand.WindowSize)
                {
                    // do nothing
                }
                else
                {
                    return;
                }

                packet = packet.Slice(length);
            }

            if (flag)
            {
                HandleFastAck(maxack, latest_ts);
            }


            if (TimeDiff(_snd_una, prev_una) > 0)
            {
                bool lockTaken = false;
                try
                {
                    _cwndUpdateLock.Enter(ref lockTaken);

                    uint cwnd = _cwnd;
                    uint incr = _incr;

                    if (cwnd < _rmt_wnd)
                    {
                        uint mss = (uint)_mss;
                        if (cwnd < _ssthresh)
                        {
                            cwnd++;
                            incr += mss;
                        }
                        else
                        {
                            if (incr < mss)
                            {
                                incr = mss;
                            }
                            incr += (mss * mss) / incr + mss / 16;
                            cwnd = (incr + mss - 1) / (mss > 0 ? mss : 1);
                        }
                        if (cwnd > _rmt_wnd)
                        {
                            cwnd = _rmt_wnd;
                            incr = _rmt_wnd * mss;
                        }
                    }

                    _cwnd = cwnd;
                    _incr = incr;
                }
                finally
                {
                    if (lockTaken)
                    {
                        _cwndUpdateLock.Exit();
                    }
                }
            }
        }

        private void HandleUnacknowledged(uint una)
        {
            lock (_sndBuf)
            {
                LinkedListNodeOfBufferItem? node = _sndBuf.First;
                while (node is not null)
                {
                    LinkedListNodeOfBufferItem? next = node.Next;

                    if (TimeDiff(una, node.ValueRef.Segment.SerialNumber) > 0)
                    {
                        _sndBuf.Remove(node);
                        ref KcpBuffer dataRef = ref node.ValueRef.Data;
                        _sendQueue.SubtractUnflushedBytes(dataRef.Length);
                        dataRef.Release();
                        dataRef = default;
                        _cache.Return(node);
                    }
                    else
                    {
                        break;
                    }

                    node = next;
                }
            }
        }

        private void UpdateSendUnacknowledged()
        {
            lock (_sndBuf)
            {
                LinkedListNodeOfBufferItem? first = _sndBuf.First;
                if (first is not null)
                {
                    _snd_una = first.ValueRef.Segment.SerialNumber;
                }
                else
                {
                    _snd_una = _snd_nxt;
                }
            }
        }

        private void UpdateRto(int rtt)
        {
            if (_rx_srtt == 0)
            {
                _rx_srtt = rtt;
                _rx_rttval = rtt / 2;
            }
            else
            {
                int delta = rtt - _rx_srtt;
                if (delta < 0)
                {
                    delta = -delta;
                }
                _rx_rttval = (3 * _rx_rttval + delta) / 4;
                _rx_srtt = (7 * _rx_srtt + rtt) / 8;
                if (_rx_srtt < 1)
                {
                    _rx_srtt = 1;
                }
            }
            int rto = _rx_srtt + Math.Max((int)_interval, 4 * _rx_rttval);
#if NEED_MATH_SHIM
            _rx_rto = Math.Min(Math.Max((uint)rto, _rx_minrto), IKCP_RTO_MAX);
#else
            _rx_rto = Math.Clamp((uint)rto, _rx_minrto, IKCP_RTO_MAX);
#endif
        }

        private void HandleAck(uint serialNumber)
        {
            if (TimeDiff(serialNumber, _snd_una) < 0 || TimeDiff(serialNumber, _snd_nxt) >= 0)
            {
                return;
            }

            lock (_sndBuf)
            {
                LinkedListNodeOfBufferItem? node = _sndBuf.First;
                while (node is not null)
                {
                    LinkedListNodeOfBufferItem? next = node.Next;

                    if (serialNumber == node.ValueRef.Segment.SerialNumber)
                    {
                        _sndBuf.Remove(node);
                        ref KcpBuffer dataRef = ref node.ValueRef.Data;
                        _sendQueue.SubtractUnflushedBytes(dataRef.Length);
                        dataRef.Release();
                        dataRef = default;
                        _cache.Return(node);
                        break;
                    }

                    if (TimeDiff(serialNumber, node.ValueRef.Segment.SerialNumber) < 0)
                    {
                        break;
                    }

                    node = next;
                }
            }
        }

        private void HandleData(KcpPacketHeader header, ReadOnlySpan<byte> data)
        {
            uint serialNumber = header.SerialNumber;
            if (TimeDiff(serialNumber, _rcv_nxt + _rcv_wnd) >= 0 || TimeDiff(serialNumber, _rcv_nxt) < 0)
            {
                return;
            }

            bool repeat = false;
            LinkedListNodeOfBufferItem? node;
            lock (_rcvBuf)
            {
                if (_transportClosed)
                {
                    return;
                }
                node = _rcvBuf.Last;
                while (node is not null)
                {
                    uint nodeSerialNumber = node.ValueRef.Segment.SerialNumber;
                    if (serialNumber == nodeSerialNumber)
                    {
                        repeat = true;
                        break;
                    }
                    if (TimeDiff(serialNumber, nodeSerialNumber) > 0)
                    {
                        break;
                    }

                    node = node.Previous;
                }

                if (!repeat)
                {
                    IMemoryOwner<byte>? buffer = _allocator.Allocate(data.Length);
                    var item = new KcpSendReceiveBufferItem
                    {
                        Data = KcpBuffer.CreateFromSpan(buffer, data),
                        Segment = header
                    };
                    if (node is null)
                    {
                        _rcvBuf.AddFirst(_cache.Allocate(ref item));
                    }
                    else
                    {
                        _rcvBuf.AddAfter(node, _cache.Allocate(ref item));
                    }
                }

                // move available data from rcv_buf -> rcv_queue
                node = _rcvBuf.First;
                while (node is not null)
                {
                    LinkedListNodeOfBufferItem? next = node.Next;

                    if (node.ValueRef.Segment.SerialNumber == _rcv_nxt && _receiveQueue.GetQueueSize() < _rcv_wnd)
                    {
                        _rcvBuf.Remove(node);
                        _receiveQueue.Enqueue(node.ValueRef.Data, node.ValueRef.Segment.Fragment);
                        node.ValueRef.Data = default;
                        _cache.Return(node);
                        _rcv_nxt++;
                    }
                    else
                    {
                        break;
                    }

                    node = next;
                }
            }

        }

        private void AckPush(uint serialNumber, uint timestamp) => _ackList.Add(serialNumber, timestamp);

        private void HandleFastAck(uint serialNumber, uint timestamp)
        {
            if (TimeDiff(serialNumber, _snd_una) < 0 || TimeDiff(serialNumber, _snd_nxt) >= 0)
            {
                return;
            }

            lock (_sndBuf)
            {
                LinkedListNodeOfBufferItem? node = _sndBuf.First;
                while (node is not null)
                {
                    LinkedListNodeOfBufferItem? next = node.Next;
                    if (TimeDiff(serialNumber, node.ValueRef.Segment.SerialNumber) < 0)
                    {
                        break;
                    }
                    else if (serialNumber != node.ValueRef.Segment.SerialNumber)
                    {
                        ref KcpSendSegmentStats stats = ref node.ValueRef.Stats;
#if !IKCP_FASTACK_CONSERVE
                        stats = new KcpSendSegmentStats(stats.ResendTimestamp, stats.Rto, stats.FastAck + 1, stats.TransmitCount);
#else
                        if (TimeDiff(timestamp, node.ValueRef.Segment.Timestamp) >= 0)
                        {
                            stats = new KcpSendSegmentStats(stats.ResendTimestamp, stats.Rto, stats.FastAck + 1, stats.TransmitCount);
                        }
#endif
                    }

                    node = next;
                }
            }
        }

        private static uint GetTimestamp()
        {
            return (uint)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int TimeDiff(uint later, uint earlier)
            => (int)(later - earlier);

        /// <summary>
        /// Get the size of the next available message in the receive queue.
        /// </summary>
        /// <param name="result">The transport state and the size of the next available message.</param>
        /// <exception cref="InvalidOperationException">The receive or peek operation is initiated concurrently.</exception>
        /// <returns>True if the receive queue contains at least one message. False if the receive queue is empty or the transport is closed.</returns>
        public bool TryPeek(out KcpConversationReceiveResult result)
            => _receiveQueue.TryPeek(out result);

        /// <summary>
        /// Remove the next available message in the receive queue and copy its content into <paramref name="buffer"/>. When in stream mode, move as many bytes as possible into <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">The buffer to receive message.</param>
        /// <param name="result">The transport state and the count of bytes moved into <paramref name="buffer"/>.</param>
        /// <exception cref="ArgumentException">The size of the next available message is larger than the size of <paramref name="buffer"/>. This exception is never thrown in stream mode.</exception>
        /// <exception cref="InvalidOperationException">The receive or peek operation is initiated concurrently.</exception>
        /// <returns>True if the next available message is moved into <paramref name="buffer"/>. False if the receive queue is empty or the transport is closed.</returns>
        public bool TryReceive(Memory<byte> buffer, out KcpConversationReceiveResult result)
            => _receiveQueue.TryReceive(buffer, out result);

        /// <summary>
        /// Wait until the receive queue contains at least one full message, or at least one byte in stream mode.
        /// </summary>
        /// <param name="cancellationToken">The token to cancel this operation.</param>
        /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> is fired before receive operation is completed.</exception>
        /// <exception cref="InvalidOperationException">The receive or peek operation is initiated concurrently.</exception>
        /// <returns>A <see cref="ValueTask{KcpConversationReceiveResult}"/> that completes when the receive queue contains at least one full message, or at least one byte in stream mode. Its result contains the transport state and the size of the available message.</returns>
        public ValueTask<KcpConversationReceiveResult> WaitToReceiveAsync(CancellationToken cancellationToken)
            => _receiveQueue.WaitToReceiveAsync(cancellationToken);

        /// <summary>
        /// Wait for the next full message to arrive if the receive queue is empty. Remove the next available message in the receive queue and copy its content into <paramref name="buffer"/>. When in stream mode, move as many bytes as possible into <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">The buffer to receive message.</param>
        /// <param name="cancellationToken">The token to cancel this operation.</param>
        /// <exception cref="ArgumentException">The size of the next available message is larger than the size of <paramref name="buffer"/>. This exception is never thrown in stream mode.</exception>
        /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> is fired before send operation is completed.</exception>
        /// <exception cref="InvalidOperationException">The receive or peek operation is initiated concurrently.</exception>
        /// <returns>A <see cref="ValueTask{KcpConversationReceiveResult}"/> that completes when a full message is moved into <paramref name="buffer"/> or the transport is closed. Its result contains the transport state and the count of bytes written into <paramref name="buffer"/>.</returns>
        public ValueTask<KcpConversationReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            => _receiveQueue.ReceiveAsync(buffer, cancellationToken);

        /// <inheritdoc />
        public void SetTransportClosed()
        {
            _transportClosed = true;
            try
            {
                Interlocked.Exchange(ref _checkLoopCts, null)?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore
            }
            try
            {
                Interlocked.Exchange(ref _updateLoopCts, null)?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore
            }

            _sendQueue.SetTransportClosed();
            _receiveQueue.SetTransportClosed();
            lock (_sndBuf)
            {
                LinkedListNodeOfBufferItem? node = _sndBuf.First;
                while (node is not null)
                {
                    node.ValueRef.Data.Release();
                    node = node.Next;
                }
                _sndBuf.Clear();
            }
            lock (_rcvBuf)
            {
                LinkedListNodeOfBufferItem? node = _rcvBuf.First;
                while (node is not null)
                {
                    node.ValueRef.Data.Release();
                    node = node.Next;
                }
                _rcvBuf.Clear();
            }
            _queueItemCache.Clear();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            bool disposed = _disposed;
            _disposed = true;
            SetTransportClosed();
            if (!disposed)
            {
                _sendQueue.Dispose();
                _receiveQueue.Dispose();
                Interlocked.Exchange(ref _updateEvent, null)?.Dispose();
            }
        }

    }
}
