using System.Reflection;
using BenchmarkDotNet.Running;

namespace KcpSharp.Benchmarks
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            new BenchmarkSwitcher(typeof(Program).GetTypeInfo().Assembly).Run(args);
        }
    }
}
