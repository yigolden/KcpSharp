using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.SocketTransport
{
    public sealed class KcpSocketNetworkTransport : IKcpNetworkTransport, IDisposable
    {
        private readonly int _mtu;
        private readonly IKcpBufferPool _bufferPool;

        private Socket? _socket;
        private CancellationTokenSource? _cts;
        private KcpSocketNetworkSendQueue? _sendQueue;

        private readonly ConcurrentDictionary<EndPoint, ApplicationRegistration> _applications = new();
        private bool _disposed;

        private IKcpNetworkApplication? _fallbackApplication;

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
            if (_disposed)
            {
                ThrowObjectDisposedException();
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
            if (_disposed)
            {
                ThrowObjectDisposedException();
            }
            _socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            PatchSocket(_socket);
            return _socket.ConnectAsync(remoteEndPoint, cancellationToken);
        }

        public KcpSocketNetworkApplicationRegistration Register(EndPoint remoteEndPoint, IKcpNetworkApplication application)
        {
            if (remoteEndPoint is null)
            {
                throw new ArgumentNullException(nameof(remoteEndPoint));
            }
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (_disposed)
            {
                ThrowObjectDisposedException();
            }

            var registration = new ApplicationRegistration(this, remoteEndPoint, application);
            if (!_applications.TryAdd(remoteEndPoint, registration))
            {
                ThrowInvalidOperationException();
            }

            if (_disposed)
            {
                _applications.TryRemove(new KeyValuePair<EndPoint, ApplicationRegistration>(remoteEndPoint, registration));
                ThrowObjectDisposedException();
            }

            return new KcpSocketNetworkApplicationRegistration(this, (IDisposable)registration);
        }

        public KcpSocketNetworkApplicationRegistration RegisterFallback(IKcpNetworkApplication application)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (Interlocked.CompareExchange(ref _fallbackApplication, application, null) != null)
            {
                ThrowInvalidOperationException();
            }
            if (_disposed)
            {
                Interlocked.CompareExchange<IKcpNetworkApplication?>(ref _fallbackApplication, null, application);
                ThrowObjectDisposedException();
            }
            return new KcpSocketNetworkApplicationRegistration(this, application);
        }

        private void Unregister(EndPoint remoteEndPoint, ApplicationRegistration application)
        {
            _applications.TryRemove(new KeyValuePair<EndPoint, ApplicationRegistration>(remoteEndPoint, application));
        }

        internal void UnregisterFallback(IKcpNetworkApplication application)
        {
            Interlocked.CompareExchange(ref _fallbackApplication, null, application);
        }

        public void Start(EndPoint remoteEndPoint, int sendQueueCapacity)
        {
            if (_cts is not null || _socket is null)
            {
                ThrowInvalidOperationException();
            }
            _sendQueue = new KcpSocketNetworkSendQueue(_bufferPool, _socket, sendQueueCapacity);
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoop(remoteEndPoint, _cts.Token));
        }

        private IKcpNetworkApplication? LookupApplication(EndPoint remoteEndPoint)
        {
            if (_applications.TryGetValue(remoteEndPoint, out ApplicationRegistration? application))
            {
                return application;
            }
            return _fallbackApplication;
        }

        private void SetTransportClosed()
        {
            ConcurrentDictionary<EndPoint, ApplicationRegistration> applications = _applications;
            while (!applications.IsEmpty)
            {
                foreach (KeyValuePair<EndPoint, ApplicationRegistration> item in applications)
                {
                    if (applications.TryRemove(item))
                    {
                        item.Value.SetTransportClosed();
                    }
                }
            }
            Interlocked.Exchange(ref _fallbackApplication, null)?.SetTransportClosed();
        }

        private async Task ReceiveLoop(EndPoint remoteEndPoint, CancellationToken cancellationToken)
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
                    if (result.ReceivedBytes > _mtu)
                    {
                        continue;
                    }
                    IKcpNetworkApplication? application = LookupApplication(result.RemoteEndPoint);
                    if (application is not null)
                    {
                        await application.InputPacketAsync(rentedBuffer.Memory.Slice(0, result.ReceivedBytes), result.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
            finally
            {
                SetTransportClosed();
            }
            // TODO handle other exceptions
        }

        bool IKcpNetworkTransport.QueuePacket(ReadOnlySpan<byte> packet, EndPoint remoteEndPoint)
        {
            KcpSocketNetworkSendQueue? sendQueue = _sendQueue;
            if (sendQueue is not null)
            {
                sendQueue.Queue(packet, remoteEndPoint);
            }
            return false;
        }

        ValueTask IKcpNetworkTransport.QueueAndSendPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            KcpSocketNetworkSendQueue? sendQueue = _sendQueue;
            if (sendQueue is not null)
            {
                return sendQueue.SendAsync(packet, remoteEndPoint, cancellationToken);
            }
            return default;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            CancellationTokenSource? cts = Interlocked.Exchange(ref _cts, null);
            if (cts is not null)
            {
                cts.Cancel();
                cts.Dispose();
            }
            SetTransportClosed();
            KcpSocketNetworkSendQueue? sendQueue = Interlocked.Exchange(ref _sendQueue, null);
            if (sendQueue is not null)
            {
                sendQueue.Dispose();
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

        [DoesNotReturn]
        private static void ThrowObjectDisposedException()
        {
            throw new ObjectDisposedException(nameof(KcpSocketNetworkTransport));
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

        sealed class ApplicationRegistration : IKcpNetworkApplication, IDisposable
        {
            private KcpSocketNetworkTransport? _transport;
            private readonly EndPoint _remoteEndPoint;
            private readonly IKcpNetworkApplication _application;
            private bool _disposed;

            public ApplicationRegistration(KcpSocketNetworkTransport transport, EndPoint remoteEndPoint, IKcpNetworkApplication application)
            {
                _transport = transport;
                _remoteEndPoint = remoteEndPoint;
                _application = application;
            }

            public ValueTask InputPacketAsync(ReadOnlyMemory<byte> packet, EndPoint remoteEndPoint, CancellationToken cancellationToken)
            {
                if (_transport is null || _disposed)
                {
                    return default;
                }
                return _application.InputPacketAsync(packet, remoteEndPoint, cancellationToken);
            }

            public void SetTransportClosed()
            {
                KcpSocketNetworkTransport? transport = Interlocked.Exchange(ref _transport, null);
                if (transport is not null && !_disposed)
                {
                    _application.SetTransportClosed();
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;

                _transport?.Unregister(_remoteEndPoint, this);
            }
        }
    }
}
