using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpTunnel
{
    internal static class UdpSocketServiceDispatcher
    {
        public static UdpSocketServiceDispatcher<T> Create<T>(Socket socket, UdpSocketDispatcherOptions<T> options) where T : class, IUdpService
        {
            return new UdpSocketServiceDispatcher<T>(socket, options);
        }
    }

    internal sealed class UdpSocketServiceDispatcher<T> : IUdpServiceDispatcher, IDisposable where T : class, IUdpService
    {
        private readonly Socket _socket;
        private readonly TimeSpan _keepAliveInterval;
        private readonly TimeSpan _scanInterval;
        private readonly UdpSocketDispatcherOptions<T> _options;

        private readonly ConcurrentDictionary<EndPoint, ServiceInfo> _services;

        private bool _disposed;

        public UdpSocketServiceDispatcher(Socket socket, UdpSocketDispatcherOptions<T> options)
        {
            _socket = socket;
            _keepAliveInterval = options.KeepAliveInterval;
            _scanInterval = options.ScanInterval;
            _options = options;

            _services = new ConcurrentDictionary<EndPoint, ServiceInfo>();
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
                SocketReceiveFromResult result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEndPoint, cancellationToken).ConfigureAwait(false);

                ServiceInfo? info = GetServiceInfoOrActivate(result.RemoteEndPoint);
                if (info is null)
                {
                    continue;
                }

                await info.Service.InputPacketAsync(buffer.Slice(0, result.ReceivedBytes), cancellationToken).ConfigureAwait(false);
            }
        }

        public ValueTask SendPacketAsync(EndPoint endPoint, ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            ServiceInfo? serviceInfo = GetServiceInfoUnmutated(endPoint);
            if (serviceInfo is null)
            {
                return default;
            }
            return new ValueTask(_socket.SendToAsync(packet, SocketFlags.None, endPoint, cancellationToken).AsTask());
        }

        private ServiceInfo? GetServiceInfoUnmutated(EndPoint endPoint)
        {
            if (_disposed)
            {
                return null;
            }
            if (_services.TryGetValue(endPoint, out ServiceInfo? value))
            {
                return value;
            }
            return null;
        }

        private ServiceInfo? GetServiceInfoOrActivate(EndPoint endPoint)
        {
            if (_disposed)
            {
                return null;
            }
            if (_services.TryGetValue(endPoint, out ServiceInfo? value))
            {
                value.UpdateActiveTime(DateTime.UtcNow);
                return value;
            }

            T? service = _options.Activate(this, endPoint);
            if (service is null)
            {
                return null;
            }
            var serviceInfo = new ServiceInfo(service);

            ServiceInfo? addedServiceInfo = _services.AddOrUpdate(endPoint, (_, s) => s, (_, s, _) => s, serviceInfo);
            if (!ReferenceEquals(serviceInfo, addedServiceInfo))
            {
                _options.Close(service);
                serviceInfo = addedServiceInfo;
            }
            if (_disposed && _services.TryRemove(endPoint, out addedServiceInfo))
            {
                _options.Close(service);
                return null;
            }

            return serviceInfo;
        }

        public void RemoveService(EndPoint endPoint, object? service = null)
        {
            if (_disposed)
            {
                return;
            }
            ServiceInfo? serviceInfo;
            if (service is null)
            {
                if (_services.TryRemove(endPoint, out serviceInfo))
                {
                    serviceInfo.Service.SetTransportClosed();
                    _options.Close(serviceInfo.Service);
                }
                return;
            }
            if (_services.TryGetValue(endPoint, out serviceInfo))
            {
                if (ReferenceEquals(serviceInfo.Service, service))
                {
                    if (_services.TryRemove(new KeyValuePair<EndPoint, ServiceInfo>(endPoint, serviceInfo)))
                    {
                        serviceInfo.Service.SetTransportClosed();
                        _options.Close(serviceInfo.Service);
                    }
                }
            }
        }

        private async Task RunScanLoopAsync(CancellationToken cancellationToken)
        {
            ConcurrentDictionary<EndPoint, ServiceInfo> services = _services;

            while (!cancellationToken.IsCancellationRequested && !_disposed)
            {
                DateTime threshold = DateTime.UtcNow - _keepAliveInterval;

                foreach (KeyValuePair<EndPoint, ServiceInfo> kv in services)
                {
                    if (kv.Value.IsLastActiveDateTimeLessThan(threshold) || !kv.Value.Service.ValidateAliveness())
                    {
                        if (services.TryRemove(kv))
                        {
                            T service = kv.Value.Service;
                            service.SetTransportClosed();
                            _options.Close(service);
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
            _disposed = true;

            ConcurrentDictionary<EndPoint, ServiceInfo> services = _services;
            UdpSocketDispatcherOptions<T> options = _options;
            while (true)
            {
                bool anyRemoved = false;
                foreach (KeyValuePair<EndPoint, ServiceInfo> kv in services)
                {
                    if (services.TryRemove(kv))
                    {
                        T service = kv.Value.Service;
                        service.SetTransportClosed();
                        options.Close(service);
                        anyRemoved = true;
                    }
                }

                if (!anyRemoved)
                {
                    break;
                }
            }
        }

        class ServiceInfo
        {
            private SpinLock _lock;
            public T Service { get; }
            public DateTime LastActiveDateTimeUtc { get; private set; }

            public ServiceInfo(T service)
            {
                Service = service;
            }

            public void UpdateActiveTime(DateTime utcNow)
            {
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    LastActiveDateTimeUtc = utcNow;
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
                }
            }

            public bool IsLastActiveDateTimeLessThan(DateTime utcTime)
            {
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    return LastActiveDateTimeUtc < utcTime;
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
                }
            }

        }
    }

    public interface IUdpServiceDispatcher
    {
        ValueTask SendPacketAsync(EndPoint endPoint, ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
        void RemoveService(EndPoint endPoint, object? service = null);
    }

    public interface IUdpService
    {
        bool ValidateAliveness() => true;
        void SetTransportClosed();
        ValueTask InputPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
    }


}
