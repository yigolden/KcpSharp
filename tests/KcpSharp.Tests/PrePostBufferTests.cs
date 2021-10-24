using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace KcpSharp.Tests
{
    public class PrePostBufferTests
    {
        [InlineData(500, 0, 500, false)]
        [InlineData(600, 0, 500, false)]
        [InlineData(0, 500, 500, false)]
        [InlineData(0, 600, 500, false)]
        [InlineData(100, 400, 500, false)]
        [InlineData(200, 400, 500, false)]
        [InlineData(400, 100, 500, false)]
        [InlineData(400, 200, 500, false)]
        [InlineData(500, 0, 504, true)]
        [InlineData(600, 0, 504, true)]
        [InlineData(0, 500, 504, true)]
        [InlineData(0, 600, 504, true)]
        [InlineData(100, 400, 504, true)]
        [InlineData(200, 400, 504, true)]
        [InlineData(400, 100, 504, true)]
        [InlineData(400, 200, 504, true)]
        [Theory]
        public void TestExceptionForRawChannel(int preBufferSize, int postBufferSize, int mtu, bool includeId)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var options = new KcpRawChannelOptions
            {
                PreBufferSize = preBufferSize,
                PostBufferSize = postBufferSize,
                Mtu = mtu
            };
            if (includeId)
            {
                Assert.Throws<ArgumentException>("options", () => new KcpRawChannel(blackholeConnection.Object, 0x12345678, options));
            }
            else
            {
                Assert.Throws<ArgumentException>("options", () => new KcpRawChannel(blackholeConnection.Object, options));
            }
        }

        [InlineData(500, 0, 520, false)]
        [InlineData(600, 0, 520, false)]
        [InlineData(0, 500, 520, false)]
        [InlineData(0, 600, 520, false)]
        [InlineData(100, 400, 520, false)]
        [InlineData(200, 400, 520, false)]
        [InlineData(400, 100, 520, false)]
        [InlineData(400, 200, 520, false)]
        [InlineData(500, 0, 524, true)]
        [InlineData(600, 0, 524, true)]
        [InlineData(0, 500, 524, true)]
        [InlineData(0, 600, 524, true)]
        [InlineData(100, 400, 524, true)]
        [InlineData(200, 400, 524, true)]
        [InlineData(400, 100, 524, true)]
        [InlineData(400, 200, 524, true)]
        [Theory]
        public void TestExceptionForConversation(int preBufferSize, int postBufferSize, int mtu, bool includeId)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var options = new KcpConversationOptions
            {
                PreBufferSize = preBufferSize,
                PostBufferSize = postBufferSize,
                Mtu = mtu
            };
            if (includeId)
            {
                Assert.Throws<ArgumentException>("options", () => new KcpConversation(blackholeConnection.Object, 0x12345678, options));
            }
            else
            {
                Assert.Throws<ArgumentException>("options", () => new KcpConversation(blackholeConnection.Object, options));
            }
        }

        [InlineData(100, 0, 500, false)]
        [InlineData(100, 0, 500, true)]
        [InlineData(0, 100, 500, false)]
        [InlineData(0, 100, 500, true)]
        [InlineData(80, 100, 500, false)]
        [InlineData(80, 100, 500, true)]
        [Theory]
        public async Task TestPrePostBufferForRawChannel(int preBufferSize, int postBufferSize, int mtu, bool includeId)
        {
            var options = new KcpRawChannelOptions
            {
                PreBufferSize = preBufferSize,
                PostBufferSize = postBufferSize,
                Mtu = mtu
            };
            const int payloadSize = 10;
            int overhead = preBufferSize + postBufferSize;
            if (includeId)
            {
                overhead += 4;
            }
            byte[] buffer = new byte[payloadSize];
            buffer.AsSpan().Fill(0xff);

            var transport = new ValidationTransport(packet =>
            {
                if (packet.Length != (overhead + payloadSize))
                {
                    return false;
                }
                if (!packet.Slice(0, preBufferSize).SequenceEqual(new byte[preBufferSize]))
                {
                    return false;
                }
                packet = packet.Slice(preBufferSize);
                if (!packet.Slice(packet.Length - postBufferSize).SequenceEqual(new byte[postBufferSize]))
                {
                    return false;
                }
                packet = packet.Slice(0, packet.Length - postBufferSize);
                if (includeId)
                {
                    if (BinaryPrimitives.ReadUInt32LittleEndian(packet) != 0x12345678)
                    {
                        return false;
                    }
                    packet = packet.Slice(4);
                }
                return packet.SequenceEqual(buffer);
            });

            using KcpRawChannel channel = includeId ? new KcpRawChannel(transport, 0x12345678, options) : new KcpRawChannel(transport, options);
            await TestHelper.RunWithTimeout(TimeSpan.FromSeconds(5), async cancellationToken =>
            {
                await channel.SendAsync(buffer, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            });
            Assert.True(transport.ValidationResult);
        }

        [InlineData(100, 0, 500, false)]
        [InlineData(100, 0, 500, true)]
        [InlineData(0, 100, 500, false)]
        [InlineData(0, 100, 500, true)]
        [InlineData(80, 100, 500, false)]
        [InlineData(80, 100, 500, true)]
        [Theory]
        public async Task TestPrePostBufferForConversation(int preBufferSize, int postBufferSize, int mtu, bool includeId)
        {
            var options = new KcpConversationOptions
            {
                PreBufferSize = preBufferSize,
                PostBufferSize = postBufferSize,
                Mtu = mtu
            };
            const int payloadSize = 10;
            int packetHeaderSize = includeId ? 24 : 20;
            int overhead = preBufferSize + postBufferSize + packetHeaderSize;
            byte[] buffer = new byte[payloadSize];
            buffer.AsSpan().Fill(0xff);

            var transport = new ValidationTransport(packet =>
            {
                if (packet.Length < preBufferSize)
                {
                    return false;
                }
                if (!packet.Slice(0, preBufferSize).SequenceEqual(new byte[preBufferSize]))
                {
                    return false;
                }
                packet = packet.Slice(preBufferSize);
                if (packet.Length < postBufferSize || !packet.Slice(packet.Length - postBufferSize).SequenceEqual(new byte[postBufferSize]))
                {
                    return false;
                }
                packet = packet.Slice(0, packet.Length - postBufferSize);
                while (!packet.IsEmpty)
                {
                    if (packet.Length < packetHeaderSize)
                    {
                        return false;
                    }
                    if (includeId)
                    {
                        if (BinaryPrimitives.ReadUInt32LittleEndian(packet) != 0x12345678)
                        {
                            return false;
                        }
                        packet = packet.Slice(4);
                    }
                    int length = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(16));
                    packet = packet.Slice(20);
                    if (length != 0)
                    {
                        if (packet.Length < length)
                        {
                            return false;
                        }
                        if (!packet.Slice(0, length).SequenceEqual(buffer))
                        {
                            return false;
                        }
                        packet = packet.Slice(length);
                    }
                }
                return true;
            });

            using KcpConversation channel = includeId ? new KcpConversation(transport, 0x12345678, options) : new KcpConversation(transport, options);
            await TestHelper.RunWithTimeout(TimeSpan.FromSeconds(5), async cancellationToken =>
            {
                await channel.SendAsync(buffer, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            });
            Assert.True(transport.ValidationResult);
        }

        delegate bool ValidationAction(ReadOnlySpan<byte> packet);

        class ValidationTransport : IKcpTransport
        {
            private readonly ValidationAction _validationFunc;
            private bool? _validationResult;

            public bool? ValidationResult => _validationResult;

            public ValidationTransport(ValidationAction validationFunc)
            {
                _validationFunc = validationFunc;
            }

            public ValueTask SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
            {
                bool result;
                try
                {
                    result = _validationFunc(packet.Span);
                }
                catch
                {
                    result = false;
                }

                if (_validationResult.HasValue)
                {
                    _validationResult = _validationResult.GetValueOrDefault() & result;
                }
                else
                {
                    _validationResult = result;
                }

                return default;
            }
        }
    }
}
