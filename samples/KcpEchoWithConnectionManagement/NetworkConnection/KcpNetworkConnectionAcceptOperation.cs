using System.Threading.Tasks.Sources;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal sealed class KcpNetworkConnectionAcceptOperation : IValueTaskSource<KcpNetworkConnection>
    {
        private ManualResetValueTaskSourceCore<KcpNetworkConnectionListenerConnectionState> _mrvtsc;

        ValueTaskSourceStatus IValueTaskSource<KcpNetworkConnection>.GetStatus(short token) => _mrvtsc.GetStatus(token);
        void IValueTaskSource<KcpNetworkConnection>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _mrvtsc.OnCompleted(continuation, state, token, flags);
        KcpNetworkConnection IValueTaskSource<KcpNetworkConnection>.GetResult(short token)
        {
            try
            {
                return _mrvtsc.GetResult(token).CreateNetworkConnection();
            }
            finally
            {
                _mrvtsc.Reset();

            }
        }

        public KcpNetworkConnectionAcceptOperation()
        {
            _mrvtsc = new ManualResetValueTaskSourceCore<KcpNetworkConnectionListenerConnectionState>
            {
                RunContinuationsAsynchronously = true
            };
        }

        public ValueTask<KcpNetworkConnection> AcceptAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public bool TryQueue(KcpNetworkConnectionListenerConnectionState state)
        {
            throw new NotImplementedException();
        }

    }
}
