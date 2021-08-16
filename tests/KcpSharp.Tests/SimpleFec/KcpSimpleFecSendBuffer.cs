using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace KcpSharp.Tests.SimpleFec
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

            Span<byte> contentSpan = packet.Span.Slice(_preBufferSize, packet.Length - _preBufferSize - _postBufferSize);
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
            Xor(ecPacket.Span.Slice(_preBufferSize, ecPacket.Length - _postBufferSize), contentSpan);

            _groupBitmap = _groupBitmap | bitMask;
            if ((~_groupBitmap) == 0)
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

                if (_conversationId.HasValue)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(ecPacket.Span.Slice(_preBufferSize - 4), _conversationId.GetValueOrDefault());
                }

                Memory<byte> content = ecPacket.Slice(_preBufferSize);
                content.Span[0] = 85;

                uint sn = ((uint)_rank << 16) + _currentGroup;
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
            _buffer.Memory.Span.Clear();
            _groupBitmap = 0;
        }

        private static void Xor(Span<byte> buffer, ReadOnlySpan<byte> data)
        {
            // slow
            int count = Math.Min(buffer.Length, data.Length);

            if (Vector.IsHardwareAccelerated)
            {
                int vectorSize = Vector<byte>.Count;
                while (count > vectorSize)
                {
                    var v1 = new Vector<byte>(buffer);
                    var v2 = new Vector<byte>(data);
                    v1 = Vector.Xor(v1, v2);
                    v1.CopyTo(buffer);

                    count -= vectorSize;
                    buffer = buffer.Slice(vectorSize);
                    data = data.Slice(vectorSize);
                }
            }
            else
            {
                while (count > 8)
                {
                    long v1 = MemoryMarshal.Read<long>(buffer);
                    long v2 = MemoryMarshal.Read<long>(data);
                    v1 = v1 ^ v2;
                    MemoryMarshal.Write(buffer, ref v1);

                    count -= 8;
                    buffer = buffer.Slice(8);
                    data = data.Slice(8);
                }
            }

            for (int i = 0; i < count; i++)
            {
                buffer[i] = (byte)(buffer[i] ^ data[i]);
            }
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
