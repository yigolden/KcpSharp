using System;
using System.Net;

namespace KcpTunnel
{
    public abstract class UdpSocketDispatcherOptions<T> where T : class, IUdpService
    {
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromMinutes(1);
        public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(2);

        public abstract T Activate(IUdpServiceDispatcher dispatcher, EndPoint endpoint);
        public abstract void Close(T service);
    }
}
