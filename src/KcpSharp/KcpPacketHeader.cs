using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace KcpSharp
{
    internal readonly struct KcpPacketHeader : IEquatable<KcpPacketHeader>
    {
        public KcpPacketHeader(KcpCommand command, byte fragment, ushort windowSize, uint timestamp, uint serialNumber, uint unacknowledged)
        {
            Command = command;
            Fragment = fragment;
            WindowSize = windowSize;
            Timestamp = timestamp;
            SerialNumber = serialNumber;
            Unacknowledged = unacknowledged;
        }

        internal KcpPacketHeader(byte fragment)
        {
            Command = 0;
            Fragment = fragment;
            WindowSize = 0;
            Timestamp = 0;
            SerialNumber = 0;
            Unacknowledged = 0;
        }

        public KcpCommand Command { get; }
        public byte Fragment { get; }
        public ushort WindowSize { get; }
        public uint Timestamp { get; }
        public uint SerialNumber { get; }
        public uint Unacknowledged { get; }

        public bool Equals(KcpPacketHeader other) => Command == other.Command && Fragment == other.Fragment && WindowSize == other.WindowSize && Timestamp == other.Timestamp && SerialNumber == other.SerialNumber && Unacknowledged == other.Unacknowledged;
        public override bool Equals(object? obj) => obj is KcpPacketHeader other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Command, Fragment, WindowSize, Timestamp, SerialNumber, Unacknowledged);

        public static KcpPacketHeader Parse(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 16)
            {
                ThrowArgumentExceptionBufferTooSmall();
            }

            return new KcpPacketHeader(
                (KcpCommand)buffer[0],
                buffer[1],
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2)),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(4)),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8)),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12))
                );
        }

        internal void EncodeHeader(uint conversationId, int payloadLength, Span<byte> destination)
        {
            Debug.Assert(destination.Length >= 24);
            BinaryPrimitives.WriteUInt32LittleEndian(destination, conversationId);
            destination[4] = (byte)Command;
            destination[5] = Fragment;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6), WindowSize);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8), Timestamp);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12), SerialNumber);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16), Unacknowledged);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(20), payloadLength);
        }

        private static void ThrowArgumentExceptionBufferTooSmall()
        {
            throw new ArgumentException("buffer is too small", "buffer");
        }
    }
}
