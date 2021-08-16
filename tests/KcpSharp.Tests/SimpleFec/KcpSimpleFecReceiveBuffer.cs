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
        private readonly int _windowSize;
        private readonly IKcpBufferPool _bufferPool;

        private ErrorCorrectionGroup[] _groups;
        private uint _baseSerialNumber;
        private int _baseIndex;

        public KcpSimpleFecReceiveBuffer(IKcpConversation conversation, int rank, int? conversationId, int packetSize, int windowSize, IKcpBufferPool bufferPool)
        {
            _conversation = conversation;
            _rank = rank;
            _mask = (1u << rank) - 1;
            _conversationId = conversationId;
            _packetSize = packetSize;
            _windowSize = windowSize;
            _bufferPool = bufferPool;

            int groupSize = 1 << rank;
            _groups = new ErrorCorrectionGroup[(windowSize + groupSize - 1) / groupSize];
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
            if (commandType != 81 && commandType != 85)
            {
                return _conversation.InputPakcetAsync(packet, cancellationToken);
            }



            // TODO
            return default;


        }

        public void Dispose()
        {

        }

        class ErrorCorrectionGroup
        {
            private uint _baseSerialNumber;
            private KcpRentedBuffer _buffer;
            private bool _allocated;
            private uint _bitmap;
            private int _count;

            public ErrorCorrectionGroup(uint baseSerialNumber)
            {
                _baseSerialNumber = baseSerialNumber;
            }

            public void Reset(uint baseSerialNumber)
            {
                _baseSerialNumber = baseSerialNumber;
                if (_allocated)
                {
                    _buffer.Dispose();
                    _buffer = default;
                    _allocated = false;
                }
                _bitmap = 0;
                _count = 0;
            }
        }

    }
}
