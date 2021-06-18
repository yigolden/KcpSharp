using System;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp.Tests
{
    internal static class TestHelper
    {
        public static async Task RunWithTimeout(TimeSpan timeout, Func<CancellationToken, Task> action)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await action(cts.Token);
            }
            catch (OperationCanceledException e)
            {
                if (cts.Token == e.CancellationToken)
                {
                    throw new TimeoutException("Test execution timed out.", e);
                }
                throw;
            }
        }
    }
}
