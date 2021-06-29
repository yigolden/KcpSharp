using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KcpSharp;

namespace KcpTunnel
{
    internal sealed class TcpKcpDataExchange
    {
        private readonly Socket _socket;
        private readonly KcpConversation _conversation;

        public TcpKcpDataExchange(Socket socket, KcpConversation conversation)
        {
            _socket = socket;
            _conversation = conversation;
        }

        public async Task RunAsync()
        {
            using var cts = new CancellationTokenSource();
            Task kcpToTcp = Task.Run(() => PumpFromKcpToTcp(cts.Token));
            Task tcpToKcp = Task.Run(() => PumpFromTcpToKcp(cts.Token));
            Task finishedTask = await Task.WhenAny(kcpToTcp, tcpToKcp);
            try
            {
                await finishedTask;
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
            catch (ObjectDisposedException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unhandled exception.");
                Console.WriteLine(ex);
            }
            finally
            {
                cts.CancelAfter(TimeSpan.FromSeconds(10));
            }

            Task unfinishedTask = ReferenceEquals(finishedTask, kcpToTcp) ? tcpToKcp : kcpToTcp;
            try
            {
                await unfinishedTask;
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
            catch (ObjectDisposedException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unhandled exception.");
                Console.WriteLine(ex);
            }
        }

        private async Task PumpFromKcpToTcp(CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(ushort.MaxValue + 2);
            try
            {
                bool connectionClosed = false;
                while (true)
                {
                    // read segment length;
                    int bytesReceived = 0;
                    KcpConversationReceiveResult result;
                    while (bytesReceived < 2)
                    {
                        result = await _conversation.ReceiveAsync(buffer.AsMemory(bytesReceived, 2 - bytesReceived), cancellationToken);
                        if (result.TransportClosed)
                        {
                            return;
                        }
                        bytesReceived += result.BytesReceived;
                    }

                    int length = BinaryPrimitives.ReadUInt16LittleEndian(buffer);
                    if (length == 0)
                    {
                        // connection closed signal
                        _socket.Dispose();
                        return;
                    }

                    // forward data
                    Memory<byte> memory = buffer.AsMemory(2, length);
                    while (!memory.IsEmpty)
                    {
                        result = await _conversation.ReceiveAsync(memory, cancellationToken);
                        if (result.TransportClosed)
                        {
                            break;
                        }

                        if (!connectionClosed)
                        {
                            try
                            {
                                await _socket.SendAsync(memory.Slice(0, result.BytesReceived), SocketFlags.None, cancellationToken);
                            }
                            catch
                            {
                                connectionClosed = true;
                            }
                        }

                        memory = memory.Slice(result.BytesReceived);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

        }

        private async Task PumpFromTcpToKcp(CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(ushort.MaxValue + 2);
            try
            {
                while (true)
                {
                    // receive data
                    int bytesReceived;
                    try
                    {
                        bytesReceived = await _socket.ReceiveAsync(buffer.AsMemory(2, ushort.MaxValue), SocketFlags.None, cancellationToken);
                    }
                    catch
                    {
                        bytesReceived = 0;
                    }

                    // socket is disposed
                    // send connection close signal
                    if (bytesReceived == 0)
                    {
                        buffer[1] = 0;
                        buffer[0] = 0;
                        await _conversation.SendAsync(buffer.AsMemory(0, 2), cancellationToken);
                        await _conversation.FlushAsync(cancellationToken);
                        return;
                    }

                    // forward to kcp conversation
                    BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)bytesReceived);
                    if (!await _conversation.SendAsync(buffer.AsMemory(0, 2 + bytesReceived), cancellationToken))
                    {
                        return;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

        }
    }
}
