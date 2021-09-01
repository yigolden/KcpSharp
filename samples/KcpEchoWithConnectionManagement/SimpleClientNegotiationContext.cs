using KcpEchoWithConnectionManagement.NetworkConnection;

namespace KcpEchoWithConnectionManagement
{
    class SimpleClientNegotiationContext : IKcpConnectionNegotiationContext
    {
        private uint _sessionId;
        private int _step;

        public SimpleClientNegotiationContext()
        {
            _sessionId = (uint)Random.Shared.Next();
        }

        public int? NegotiatedMtu => null;
        public void SetSessionId(uint sessionId)
        {
            _step = -1;
        }
        public bool TryGetSessionId(out uint sessionId)
        {
            sessionId = _sessionId;
            return true;
        }

        public KcpConnectionNegotiationResult GetNegotiationData(Span<byte> data)
        {
            if (_step < 0)
            {
                return KcpConnectionNegotiationResult.Failed;
            }

            switch (_step)
            {
                case 0:
                    data[0] = 1;
                    _step = 1;
                    return new KcpConnectionNegotiationResult(1);
                case 1:
                    return KcpConnectionNegotiationResult.Succeeded;
                default:
                    return KcpConnectionNegotiationResult.Failed;
            }
        }
        public KcpConnectionNegotiationResult PutNegotiationData(ReadOnlySpan<byte> data)
        {
            if (_step < 0)
            {
                return KcpConnectionNegotiationResult.Failed;
            }

            switch (_step)
            {
                case 0:
                    return KcpConnectionNegotiationResult.Failed;
                case 1:
                    if (data.Length != 1 || data[0] != 2)
                    {
                        _step = -1;
                        return KcpConnectionNegotiationResult.Failed;
                    }
                    _step = 1;
                    return KcpConnectionNegotiationResult.Succeeded;
                default:
                    return KcpConnectionNegotiationResult.Failed;
            }
        }
    }
}
