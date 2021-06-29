using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KcpSharp;

namespace KcpTunnel
{
    internal static class KcpTunnelClientProgram
    {
        public static async Task RunAsync(string endpoint, string tcpListen, int mtu, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(endpoint) || !IPEndPoint.TryParse(endpoint, out IPEndPoint? connectEndPoint))
            {
                throw new ArgumentException("endpoint is not a valid IPEndPoint.", nameof(endpoint));
            }
            if (string.IsNullOrEmpty(tcpListen) || !IPEndPoint.TryParse(tcpListen, out IPEndPoint? tcpListenEndPoint))
            {
                throw new ArgumentException("tcpForward is not a valid IPEndPoint.", nameof(tcpListen));
            }
            if (mtu < 64 || mtu > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(mtu));
            }

            var client = new KcpTunnelClient(connectEndPoint, tcpListenEndPoint, new KcpTunnelClientOptions { Mtu = mtu });
            try
            {
                await client.RunAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
        }
    }

    internal class KcpTunnelClient
    {
        private readonly ConnectionIdPool _idPool;
        private readonly IPEndPoint _connectEndPoint;
        private readonly IPEndPoint _listenEndPoint;
        private readonly KcpTunnelClientOptions _options;

        public KcpTunnelClient(IPEndPoint connectEndPoint, IPEndPoint listenEndPoint, KcpTunnelClientOptions options)
        {
            _idPool = new ConnectionIdPool();
            _connectEndPoint = connectEndPoint;
            _listenEndPoint = listenEndPoint;
            _options = options;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using var socket = new Socket(_connectEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            SocketHelper.PatchSocket(socket);
            await socket.ConnectAsync(_connectEndPoint, cancellationToken);
            using IKcpTransport<IKcpMultiplexConnection<IDisposable>> connection = KcpSocketTransport.CreateMultiplexConnection<IDisposable>(socket, _connectEndPoint, _options.Mtu, state => state?.Dispose());
            connection.Start();

            using (cancellationToken.UnsafeRegister(state => ((IDisposable?)state)!.Dispose(), connection))
            {
                _ = Task.Run(() => SendHeartbeatLoop(connection.Connection, cancellationToken));
                await AcceptLoop(connection.Connection, cancellationToken);
            }
        }

        private async Task AcceptLoop(IKcpMultiplexConnection<IDisposable> connection, CancellationToken cancellationToken)
        {
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(_listenEndPoint);
            socket.Listen();
            using (cancellationToken.UnsafeRegister(s => ((Socket?)s)!.Dispose(), socket))
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        Socket acceptSocket = await socket.AcceptAsync();
                        ProcessNewConnection(connection, acceptSocket);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        private void ProcessNewConnection(IKcpMultiplexConnection<IDisposable> connection, Socket socket)
        {
            TcpSourceConnection.Start(connection, socket, _idPool, _options);
        }

        private async Task SendHeartbeatLoop(IKcpMultiplexConnection connection, CancellationToken cancellationToken)
        {
            try
            {
                KcpRawChannel channel = connection.CreateRawChannel(0, new KcpRawChannelOptions { ReceiveQueueSize = 1 });
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!await channel.SendAsync(default, cancellationToken))
                    {
                        break;
                    }
                    Console.WriteLine("Heartbeat sent. " + _connectEndPoint);
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
        }

    }
}
