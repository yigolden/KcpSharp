using System.Numerics;
using System.Runtime.InteropServices;

namespace KcpSharp.Tests.SimpleFec
{
    internal static class KcpSimpleFecHelper
    {
        public static void Xor(Span<byte> buffer, ReadOnlySpan<byte> data)
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
                while (count > 4)
                {
                    uint v1 = MemoryMarshal.Read<uint>(buffer);
                    uint v2 = MemoryMarshal.Read<uint>(data);
                    v1 = v1 ^ v2;
                    MemoryMarshal.Write(buffer, ref v1);

                    count -= 4;
                    buffer = buffer.Slice(4);
                    data = data.Slice(4);
                }
            }

            for (int i = 0; i < count; i++)
            {
                buffer[i] = (byte)(buffer[i] ^ data[i]);
            }
        }

    }
}
