using System;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using KcpSharp.ThroughputBanchmarks.PacketsThroughputBenchmark;
using KcpSharp.ThroughputBanchmarks.StreamThroughputBenchmark;

namespace KcpSharp.ThroughputBanchmarks
{
    internal static class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine($"Process ID: {Environment.ProcessId}");

            var builder = new CommandLineBuilder();
            builder.Command.Description = "KcpSharp.ThroughputBanchmarks";

            builder.Command.AddCommand(PacketsThroughputBenchmarkProgram.BuildCommand());
            builder.Command.AddCommand(StreamThroughputBenchmarkProgram.BuildCommand());

            builder.UseVersionOption();

            builder.UseHelp();
            builder.UseSuggestDirective();
            builder.RegisterWithDotnetSuggest();
            builder.UseParseErrorReporting();

            Parser parser = builder.Build();
            return await parser.InvokeAsync(args).ConfigureAwait(false);
        }
    }
}
