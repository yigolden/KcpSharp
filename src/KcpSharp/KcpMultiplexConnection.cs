using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp
{
    /// <summary>
    /// Multiplex many channels or conversations over the same transport.
    /// </summary>
    /// <typeparam name="T">The state of the channel.</typeparam>
    public sealed class KcpMultiplexConnection<T> : IKcpTransport, IDisposable
    {
        private readonly IKcpTransport _transport;

        private readonly ConcurrentDictionary<int, (IKcpConversation Conversation, T? State)> _conversations = new();
        private bool _disposed;

        /// <summary>
        /// Construct a multiplexed connection over a transport.
        /// </summary>
        /// <param name="transport">The underlying transport.</param>
        public KcpMultiplexConnection(IKcpTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        private void CheckDispose()
        {
            if (_disposed)
            {
                ThrowObjectDisposedException();
            }
        }

        private static void ThrowObjectDisposedException()
        {
            throw new ObjectDisposedException(nameof(KcpMultiplexConnection<T>));
        }

        /// <summary>
        /// Process a newly received packet from the transport.
        /// </summary>
        /// <param name="packet">The content of the packet with conversation ID.</param>
        /// <param name="cancellationToken">A token to cancel this operation.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the packet is handled by the corresponding channel or conversation.</returns>
        public ValueTask InputPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            ReadOnlySpan<byte> span = packet.Span;
            if (span.Length < 4)
            {
                return default;
            }
            if (_disposed)
            {
                return default;
            }
            int id = (int)BinaryPrimitives.ReadUInt32LittleEndian(span);
            if (_conversations.TryGetValue(id, out (IKcpConversation Conversation, T? State) value))
            {
                return value.Conversation.OnReceivedAsync(packet, cancellationToken);
            }
            return default;
        }

        /// <summary>
        /// Create a raw channel with the specified conversation ID.
        /// </summary>
        /// <param name="id">The conversation ID.</param>
        /// <param name="options">The options of the <see cref="KcpRawChannel"/>.</param>
        /// <returns>The raw channel created.</returns>
        public KcpRawChannel CreateRawChannel(int id, KcpRawChannelOptions? options = null)
        {
            KcpRawChannel? channel = new KcpRawChannel(this, id, options);
            try
            {
                RegisterConversation(channel, id, default);
                return Interlocked.Exchange<KcpRawChannel?>(ref channel, null)!;
            }
            finally
            {
                if (channel is not null)
                {
                    channel.Dispose();
                }
            }
        }

        /// <summary>
        /// Create a raw channel with the specified conversation ID.
        /// </summary>
        /// <param name="id">The conversation ID.</param>
        /// <param name="state">The user state of this channel.</param>
        /// <param name="options">The options of the <see cref="KcpRawChannel"/>.</param>
        /// <returns>The raw channel created.</returns>
        public KcpRawChannel CreateRawChannel(int id, T state, KcpRawChannelOptions? options = null)
        {
            var channel = new KcpRawChannel(this, id, options);
            try
            {
                RegisterConversation(channel, id, state);
                return Interlocked.Exchange<KcpRawChannel?>(ref channel, null)!;
            }
            finally
            {
                if (channel is not null)
                {
                    channel.Dispose();
                }
            }
        }

        /// <summary>
        /// Create a conversation with the specified conversation ID.
        /// </summary>
        /// <param name="id">The conversation ID.</param>
        /// <param name="options">The options of the <see cref="KcpConversation"/>.</param>
        /// <returns>The KCP conversation created.</returns>
        public KcpConversation CreateConversation(int id, KcpConversationOptions? options = null)
        {
            var conversation = new KcpConversation(this, id, options);
            try
            {
                RegisterConversation(conversation, id, default);
                return Interlocked.Exchange<KcpConversation?>(ref conversation, null)!;
            }
            finally
            {
                if (conversation is not null)
                {
                    conversation.Dispose();
                }
            }
        }

        /// <summary>
        /// Create a conversation with the specified conversation ID.
        /// </summary>
        /// <param name="id">The conversation ID.</param>
        /// <param name="state">The user state of this conversation.</param>
        /// <param name="options">The options of the <see cref="KcpConversation"/>.</param>
        /// <returns>The KCP conversation created.</returns>
        public KcpConversation CreateConversation(int id, T state, KcpConversationOptions? options = null)
        {
            var conversation = new KcpConversation(this, id, options);
            try
            {
                RegisterConversation(conversation, id, state);
                return Interlocked.Exchange<KcpConversation?>(ref conversation, null)!;
            }
            finally
            {
                if (conversation is not null)
                {
                    conversation.Dispose();
                }
            }
        }

        /// <summary>
        /// Register a conversation or channel with the specified conversation ID and user state.
        /// </summary>
        /// <param name="conversation">The conversation or channel to register.</param>
        /// <param name="id">The conversation ID.</param>
        /// <param name="state">The user state</param>
        public void RegisterConversation(IKcpConversation conversation, int id, T? state)
        {
            if (conversation is null)
            {
                throw new ArgumentNullException(nameof(conversation));
            }

            CheckDispose();
            (IKcpConversation addedConversation, T? _) = _conversations.GetOrAdd(id, (conversation, state));
            if (!ReferenceEquals(addedConversation, conversation))
            {
                throw new InvalidOperationException("Duplicated conversation.");
            }
            if (_disposed)
            {
                _conversations.TryRemove(id, out _);
                ThrowObjectDisposedException();
            }
        }

        /// <summary>
        /// Unregister a conversation or channel with the specified conversation ID.
        /// </summary>
        /// <param name="id">The conversation ID.</param>
        /// <returns>The conversation unregistered with the user state. Returns default when the conversation with the specified ID is not found.</returns>
        public (IKcpConversation? Conversation, T? State) UnregisterConversation(int id)
        {
            if (_disposed)
            {
                return default;
            }
            if (_conversations.TryRemove(id, out (IKcpConversation Conversation, T? State) value))
            {
                value.Conversation.SetTransportClosed();
                return value;
            }
            return default;
        }

        /// <inheritdoc />
        public ValueTask SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return default;
            }
            return _transport.SendPacketAsync(packet, cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach ((IKcpConversation conversation, T? _) in _conversations.Values)
            {
                conversation.Dispose();
            }
            _conversations.Clear();
        }
    }
}
