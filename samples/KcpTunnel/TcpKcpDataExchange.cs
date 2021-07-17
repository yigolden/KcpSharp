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
            bool connectionClosed = false;
            while (true)
            {
                if (!await _conversation.WaitForReceiveQueueAvailableDataAsync(2, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                int length = ReadLength(_conversation, out KcpConversationReceiveResult result);
                if (result.TransportClosed)
                {
                    return;
                }

                if (length == 0)
                {
                    // connection closed signal
                    _socket.Dispose();
                    return;
                }

                byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    Memory<byte> memory = buffer.AsMemory(0, length);

                    // forward data
                    while (!memory.IsEmpty)
                    {
                        result = await _conversation.ReceiveAsync(memory, cancellationToken).ConfigureAwait(false);
                        if (result.TransportClosed)
                        {
                            break;
                        }

                        if (!connectionClosed)
                        {
                            try
                            {
                                await _socket.SendAsync(memory.Slice(0, result.BytesReceived), SocketFlags.None, cancellationToken).ConfigureAwait(false);
                            }
                            catch
                            {
                                connectionClosed = true;
                            }
                        }

                        memory = memory.Slice(result.BytesReceived);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            static ushort ReadLength(KcpConversation conversation, out KcpConversationReceiveResult result)
            {
                Span<byte> buffer = stackalloc byte[2];
                conversation.TryReceive(buffer, out result);
                return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
            }
        }

        private async Task PumpFromTcpToKcp(CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(ushort.MaxValue);
            try
            {
                while (true)
                {
                    // receive data
                    int bytesReceived;
                    try
                    {
                        bytesReceived = await _socket.ReceiveAsync(buffer.AsMemory(0, ushort.MaxValue), SocketFlags.None, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        bytesReceived = 0;
                    }

                    // socket is disposed
                    // send connection close signal
                    if (bytesReceived == 0)
                    {
                        await _conversation.WaitForSendQueueAvailableSpaceAsync(2, 0, cancellationToken).ConfigureAwait(false);
                        if (SendLength(_conversation, 0))
                        {
                            await _conversation.FlushAsync(cancellationToken).ConfigureAwait(false);
                        }
                        return;
                    }

                    // forward to kcp conversation
                    if (!await _conversation.WaitForSendQueueAvailableSpaceAsync(2, 0, cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }
                    if (!SendLength(_conversation, (ushort)bytesReceived))
                    {
                        return;
                    }
                    if (!await _conversation.SendAsync(buffer.AsMemory(0, bytesReceived), cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }
                }

            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            static bool SendLength(KcpConversation conversation, ushort length)
            {
                Span<byte> buffer = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, length);
                return conversation.TrySend(buffer);
            }
        }
    }
}
