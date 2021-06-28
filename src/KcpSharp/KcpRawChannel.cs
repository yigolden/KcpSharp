using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp
{
    /// <summary>
    /// An unreliable channel with a conversation ID.
    /// </summary>
    public sealed class KcpRawChannel : IKcpConversation, IKcpExceptionProducer<KcpRawChannel>
    {
        private readonly IKcpBufferAllocator _allocator;
        private readonly IKcpTransport _transport;
        private readonly uint _id;
        private readonly int _mtu;

        private CancellationTokenSource? _sendLoopCts;
        private readonly KcpRawReceiveQueue _receiveQueue;
        private readonly KcpRawSendOperation _sendOperation;
        private readonly AsyncAutoResetEvent<int> _sendNotification;

        private Func<Exception, KcpRawChannel, object?, bool>? _exceptionHandler;
        private object? _exceptionHandlerState;

        /// <summary>
        /// Construct a unreliable channel with a conversation ID.
        /// </summary>
        /// <param name="transport">The underlying transport.</param>
        /// <param name="conversationId">The conversation ID.</param>
        /// <param name="options">The options of the <see cref="KcpRawChannel"/>.</param>
        public KcpRawChannel(IKcpTransport transport, int conversationId, KcpRawChannelOptions? options)
        {
            _allocator = options?.BufferAllocator ?? DefaultArrayPoolBufferAllocator.Default;
            _transport = transport;
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

            int queueSize = options?.ReceiveQueueSize ?? 32;
            if (queueSize < 1)
            {
                throw new ArgumentException("QueueSize must be a positive integer.", nameof(options));
            }

            _sendLoopCts = new CancellationTokenSource();
            _sendNotification = new AsyncAutoResetEvent<int>();
            _receiveQueue = new KcpRawReceiveQueue(_allocator, queueSize);
            _sendOperation = new KcpRawSendOperation(_sendNotification);

            _ = Task.Run(() => SendLoopAsync(_sendLoopCts));
        }

        /// <summary>
        /// Set the handler to invoke when exception is thrown during flushing packets to the transport. Return true in the handler to ignore the error and continue running. Return false in the handler to abort the operation and mark the transport as closed.
        /// </summary>
        /// <param name="handler">The exception handler.</param>
        /// <param name="state">The state object to pass into the exception handler.</param>
        public void SetExceptionHandler(Func<Exception, KcpRawChannel, object?, bool> handler, object? state)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _exceptionHandler = handler;
            _exceptionHandlerState = state;
        }

        /// <summary>
        /// Get the ID of the current conversation.
        /// </summary>
        public int ConversationId => (int)_id;

        /// <summary>
        /// Get whether the transport is marked as closed.
        /// </summary>
        public bool TransportClosed => _sendLoopCts is null;

        /// <summary>
        /// Send message to the underlying transport.
        /// </summary>
        /// <param name="buffer">The content of the message</param>
        /// <param name="cancellationToken">The token to cancel this operation.</param>
        /// <exception cref="ArgumentException">The size of the message is larger than mtu, thus it can not be sent.</exception>
        /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> is fired before send operation is completed.</exception>
        /// <exception cref="InvalidOperationException">The send operation is initiated concurrently.</exception>
        /// <exception cref="ObjectDisposedException">The <see cref="KcpConversation"/> instance is disposed.</exception>
        /// <returns>A <see cref="ValueTask{Boolean}"/> that completes when the entire message is put into the queue. The result of the task is false when the transport is closed.</returns>
        public ValueTask<bool> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => _sendOperation.SendAsync(buffer, cancellationToken);


        /// <summary>
        /// Cancel the current send operation or flush operation.
        /// </summary>
        /// <returns>True if the current operation is canceled. False if there is no active send operation.</returns>
        public bool CancelPendingSend()
            => _sendOperation.CancelPendingOperation(null, default);

        /// <summary>
        /// Cancel the current send operation or flush operation.
        /// </summary>
        /// <param name="innerException">The inner exception of the <see cref="OperationCanceledException"/> thrown by the <see cref="SendAsync(ReadOnlyMemory{byte}, CancellationToken)"/> method.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> in the <see cref="OperationCanceledException"/> thrown by the <see cref="SendAsync(ReadOnlyMemory{byte}, CancellationToken)"/> method.</param>
        /// <returns>True if the current operation is canceled. False if there is no active send operation.</returns>
        public bool CancelPendingSend(Exception? innerException, CancellationToken cancellationToken)
            => _sendOperation.CancelPendingOperation(innerException, cancellationToken);


        private async Task SendLoopAsync(CancellationTokenSource cts)
        {
            CancellationToken cancellationToken = cts.Token;
            KcpRawSendOperation sendOperation = _sendOperation;
            AsyncAutoResetEvent<int> ev = _sendNotification;
            int mss = _mtu - 4;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesCount = await ev.WaitAsync().ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (bytesCount < 0 || bytesCount > mss)
                    {
                        _ = sendOperation.TryConsume(default, out _);
                        continue;
                    }

                    int packetSize = bytesCount + 4;
                    {
                        using IMemoryOwner<byte> owner = _allocator.Allocate(packetSize);
                        Memory<byte> memory = owner.Memory;
                        BinaryPrimitives.WriteUInt32LittleEndian(memory.Span, _id);
                        if (sendOperation.TryConsume(memory.Slice(4), out int bytesWritten))
                        {
                            packetSize = Math.Min(packetSize, bytesWritten + 4);
                            try
                            {
                                await _transport.SendPacketAsync(memory.Slice(0, packetSize), cancellationToken).ConfigureAwait(false);
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
                }
            }
            catch (OperationCanceledException)
            {
                // Do nothing
            }
            finally
            {
                cts.Dispose();
            }
        }


        private bool HandleFlushException(Exception ex)
        {
            Func<Exception, KcpRawChannel, object?, bool>? handler = _exceptionHandler;
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
        public ValueTask OnReceivedAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
        {
            ReadOnlySpan<byte> span = packet.Span;
            if (span.Length < 4 || span.Length > _mtu)
            {
                return default;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(span) != _id)
            {
                return default;
            }
            _receiveQueue.Enqueue(span.Slice(4));
            return default;
        }

        /// <summary>
        /// Get the size of the next available message in the receive queue.
        /// </summary>
        /// <param name="result">The transport state and the size of the next available message.</param>
        /// <exception cref="InvalidOperationException">The receive or peek operation is initiated concurrently.</exception>
        /// <returns>True if the receive queue contains at least one message. False if the receive queue is empty or the transport is closed.</returns>
        public bool TryPeek(out KcpConversationReceiveResult result)
            => _receiveQueue.TryPeek(out result);

        /// <summary>
        /// Remove the next available message in the receive queue and copy its content into <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">The buffer to receive message.</param>
        /// <param name="result">The transport state and the count of bytes moved into <paramref name="buffer"/>.</param>
        /// <exception cref="ArgumentException">The size of the next available message is larger than the size of <paramref name="buffer"/>.</exception>
        /// <exception cref="InvalidOperationException">The receive or peek operation is initiated concurrently.</exception>
        /// <returns>True if the next available message is moved into <paramref name="buffer"/>. False if the receive queue is empty or the transport is closed.</returns>
        public bool TryReceive(Span<byte> buffer, out KcpConversationReceiveResult result)
            => _receiveQueue.TryReceive(buffer, out result);

        /// <summary>
        /// Wait until the receive queue contains at least one message.
        /// </summary>
        /// <param name="cancellationToken">The token to cancel this operation.</param>
        /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> is fired before receive operation is completed.</exception>
        /// <exception cref="InvalidOperationException">The receive or peek operation is initiated concurrently.</exception>
        /// <returns>A <see cref="ValueTask{KcpConversationReceiveResult}"/> that completes when the receive queue contains at least one full message, or at least one byte in stream mode. Its result contains the transport state and the size of the available message.</returns>
        public ValueTask<KcpConversationReceiveResult> WaitToReceiveAsync(CancellationToken cancellationToken)
            => _receiveQueue.WaitToReceiveAsync(cancellationToken);

        /// <summary>
        /// Wait for the next full message to arrive if the receive queue is empty. Remove the next available message in the receive queue and copy its content into <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">The buffer to receive message.</param>
        /// <param name="cancellationToken">The token to cancel this operation.</param>
        /// <exception cref="ArgumentException">The size of the next available message is larger than the size of <paramref name="buffer"/>.</exception>
        /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> is fired before send operation is completed.</exception>
        /// <exception cref="InvalidOperationException">The receive or peek operation is initiated concurrently.</exception>
        /// <returns>A <see cref="ValueTask{KcpConversationReceiveResult}"/> that completes when a message is moved into <paramref name="buffer"/> or the transport is closed. Its result contains the transport state and the count of bytes written into <paramref name="buffer"/>.</returns>
        public ValueTask<KcpConversationReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _receiveQueue.ReceiveAsync(buffer, cancellationToken);


        /// <summary>
        /// Cancel the current receive operation.
        /// </summary>
        /// <returns>True if the current operation is canceled. False if there is no active send operation.</returns>
        public bool CancelPendingReceive()
            => _receiveQueue.CancelPendingOperation(null, default);

        /// <summary>
        /// Cancel the current send operation or flush operation.
        /// </summary>
        /// <param name="innerException">The inner exception of the <see cref="OperationCanceledException"/> thrown by the <see cref="ReceiveAsync(Memory{byte}, CancellationToken)"/> method or <see cref="WaitToReceiveAsync(CancellationToken)"/> method.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> in the <see cref="OperationCanceledException"/> thrown by the <see cref="ReceiveAsync(Memory{byte}, CancellationToken)"/> method or <see cref="WaitToReceiveAsync(CancellationToken)"/> method.</param>
        /// <returns>True if the current operation is canceled. False if there is no active send operation.</returns>
        public bool CancelPendingReceive(Exception? innerException, CancellationToken cancellationToken)
            => _receiveQueue.CancelPendingOperation(innerException, cancellationToken);


        /// <inheritdoc />
        public void SetTransportClosed()
        {
            try
            {
                Interlocked.Exchange(ref _sendLoopCts, null)?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore
            }

            _receiveQueue.SetTransportClosed();
            _sendOperation.SetTransportClosed();
            _sendNotification.Set(0);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            SetTransportClosed();
            _receiveQueue.Dispose();
            _sendOperation.Dispose();
        }
    }
}
