using System.CommandLine;
using System.CommandLine.Invocation;
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
            command.AddOption(ListenOption());
            command.AddOption(MtuOption());
            command.AddOption(WindowSizeOption());
            command.AddOption(UpdateIntervalOption());
            command.AddOption(NoDelayOption());
            command.Handler = CommandHandler.Create<string, int, int, int, bool, CancellationToken>(RunServerAsync);
            return command;

            Option ListenOption() =>
                new Option<string>("--listen", "Endpoint where the server listens.")
                {
                    Arity = ArgumentArity.ExactlyOne
                };

            Option MtuOption() =>
                new Option<int>("--mtu", () => 1400, "MTU.");

            Option WindowSizeOption() =>
                new Option<int>("--window-size", () => 128, "Window size.");

            Option UpdateIntervalOption() =>
                new Option<int>("--update-interval", () => 50, "Update interval.");

            Option NoDelayOption() =>
                new Option<bool>("--no-delay", () => false, "No delay mode.");

        }

        public static Task RunServerAsync(string listen, int mtu, int windowSize, int updateInterval, bool noDelay, CancellationToken cancellationToken)
        {
            var server = new PacketsThroughputBenchmarkServer();
            return server.RunAsync(listen, mtu, windowSize, updateInterval, noDelay, cancellationToken);
        }

        private static Command BuildClientCommand()
        {
            var command = new Command("client", "Run client side.");
            command.AddOption(ListenOption());
            command.AddOption(MtuOption());
            command.AddOption(ConcurrencyOption());
            command.AddOption(PacketSizeOption());
            command.AddOption(WindowSizeOption());
            command.AddOption(QueueSizeOption());
            command.AddOption(UpdateIntervalOption());
            command.AddOption(NoDelayOption());
            command.Handler = CommandHandler.Create<string, int, int, int, int, int, int, bool, CancellationToken>(RunClientAsync);
            return command;

            Option ListenOption() =>
                new Option<string>("--endpoint", "Endpoint which the client connects to.")
                {
                    Arity = ArgumentArity.ExactlyOne
                };

            Option MtuOption() =>
                new Option<int>("--mtu", () => 1400, "MTU.");

            Option ConcurrencyOption() =>
                new Option<int>("--concurrency", () => 1, "Concurrency.");

            Option PacketSizeOption() =>
                new Option<int>("--packet-size", () => 1376, "Packet size.");

            Option WindowSizeOption() =>
                new Option<int>("--window-size", () => 128, "Window size.");

            Option QueueSizeOption() =>
                new Option<int>("--queue-size", () => 256, "Queue size.");

            Option UpdateIntervalOption() =>
                new Option<int>("--update-interval", () => 50, "Update interval.");

            Option NoDelayOption() =>
                new Option<bool>("--no-delay", () => false, "No delay mode.");

        }

        public static Task RunClientAsync(string endpoint, int mtu, int concurrency, int packetSize, int windowSize, int queueSize, int updateInterval, bool noDelay, CancellationToken cancellationToken)
        {
            var client = new PacketsThroughputBenchmarkClient();
            return client.RunAsync(endpoint, mtu, concurrency, packetSize, windowSize, queueSize, updateInterval, noDelay, cancellationToken);
        }
    }
}
