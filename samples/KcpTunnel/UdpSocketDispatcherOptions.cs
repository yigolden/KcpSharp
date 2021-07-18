using System;
using System.Net;

namespace KcpTunnel
{
    internal abstract class UdpSocketDispatcherOptions<T> where T : class, IUdpService
    {
        public virtual TimeSpan KeepAliveInterval => TimeSpan.FromMinutes(1);
        public virtual TimeSpan ScanInterval => TimeSpan.FromMinutes(2);

        public abstract T Activate(IUdpServiceDispatcher dispatcher, EndPoint endpoint);
        public abstract void Close(T service);
    }
}
