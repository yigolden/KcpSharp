using System.Buffers.Binary;

namespace KcpSharp.Tests.SimpleFec
{
    internal sealed class KcpSimpleFecReceiveBuffer : IDisposable
    {
        private readonly IKcpConversation _conversation;
        private readonly int _rank;
        private readonly uint _mask;
        private readonly int? _conversationId;
        private readonly int _packetSize;
        private readonly IKcpBufferPool _bufferPool;

        private ErrorCorrectionGroup[] _groups;
        private ushort _baseGroupNumber;
        private int _baseIndex;

        public KcpSimpleFecReceiveBuffer(IKcpConversation conversation, int rank, int? conversationId, int packetSize, int windowSize, IKcpBufferPool bufferPool)
        {
            _conversation = conversation;
            _rank = rank;
            _mask = (1u << rank) - 1;
            _conversationId = conversationId;
            _packetSize = packetSize;
            _bufferPool = bufferPool;

            int groupSize = 1 << rank;
            _groups = new ErrorCorrectionGroup[(windowSize + groupSize - 1) / groupSize];
            for (int i = 0; i < _groups.Length; i++)
            {
                _groups[i] = new ErrorCorrectionGroup((ushort)i);
            }
        }

        public ValueTask InputPakcetAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            ReadOnlySpan<byte> contentSpan = packet.Span;
            if (_conversationId.HasValue)
            {
                if (contentSpan.Length < 24)
                {
                    return default;
                }
                if (BinaryPrimitives.ReadInt32LittleEndian(contentSpan) != _conversationId.GetValueOrDefault())
                {
                    return default;
                }

                contentSpan = contentSpan.Slice(4);
            }
            else
            {
                if (contentSpan.Length < 20)
                {
                    return default;
                }
            }

            int commandType = contentSpan[0];

            // data packet
            if (commandType == 81)
            {
                uint serialNumber = BinaryPrimitives.ReadUInt16LittleEndian(contentSpan.Slice(8));
                ushort groupNumber = (ushort)(serialNumber >> _rank);

                int groupOffset = groupNumber - _baseGroupNumber;
                if (groupOffset < 0)
                {
                    // maybe we can drop this packet?
                    return _conversation.InputPakcetAsync(packet, cancellationToken);
                }

                int index = (_baseIndex + groupOffset) % _groups.Length;
                ErrorCorrectionGroup? group = _groups[index];
                if (group.GroupNumber != groupNumber)
                {
                    return _conversation.InputPakcetAsync(packet, cancellationToken);
                }

                uint groupSerial = serialNumber & _mask;
                if (!group.InputDataPacket(_bufferPool, _packetSize, packet.Span, groupSerial))
                {
                    // maybe we can drop this packet?
                    return _conversation.InputPakcetAsync(packet, cancellationToken);
                }

                if (group.Count > _mask)
                {
                    group.SetFullyReceived();
                    if (group.ErrorCorrectionRecived)
                    {
                        return new ValueTask(ReceiveWithErrorCorrection(packet, group, cancellationToken));
                    }
                }
            }

            // error-correction packet
            if (commandType == 85)
            {
                uint serialNumber = BinaryPrimitives.ReadUInt16LittleEndian(contentSpan.Slice(8));
                ushort groupNumber = (ushort)(serialNumber);
                if ((serialNumber >> 16) != (uint)_rank)
                {
                    return default;
                }

                int groupOffset = groupNumber - _baseGroupNumber;
                if (groupOffset < 0)
                {
                    return default;
                }

                int index = (_baseIndex + groupOffset) % _groups.Length;
                ErrorCorrectionGroup? group = _groups[index];
                if (group.GroupNumber != groupNumber)
                {
                    return default;
                }

                if (!group.InputErrorCorrectionPacket(_bufferPool, _packetSize, packet.Span, _conversationId.HasValue))
                {
                    return default;
                }

                if (group.Count > _mask)
                {
                    group.SetFullyReceived();
                    if (group.ErrorCorrectionRecived)
                    {
                        return new ValueTask(ReceiveWithErrorCorrection(packet, group, cancellationToken));
                    }
                }
            }

