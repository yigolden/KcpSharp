﻿using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp.ThroughputBanchmarks.StreamThroughputBenchmark
{
    internal static class StreamThroughputBenchmarkProgram
    {
        public static Command BuildCommand()
        {
            var rootCommand = new Command("stream-throughput", "Run stream throughput benchmark.");
            rootCommand.AddCommand(BuildServerCommand());
            rootCommand.AddCommand(BuildClientCommand());
            return rootCommand;
        }

        private static Command BuildServerCommand()
        {
            var command = new Command("server", "Run server side.");
            var listenOption = new Option<string>("--listen", "Endpoint where the server listens.")
            {
                Arity = ArgumentArity.ExactlyOne
            };
            var mtuOption = new Option<int>("--mtu", () => 1400, "MTU.");
            var windowSizeOption = new Option<int>("--window-size", () => 128, "Window size.");
            var updateIntervalOption = new Option<int>("--update-interval", () => 50, "Update interval.");
            var noDelayOption = new Option<bool>("--no-delay", () => false, "No delay mode.");

            command.AddOption(listenOption);
            command.AddOption(mtuOption);
            command.AddOption(windowSizeOption);
            command.AddOption(updateIntervalOption);
            command.AddOption(noDelayOption);
            command.SetHandler<string, int, int, int, bool, CancellationToken>(RunServerAsync, listenOption, mtuOption, windowSizeOption, updateIntervalOption, noDelayOption);

            return command;
        }

        public static Task RunServerAsync(string listen, int mtu, int windowSize, int updateInterval, bool noDelay, CancellationToken cancellationToken)
        {
            var server = new StreamThroughputBenchmarkServer();
            return server.RunAsync(listen, mtu, windowSize, updateInterval, noDelay, cancellationToken);
        }

        private static Command BuildClientCommand()
        {
            var command = new Command("client", "Run client side.");
            var endpointOption = new Option<string>("--endpoint", "Endpoint which the client connects to.")
            {
                Arity = ArgumentArity.ExactlyOne
            };
            var mtuOption = new Option<int>("--mtu", () => 1400, "MTU.");
            var concurrencyOption = new Option<int>("--concurrency", () => 1, "Concurrency.");
            var bufferSizeOption = new Option<int>("--buffer-size", () => 16384, "Buffer size.");
            var windowSizeOption = new Option<int>("--window-size", () => 128, "Window size.");
            var queueSizeOption = new Option<int>("--queue-size", () => 256, "Queue size.");
            var updateIntervalOption = new Option<int>("--update-interval", () => 50, "Update interval.");
            var noDelayOption = new Option<bool>("--no-delay", () => false, "No delay mode.");

            command.AddOption(endpointOption);
            command.AddOption(mtuOption);
            command.AddOption(concurrencyOption);
            command.AddOption(bufferSizeOption);
            command.AddOption(windowSizeOption);
            command.AddOption(queueSizeOption);
            command.AddOption(updateIntervalOption);
            command.AddOption(noDelayOption);

            command.SetHandler<string, int, int, int, int, int, int, bool, CancellationToken>(RunClientAsync, endpointOption, mtuOption, concurrencyOption, bufferSizeOption, windowSizeOption, queueSizeOption, updateIntervalOption, noDelayOption);

            return command;
        }

        public static Task RunClientAsync(string endpoint, int mtu, int concurrency, int bufferSize, int windowSize, int queueSize, int updateInterval, bool noDelay, CancellationToken cancellationToken)
        {
            var client = new StreamThroughputBenchmarkClient();
            return client.RunAsync(endpoint, mtu, concurrency, bufferSize, windowSize, queueSize, updateInterval, noDelay, cancellationToken);
        }
    }
}
