﻿using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp
{
    public sealed class KcpRawChannel : IKcpConversation
    {
        private readonly IKcpBufferAllocator _allocator;
        private readonly IKcpTransport _transport;
        private readonly uint _id;
        private readonly int _mtu;

        private CancellationTokenSource? _sendLoopCts;
        private KcpRawReceiveQueue _receiveQueue;
        private KcpRawSendOperation _sendOperation;
        private AsyncAutoResetEvent<int> _sendNotification;

        private Func<Exception, KcpRawChannel, object?, bool>? _exceptionHandler;
        private object? _exceptionHandlerState;

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

        #region Exception handlers

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
        /// Set the handler to invoke when exception is thrown during flushing packets to the transport. Return true in the handler to ignore the error and continue running. Return false in the handler to abort the operation and mark the transport as closed.
        /// </summary>
        /// <param name="handler">The exception handler.</param>
        public void SetExceptionHandler(Func<Exception, KcpRawChannel, bool> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _exceptionHandler = (ex, conv, state) => ((Func<Exception, KcpRawChannel, bool>?)state)!.Invoke(ex, conv);
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
        public void SetExceptionHandler(Action<Exception, KcpRawChannel, object?> handler, object? state)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _exceptionHandler = (ex, conv, state) =>
            {
                var tuple = (Tuple<Action<Exception, KcpRawChannel, object?>, object?>)state!;
                tuple.Item1.Invoke(ex, conv, tuple.Item2);
                return false;
            };
            _exceptionHandlerState = Tuple.Create(handler, state);
        }

        /// <summary>
        /// Set the handler to invoke when exception is thrown during flushing packets to the transport. The transport will be marked as closed after the exception handler in invoked.
        /// </summary>
        /// <param name="handler">The exception handler.</param>
        public void SetExceptionHandler(Action<Exception, KcpRawChannel> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _exceptionHandler = (ex, conv, state) =>
            {
                var handler = (Action<Exception, KcpRawChannel>)state!;
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
        public bool TransportClosed => _sendLoopCts is null;

        public ValueTask<bool> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            => _sendOperation.SendAsync(buffer, cancellationToken);

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

        ValueTask IKcpConversation.OnReceivedAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
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

        public bool TryPeek(out int packetSize)
            => _receiveQueue.TryPeek(out packetSize);

        public ValueTask<KcpConversationReceiveResult> WaitToReceiveAsync(CancellationToken cancellationToken)
            => _receiveQueue.WaitToReceiveAsync(cancellationToken);

        public ValueTask<KcpConversationReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            => _receiveQueue.ReceiveAsync(buffer, cancellationToken);

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

        public void Dispose()
        {
            SetTransportClosed();
            _receiveQueue.Dispose();
            _sendOperation.Dispose();
        }
    }
}