            return _conversation.InputPakcetAsync(packet, cancellationToken);
        }

        private async Task ReceiveWithErrorCorrection(ReadOnlyMemory<byte> packet, ErrorCorrectionGroup group, CancellationToken cancellationToken)
        {
            await _conversation.InputPakcetAsync(packet, cancellationToken).ConfigureAwait(false);

            packet = group.PrepareForReceiving(_packetSize, _rank, _conversationId);
            try
            {
                await _conversation.InputPakcetAsync(packet, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                group.CompleteReceiving();
            }
        }

        public void Dispose()
        {

        }

        class ErrorCorrectionGroup
        {
            private ushort _groupNumber;
            private KcpRentedBuffer _buffer;
            private bool _allocated;
            private bool _errorCorrectionRecived;
            private bool _isFullyReceived;
            private uint _bitmap;
            private int _count;

            public ushort GroupNumber => _groupNumber;

            public int Count => _count;

            public bool ErrorCorrectionRecived => _errorCorrectionRecived;

            public ErrorCorrectionGroup(ushort groupNumber)
            {
                _groupNumber = groupNumber;
            }

            public void Reset(ushort groupNumber)
            {
                _groupNumber = groupNumber;
                if (_allocated)
                {
                    _buffer.Dispose();
                    _buffer = default;
                    _allocated = false;
                }
                _errorCorrectionRecived = false;
                _isFullyReceived = false;
                _bitmap = 0;
                _count = 0;
            }

            public void SetFullyReceived()
            {
                _isFullyReceived = true;
            }

            public bool InputDataPacket(IKcpBufferPool bufferPool, int packetSize, ReadOnlySpan<byte> packet, uint groupSerial)
            {
                if (_isFullyReceived)
                {
                    return false;
                }

                uint bitMask = 1u << (int)groupSerial;
                if ((_bitmap & bitMask) != 0)
                {
                    return false;
                }
                if (!_allocated)
                {
                    _buffer = bufferPool.Rent(new KcpBufferPoolRentOptions(packetSize, false));
                    _buffer.Memory.Span.Clear();
                    _allocated = true;
                }
                KcpSimpleFecHelper.Xor(_buffer.Memory.Span.Slice(0, packetSize), packet);

                _bitmap = _bitmap | bitMask;
                _count++;
                return true;
            }

            public bool InputErrorCorrectionPacket(IKcpBufferPool bufferPool, int packetSize, ReadOnlySpan<byte> packet, bool hasConversationId)
            {
                if (_isFullyReceived)
                {
                    return false;
                }

                if (_errorCorrectionRecived)
                {
                    return false;
                }
                _errorCorrectionRecived = true;

                if (!_allocated)
                {
                    _buffer = bufferPool.Rent(new KcpBufferPoolRentOptions(packetSize, false));
                    _buffer.Memory.Span.Clear();
                    _allocated = true;
                }

                Span<byte> bufferSpan = _buffer.Memory.Span;

                KcpSimpleFecHelper.Xor(bufferSpan.Slice(0, packetSize), packet);
                _count++;

                int offset = hasConversationId ? 4 : 0;
                packet = packet.Slice(offset);
                bufferSpan = bufferSpan.Slice(offset);

                bufferSpan[0] ^= 85;
                KcpSimpleFecHelper.Xor(bufferSpan.Slice(8, 4), packet.Slice(8, 4));
                return true;
            }

            public ReadOnlyMemory<byte> PrepareForReceiving(int packetSize, int rank, int? conversationId)
            {
                Span<byte> bufferSpan = _buffer.Memory.Span;

                return default;
            }

            public void CompleteReceiving()
            {
                _isFullyReceived = true;
                if (_allocated)
                {
                    _buffer.Dispose();
                    _buffer = default;
                    _allocated = false;
                }
            }
        }
    }
}
