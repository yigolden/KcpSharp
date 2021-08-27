using KcpSharp;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal class KcpNetworkConnectionNegotiationOperationPool
    {
        protected KcpNetworkConnectionNegotiationOperation Allocate()
        {
            return new KcpNetworkConnectionNegotiationOperation(this);
        }

        public virtual KcpNetworkConnectionNegotiationOperation Rent(IKcpBufferPool bufferPool, KcpNetworkConnection networkConnection, IKcpConnectionNegotiationContext negotiationContext)
        {
            KcpNetworkConnectionNegotiationOperation operation = Allocate();
            operation.Initialize(bufferPool, networkConnection, negotiationContext);
            return operation;
        }

        protected internal virtual void Return(KcpNetworkConnectionNegotiationOperation operation) { }
    }
}
