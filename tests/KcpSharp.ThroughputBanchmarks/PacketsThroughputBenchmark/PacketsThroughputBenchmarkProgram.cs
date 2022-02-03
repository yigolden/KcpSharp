using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp.ThroughputBanchmarks.PacketsThroughputBenchmark
{
    internal static class PacketsThroughputBenchmarkProgram
    {
        public static Command BuildCommand()
        {
            var rootCommand = new Command("packets-throughput", "Run packets throughput benchmark.");
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
            var server = new PacketsThroughputBenchmarkServer();
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
            var packetSizeOption = new Option<int>("--packet-size", () => 1376, "Packet size.");
            var windowSizeOption = new Option<int>("--window-size", () => 128, "Window size.");
            var queueSizeOption = new Option<int>("--queue-size", () => 256, "Queue size.");
            var updateIntervalOption = new Option<int>("--update-interval", () => 50, "Update interval.");
            var noDelayOption = new Option<bool>("--no-delay", () => false, "No delay mode.");

            command.AddOption(endpointOption);
            command.AddOption(mtuOption);
            command.AddOption(concurrencyOption);
            command.AddOption(packetSizeOption);
            command.AddOption(windowSizeOption);
            command.AddOption(queueSizeOption);
            command.AddOption(updateIntervalOption);
            command.AddOption(noDelayOption);
            command.SetHandler<string, int, int, int, int, int, int, bool, CancellationToken>(RunClientAsync, endpointOption, mtuOption, concurrencyOption, packetSizeOption, windowSizeOption, queueSizeOption, updateIntervalOption, noDelayOption);

            return command;
        }

        public static Task RunClientAsync(string endpoint, int mtu, int concurrency, int packetSize, int windowSize, int queueSize, int updateInterval, bool noDelay, CancellationToken cancellationToken)
        {
            var client = new PacketsThroughputBenchmarkClient();
            return client.RunAsync(endpoint, mtu, concurrency, packetSize, windowSize, queueSize, updateInterval, noDelay, cancellationToken);
        }
    }
}
