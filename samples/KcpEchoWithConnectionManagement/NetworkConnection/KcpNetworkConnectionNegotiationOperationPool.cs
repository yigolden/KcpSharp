using KcpSharp;

namespace KcpEchoWithConnectionManagement.NetworkConnection
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
}
