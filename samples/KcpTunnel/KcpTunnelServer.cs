using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpTunnel
{
    internal static class KcpTunnelServerProgram
    {
        public static async Task RunAsync(string listen, string tcpForward, int mtu, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(listen) || !IPEndPoint.TryParse(listen, out IPEndPoint? listenEndPoint))
            {
                throw new ArgumentException("listen is not a valid IPEndPoint.", nameof(listen));
            }
            if (string.IsNullOrEmpty(listen) || !IPEndPoint.TryParse(tcpForward, out IPEndPoint? tcpForwardEndPoint))
            {
                throw new ArgumentException("tcpForward is not a valid IPEndPoint.", nameof(tcpForward));
            }
            if (mtu < 64 || mtu > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(mtu));
            }

            var server = new KcpTunnelServer(listenEndPoint, tcpForwardEndPoint, mtu);
            try
            {
                await server.RunAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
        }
    }

    internal class KcpTunnelServer
    {
        private readonly IPEndPoint _listenEndPoint;
        private readonly IPEndPoint _forwardEndPoint;
        private readonly int _mtu;

        public KcpTunnelServer(IPEndPoint listenEndPoint, IPEndPoint forwardEndPoint, int mtu)
        {
            _listenEndPoint = listenEndPoint;
            _forwardEndPoint = forwardEndPoint;
            _mtu = mtu;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using var socket = new Socket(_listenEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            SocketHelper.PatchSocket(socket);
            if (_listenEndPoint.Equals(IPAddress.IPv6Any))
            {
                socket.DualMode = true;
            }
            socket.Bind(_listenEndPoint);

            var factory = new KcpTunnelServiceFactory(new KcpTunnelServiceOptions
            {
                ForwardEndPoint = _forwardEndPoint,
                Mtu = _mtu
            });
            using var dispatcher = UdpSocketServiceDispatcher.Create(socket, factory);
            await dispatcher.RunAsync(_listenEndPoint, GC.AllocateUninitializedArray<byte>(_mtu), cancellationToken);
        }
    }
}
