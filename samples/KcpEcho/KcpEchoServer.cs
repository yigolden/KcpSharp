using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KcpSharp;

namespace KcpEcho
{
    internal static class KcpEchoServer
    {
        public static async Task RunAsync(string listen, int mtu, uint conversationId, CancellationToken cancellationToken)
        {
            if (!IPEndPoint.TryParse(listen, out IPEndPoint? ipEndPoint))
            {
                throw new ArgumentException("endpoint is not a valid IPEndPoint.", nameof(listen));
            }
            if (mtu < 50 || mtu > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(mtu), "mtu is not valid.");
            }

            var options = new KcpConversationOptions
            {
                Mtu = mtu
            };

            var socket = new Socket(ipEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            SocketHelper.PatchSocket(socket);
            if (ipEndPoint.Equals(IPAddress.IPv6Any))
            {
                socket.DualMode = true;
            }
            socket.Bind(ipEndPoint);

            var dispatcher = new UdpSocketServiceDispatcher<KcpEchoService>(
                socket, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
                (sender, ep, state) => new KcpEchoService(sender, ep, ((Tuple<KcpConversationOptions, uint>?)state!).Item1, ((Tuple<KcpConversationOptions, uint>?)state!).Item2),
                (service, state) => service.Dispose(),
                Tuple.Create(options, conversationId));
            await dispatcher.RunAsync(ipEndPoint, GC.AllocateUninitializedArray<byte>(mtu), cancellationToken);
        }
    }
}
