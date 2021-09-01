using KcpEchoWithConnectionManagement.NetworkConnection;

namespace KcpEchoWithConnectionManagement
{
    class SimpleServerNegotiationContext : IKcpConnectionNegotiationContext
    {
        private int _step;
        private uint? _sessionId;

        public int? NegotiatedMtu => null;

        public void SetSessionId(uint sessionId)
        {
            if (_sessionId.HasValue)
            {
                if (sessionId != _sessionId.GetValueOrDefault())
                {
                    _step = -1;
                    return;
                }
            }
            _sessionId = sessionId;
        }
        public bool TryGetSessionId(out uint sessionId)
        {
            sessionId = _sessionId.GetValueOrDefault();
            return _sessionId.HasValue;
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
                    _step = 1;
                    return KcpConnectionNegotiationResult.ContinuationRequired;
                default:
                    return KcpConnectionNegotiationResult.ContinuationRequired;
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
                    _step = 1;

                    if (data.Length != 1 || data[0] != 1)
                    {
                        _step = -1;
                        return KcpConnectionNegotiationResult.Failed;
                    }
                    _step = 1;
                    return KcpConnectionNegotiationResult.ContinuationRequired;
                default:
                    return KcpConnectionNegotiationResult.ContinuationRequired;
            }

        }

    }
}
