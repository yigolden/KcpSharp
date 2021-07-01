using System;
using System.Buffers.Binary;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KcpSharp;

namespace KcpTunnel
{
    internal sealed class KcpTunnelService : IUdpService, IKcpTransport
    {
        private readonly IUdpServiceDispatcher _sender;
        private readonly EndPoint _endPoint;
        private readonly KcpTunnelServiceOptions _options;
        private readonly KcpMultiplexConnection<IDisposable> _connection;
        private CancellationTokenSource? _cts;

        public EndPoint RemoteEndPoint => _endPoint;

        public KcpTunnelService(IUdpServiceDispatcher sender, EndPoint endPoint, KcpTunnelServiceOptions options)
        {
            _sender = sender;
            _endPoint = endPoint;
            _options = options;
            _connection = new KcpMultiplexConnection<IDisposable>(this, state => state?.Dispose());
        }
        ValueTask IKcpTransport.SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken) => _sender.SendPacketAsync(_endPoint, packet, cancellationToken);
        void IUdpService.SetTransportClosed() => _connection.SetTransportClosed();
        ValueTask IUdpService.InputPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            if (BinaryPrimitives.TryReadInt32LittleEndian(packet.Span, out int id))
            {
                if (id != 0 && (uint)id <= ushort.MaxValue && !_connection.Contains(id))
                {
                    ProcessNewConnection(id);
                }
            }
            return _connection.InputPakcetAsync(packet, cancellationToken);
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => SendHeartbeatLoop(_cts));
            Console.WriteLine($"Multiplex connection created: {_endPoint}");
        }

        public void Stop()
        {
            try
            {
                Interlocked.Exchange(ref _cts, null)?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore
            }
            _connection.Dispose();
            Console.WriteLine($"Multiplex connection eliminated: {_endPoint}");
        }

        private void ProcessNewConnection(int id)
        {
            TcpForwardConnection.Start(_connection, id, _options);
        }

        private async Task SendHeartbeatLoop(CancellationTokenSource cts)
        {
            CancellationToken cancellationToken = cts.Token;
            try
            {
                KcpRawChannel channel = _connection.CreateRawChannel(0, new KcpRawChannelOptions { ReceiveQueueSize = 1 });
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!await channel.SendAsync(default, cancellationToken))
                    {
                        break;
                    }
                    Console.WriteLine("Heartbeat sent. " + _endPoint);
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            finally
            {
                cts.Dispose();
            }
        }
    }
}
