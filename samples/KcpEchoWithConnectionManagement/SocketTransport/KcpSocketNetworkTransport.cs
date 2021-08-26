using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.SocketTransport
{
    public sealed class KcpSocketNetworkTransport : IKcpNetworkTransport, IDisposable
    {
        private Socket? _socket;
        private CancellationTokenSource? _cts;
        private readonly int _mtu;
        private readonly IKcpBufferPool _bufferPool;

        public KcpSocketNetworkTransport(int mtu, IKcpBufferPool? bufferPool)
        {
            if (mtu < 512 || mtu > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(mtu));
            }
            _mtu = mtu;
            _bufferPool = bufferPool ?? DefaultBufferPool.Instance;
        }

        public void Bind(EndPoint localEndPoint)
        {
            if (_socket is not null)
            {
                ThrowInvalidOperationException();
            }
            _socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            PatchSocket(_socket);
            _socket.Bind(localEndPoint);
        }

        public ValueTask ConnectAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            if (_socket is not null)
            {
                ThrowInvalidOperationException();
            }
            _socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            PatchSocket(_socket);
            return _socket.ConnectAsync(remoteEndPoint, cancellationToken);
        }

        public void Start(IKcpNetworkApplication networkConnection, EndPoint remoteEndPoint)
        {
            if (_cts is not null)
            {
                ThrowInvalidOperationException();
            }
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoop(networkConnection, remoteEndPoint, _cts.Token));
        }

        private async Task ReceiveLoop(IKcpNetworkApplication networkConnection, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            Socket? socket = _socket;
            if (socket is null)
            {
                return;
            }
            try
            {
                using KcpRentedBuffer rentedBuffer = _bufferPool.Rent(new KcpBufferPoolRentOptions(_mtu, true));
                while (!cancellationToken.IsCancellationRequested)
                {
                    SocketReceiveFromResult result = await socket.ReceiveFromAsync(rentedBuffer.Memory, SocketFlags.None, remoteEndPoint, cancellationToken).ConfigureAwait(false);
                    await networkConnection.InputPacketAsync(rentedBuffer.Memory.Slice(0, result.ReceivedBytes), result.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
            finally
            {
                networkConnection.SetTransportClosed();
            }
            // TODO handle other exceptions
        }

        ValueTask IKcpNetworkTransport.SendPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            Socket? socket = _socket;
            if (socket is null)
            {
                return default;
            }
            return new ValueTask(socket.SendToAsync(packet, SocketFlags.None, remoteEndPoint, cancellationToken).AsTask());
        }

        public void Dispose()
        {
            CancellationTokenSource? cts = Interlocked.Exchange(ref _cts, null);
            if (cts is not null)
            {
                cts.Cancel();
                cts.Dispose();
            }
            Socket? socket = Interlocked.Exchange(ref _socket, null);
            if (socket is not null)
            {
                socket.Dispose();
            }
        }

        [DoesNotReturn]
        private static void ThrowInvalidOperationException()
        {
            throw new InvalidOperationException();
        }

        private static void PatchSocket(Socket socket)
        {
            if (OperatingSystem.IsWindows())
            {
                uint IOC_IN = 0x80000000;
                uint IOC_VENDOR = 0x18000000;
                uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                socket.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
            }
        }
    }
}
