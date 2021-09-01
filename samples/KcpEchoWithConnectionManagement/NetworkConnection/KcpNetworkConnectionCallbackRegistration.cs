namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    public readonly struct KcpNetworkConnectionCallbackRegistration : IDisposable
    {
        private readonly IDisposable _disposable;

        internal KcpNetworkConnectionCallbackRegistration(IDisposable disposable)
        {
            _disposable = disposable;
        }

        public void Dispose()
        {
            _disposable?.Dispose();
        }
    }
}
