using System.Buffers.Binary;
using System.Numerics;
using KcpSharp;

namespace KcpSimpleForwardErrorCorrection
{
    internal sealed class KcpSimpleFecSendBuffer : IDisposable
    {
        private readonly IKcpTransport _transport;
        private readonly int _rank;
        private readonly uint _mask;
        private readonly int? _conversationId;
        private readonly int _mtu;
        private readonly int _preBufferSize;
        private readonly int _postBufferSize;

        private readonly object _lock = new object();
        private bool _bufferInUse;
        private bool _disposed;
        private KcpRentedBuffer _buffer;

        private ushort _currentGroup;
        private uint _groupBitmap;

        public KcpSimpleFecSendBuffer(IKcpTransport transport, int rank, int? conversationId, int mtu, int preBufferSize, int postBufferSize, IKcpBufferPool bufferPool)
        {
            if (rank <= 0 || rank > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(rank));
            }
            _transport = transport;

            _rank = rank;
            _mask = (uint)(1 << rank) - 1;
            _conversationId = conversationId;
            _mtu = mtu;
            _preBufferSize = preBufferSize;
            _postBufferSize = postBufferSize;
            _buffer = bufferPool.Rent(new KcpBufferPoolRentOptions(mtu, true));
            ResetGroup(0);
        }

        public ValueTask SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return default;
            }

            int prePacketSize = _preBufferSize;
            if (_conversationId.HasValue)
            {
                prePacketSize += 4;
            }

            Span<byte> contentSpan = packet.Span.Slice(prePacketSize, packet.Length - prePacketSize - _postBufferSize);
            if (contentSpan[0] != 81) // push
            {
                return _transport.SendPacketAsync(packet, cancellationToken);
            }

            uint serialNumber = BinaryPrimitives.ReadUInt32LittleEndian(contentSpan.Slice(8));

            ushort group = (ushort)(serialNumber >> _rank);
            if ((((short)group) - ((short)_currentGroup)) > 0)
            {
                ResetGroup(group);
            }
            else if (group != _currentGroup)
            {
                // resend from previous group
                return _transport.SendPacketAsync(packet, cancellationToken);
            }

            uint serialInGroup = serialNumber & _mask;
            uint bitMask = 1u << (int)serialInGroup;
            if ((_groupBitmap & bitMask) != 0)
            {
                // This is a resend
                return _transport.SendPacketAsync(packet, cancellationToken);
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    return default;
                }
                _bufferInUse = true;
            }

            // update error correction
            Memory<byte> ecPacket = _buffer.Memory.Slice(0, _mtu);
            KcpSimpleFecHelper.Xor(ecPacket.Span.Slice(prePacketSize, _mtu - prePacketSize - _postBufferSize), contentSpan);

            _groupBitmap = _groupBitmap | bitMask;
            if (_groupBitmap == (uint)((1ul << (1 << _rank)) - 1))
            {
                // every packet in this group have been sent
                // send error correction packet.
                return new ValueTask(SendPacketWithErrorCorrection(packet, ecPacket, cancellationToken));
            }
            else
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        _buffer.Dispose();
                    }
                    _bufferInUse = false;
                }
                return _transport.SendPacketAsync(packet, cancellationToken);
            }
        }

        private async Task SendPacketWithErrorCorrection(Memory<byte> packet, Memory<byte> ecPacket, CancellationToken cancellationToken)
        {
            try
            {
                if (_disposed)
                {
                    return;
                }

                await _transport.SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);

                int prePacketSize = _preBufferSize;
                if (_conversationId.HasValue)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(ecPacket.Span.Slice(prePacketSize), _conversationId.GetValueOrDefault());
                    prePacketSize += 4;
                }

                Memory<byte> content = ecPacket.Slice(prePacketSize);
                content.Span[0] = 85;

                uint sn = ((_mask + 1) << 16) + _currentGroup;
                BinaryPrimitives.WriteUInt32LittleEndian(content.Span.Slice(8), sn);

                if (_disposed)
                {
                    return;
                }
                await _transport.SendPacketAsync(ecPacket, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        _buffer.Dispose();
                    }
                    _bufferInUse = false;
                }
            }
        }

        private void ResetGroup(ushort groupNumber)
        {
            _currentGroup = groupNumber;
            _buffer.Span.Clear();
            _groupBitmap = 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            lock (_lock)
            {
                _disposed = true;
                if (!_bufferInUse)
                {
                    _buffer.Dispose();
                }
            }
        }
    }
}
