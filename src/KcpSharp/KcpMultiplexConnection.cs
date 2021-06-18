using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp
{
    public sealed class KcpMultiplexConnection<T> : IKcpTransport, IDisposable
    {
        private readonly IKcpTransport _transport;

        private readonly ConcurrentDictionary<int, (IKcpConversation Conversation, T? State)> _conversations = new();
        private bool _disposed;

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

        public KcpRawChannel CreateRawChannel(int id, KcpRawChannelOptions? options = null)
        {
            KcpRawChannel? channel = new KcpRawChannel(this, id, options);
            try
            {
                RegisterConversation(channel, default, id);
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

        public KcpRawChannel CreateRawChannel(int id, T state, KcpRawChannelOptions? options = null)
        {
            var channel = new KcpRawChannel(this, id, options);
            try
            {
                RegisterConversation(channel, state, id);
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

        public KcpConversation CreateConversation(int id, KcpConversationOptions? options = null)
        {
            var conversation = new KcpConversation(this, id, options);
            try
            {
                RegisterConversation(conversation, default, id);
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

        public KcpConversation CreateConversation(int id, T state, KcpConversationOptions? options = null)
        {
            var conversation = new KcpConversation(this, id, options);
            try
            {
                RegisterConversation(conversation, state, id);
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

        public void RegisterConversation(IKcpConversation conversation, T? state, int id)
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

        public ValueTask SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return default;
            }
            return _transport.SendPacketAsync(packet, cancellationToken);
        }

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
