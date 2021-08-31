namespace KcpEchoWithConnectionManagement.SocketTransport
{
    public readonly struct KcpSocketNetworkApplicationRegistration : IDisposable
    {
        private readonly KcpSocketNetworkTransport _transport;
        private readonly IDisposable? _disposable;
        private readonly IKcpNetworkApplication? _fallback;

        internal KcpSocketNetworkApplicationRegistration(KcpSocketNetworkTransport transport, IDisposable? disposable)
        {
            _transport = transport;
            _disposable = disposable;
            _fallback = null;
        }

        internal KcpSocketNetworkApplicationRegistration(KcpSocketNetworkTransport transport, IKcpNetworkApplication? fallback)
        {
            _transport = transport;
            _disposable = null;
            _fallback = fallback;
        }

        public void Dispose()
        {
            if (_transport is null)
            {
                return;
            }
            if (_disposable is not null)
            {
                _disposable.Dispose();
            }
            if (_fallback is not null)
            {
                _transport.UnregisterFallback(_fallback);
            }
        }

    }
}
