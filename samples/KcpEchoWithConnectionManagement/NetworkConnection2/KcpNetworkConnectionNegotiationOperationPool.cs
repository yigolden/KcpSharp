using System.Collections.Concurrent;

namespace KcpEchoWithConnectionManagement.NetworkConnection2
{
    internal class KcpNetworkConnectionNegotiationOperationPool
    {
        protected KcpNetworkConnectionNegotiationOperation Allocate()
        {
            return new KcpNetworkConnectionNegotiationOperation(this);
        }

        public virtual KcpNetworkConnectionNegotiationOperation Rent(KcpNetworkConnection networkConnection, IKcpConnectionNegotiationContext negotiationContext)
        {
            KcpNetworkConnectionNegotiationOperation operation = Allocate();
            operation.Initialize(networkConnection, negotiationContext);
            return operation;
        }

        protected internal virtual void Return(KcpNetworkConnectionNegotiationOperation operation) { }
    }

    internal class KcpNetworkConnectionNegotiationOperationInfinitePool : KcpNetworkConnectionNegotiationOperationPool
    {
        private readonly ConcurrentQueue<KcpNetworkConnectionNegotiationOperation> _queue = new();

        public override KcpNetworkConnectionNegotiationOperation Rent(KcpNetworkConnection networkConnection, IKcpConnectionNegotiationContext negotiationContext)
        {
            if (!_queue.TryDequeue(out KcpNetworkConnectionNegotiationOperation? operation))
            {
                operation = Allocate();
            }
            operation.Initialize(networkConnection, negotiationContext);
            return operation;
        }

        protected internal override void Return(KcpNetworkConnectionNegotiationOperation operation)
        {
            _queue.Enqueue(operation);
        }
    }

}
