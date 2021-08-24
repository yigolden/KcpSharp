using System.Reflection;
using BenchmarkDotNet.Running;

namespace KcpSimpleForwardErrorCorrection.Benchmarks
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            new BenchmarkSwitcher(typeof(Program).GetTypeInfo().Assembly).Run(args);
        }
    }
}
