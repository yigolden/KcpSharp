using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace KcpEcho
{
    public sealed class UdpSocketServiceDispatcher<T> : IUdpServiceDispatcher, IDisposable where T : class, IUdpService
    {
        private readonly Socket _socket;
        private readonly TimeSpan _keepAliveInterval;
        private readonly TimeSpan _scanInterval;

        private readonly Dictionary<EndPoint, ServiceInfo> _services;
        private readonly ReaderWriterLockSlim _lock;

        private readonly Func<IUdpServiceDispatcher, EndPoint, object?, T?> _activateFunction;
        private readonly Action<T, object?>? _disposeFunction;
        private readonly object? _state;
        private bool _disposed;

        public UdpSocketServiceDispatcher(Socket socket, TimeSpan keepAliveInterval, TimeSpan scanInterval, Func<IUdpServiceDispatcher, EndPoint, object?, T?> activateFunction, Action<T, object?>? disposeFunction, object? state)
        {
            _socket = socket;
            _keepAliveInterval = keepAliveInterval;
            _scanInterval = scanInterval;

            _services = new Dictionary<EndPoint, ServiceInfo>();
            _lock = new ReaderWriterLockSlim();

            _activateFunction = activateFunction;
            _disposeFunction = disposeFunction;
            _state = state;
        }

        public Task RunAsync(EndPoint remoteEndPoint, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            Task scanLoopTask = RunScanLoopAsync(cancellationToken);
            Task receiveLoopTask = RunReceiveLoopAsync(remoteEndPoint, buffer, cancellationToken);
            return Task.WhenAll(scanLoopTask, receiveLoopTask);
        }

        private async Task RunReceiveLoopAsync(EndPoint remoteEndPoint, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && !_disposed)
            {
                SocketReceiveFromResult result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEndPoint, cancellationToken);

                ServiceInfo info = GetServiceInfoOrActivate(result.RemoteEndPoint);
                if (info.IsDefault)
                {
                    continue;
                }

                await info.Service.InputPacketAsync(buffer.Slice(0, result.ReceivedBytes), cancellationToken).ConfigureAwait(false);
            }
        }

        public ValueTask SendPacketAsync(EndPoint endPoint, ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            ServiceInfo serviceInfo = GetServiceInfoUnmutated(endPoint);
            if (serviceInfo.IsDefault)
            {
                return default;
            }
            return new ValueTask(_socket.SendToAsync(packet, SocketFlags.None, endPoint, cancellationToken).AsTask());
        }

        private ServiceInfo GetServiceInfoUnmutated(EndPoint endPoint)
        {
            if (_disposed)
            {
                return default;
            }
            _lock.EnterReadLock();
            try
            {
                if (_services.TryGetValue(endPoint, out ServiceInfo value))
                {
                    return value;
                }
                return default;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private ServiceInfo GetServiceInfoOrActivate(EndPoint endPoint)
        {
            if (_disposed)
            {
                return default;
            }
            _lock.EnterReadLock();
            try
            {
                ref ServiceInfo infoRef = ref CollectionsMarshal.GetValueRefOrNullRef(_services, endPoint);
                if (!Unsafe.IsNullRef(ref infoRef))
                {
                    infoRef.LastActiveDateTimeUtc = DateTime.UtcNow;
                    return infoRef;
                }

                T? service = _activateFunction.Invoke(this, endPoint, _state);
                if (service is null)
                {
                    return default;
                }

                ServiceInfo serviceInfo = new ServiceInfo { Service = service, LastActiveDateTimeUtc = DateTime.UtcNow };
                _services[endPoint] = serviceInfo;
                return serviceInfo;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private async Task RunScanLoopAsync(CancellationToken cancellationToken)
        {
            Dictionary<EndPoint, UdpSocketServiceDispatcher<T>.ServiceInfo> services = _services;

            while (!cancellationToken.IsCancellationRequested && !_disposed)
            {
                List<KeyValuePair<EndPoint, ServiceInfo>>? expiredList = null;

                _lock.EnterWriteLock();
                try
                {
                    DateTime threshold = DateTime.UtcNow - _keepAliveInterval;

                    foreach (KeyValuePair<EndPoint, ServiceInfo> kv in services)
                    {
                        if (kv.Value.LastActiveDateTimeUtc < threshold)
                        {
                            expiredList = expiredList is not null ? expiredList : new List<KeyValuePair<EndPoint, ServiceInfo>>();
                            expiredList.Add(kv);
                        }
                    }

                    if (expiredList is not null)
                    {
                        foreach (KeyValuePair<EndPoint, ServiceInfo> kv in expiredList)
                        {
                            services.Remove(kv.Key);
                        }
                    }
                }
                finally
                {
                    _lock.ExitWriteLock();
                }

                if (expiredList is not null)
                {
                    foreach (KeyValuePair<EndPoint, ServiceInfo> kv in expiredList)
                    {
                        T service = kv.Value.Service;
                        service.SetTransportClosed();
                        if (_disposeFunction is not null)
                        {
                            _disposeFunction.Invoke(service, _state);
                        }
                    }
                }

                await Task.Delay(_scanInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _lock.EnterWriteLock();
            try
            {
                if (_disposeFunction is not null)
                {
                    foreach (KeyValuePair<EndPoint, ServiceInfo> kv in _services)
                    {
                        _disposeFunction.Invoke(kv.Value.Service, _state);
                    }
                }
                _services.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            _lock.Dispose();
            _disposed = true;
        }

        struct ServiceInfo
        {
            public T Service;
            public DateTime LastActiveDateTimeUtc;

            public bool IsDefault => LastActiveDateTimeUtc == default;
        }
    }

    public interface IUdpServiceDispatcher
    {
        ValueTask SendPacketAsync(EndPoint endPoint, ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
    }

    public interface IUdpService
    {
        void SetTransportClosed();
        ValueTask InputPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
    }

}
