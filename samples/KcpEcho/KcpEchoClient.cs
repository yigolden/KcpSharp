using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KcpSharp;

namespace KcpEcho
{
    internal static class KcpEchoClient
    {
        public static async Task RunAsync(string endpoint, int mtu, uint conversationId, CancellationToken cancellationToken)
        {
            if (!IPEndPoint.TryParse(endpoint, out IPEndPoint? ipEndPoint))
            {
                throw new ArgumentException("endpoint is not a valid IPEndPoint.", nameof(endpoint));
            }
            if (mtu < 50 || mtu > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(mtu), "mtu is not valid.");
            }

            var options = new KcpConversationOptions()
            {
                Mtu = mtu
            };

            var socket = new Socket(ipEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            SocketHelper.PatchSocket(socket);
            await socket.ConnectAsync(ipEndPoint, cancellationToken);

            var transport = new SocketKcpTransport(socket, ipEndPoint);
            var conversation = new KcpConversation(transport, (int)conversationId, options);
            transport.StartPumpPacketsToConversation(conversation, options.Mtu, cancellationToken);

            _ = Task.Run(() => ReceiveAndDisplay(conversation, cancellationToken));

            int mss = mtu - 24;
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("Input message: (Press Enter to send)");
                string message = Console.ReadLine() ?? string.Empty;
                int length = Encoding.UTF8.GetByteCount(message);
                if (length > 256 * mss)
                {
                    Console.WriteLine("Error: input is too long.");
                    continue;
                }

                byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    length = Encoding.UTF8.GetBytes(message, buffer);
                    await conversation.SendAsync(buffer.AsMemory(0, length), cancellationToken);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        private static async Task ReceiveAndDisplay(KcpConversation conversation, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                KcpConversationReceiveResult result = await conversation.WaitToReceiveAsync(cancellationToken);
                if (result.TransportClosed)
                {
                    break;
                }

                byte[] buffer = ArrayPool<byte>.Shared.Rent(result.BytesReceived);
                try
                {
                    result = await conversation.ReceiveAsync(buffer, cancellationToken);
                    if (result.TransportClosed)
                    {
                        break;
                    }

                    try
                    {
                        string message = Encoding.UTF8.GetString(buffer.AsSpan(0, result.BytesReceived));
                        Console.WriteLine("Received: " + message);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Error: Failed to decode message.");
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
    }
}
