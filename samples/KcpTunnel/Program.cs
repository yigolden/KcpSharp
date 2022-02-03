using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading;
using System.Threading.Tasks;

namespace KcpTunnel
{
    internal static class Program
    {
        static async Task<int> Main(string[] args)
        {
            var builder = new CommandLineBuilder();
            builder.Command.Description = "KcpTunnel";

            builder.Command.AddCommand(BuildServerCommand());
            builder.Command.AddCommand(BuildClientCommand());

            builder.UseDefaults();

            Parser parser = builder.Build();
            return await parser.InvokeAsync(args).ConfigureAwait(false);
        }

        static Command BuildServerCommand()
        {
            var command = new Command("server", "Run server side.");
            var listenOption = new Option<string>("--listen", "Endpoint where the server listens.")
            {
                Arity = ArgumentArity.ExactlyOne
            };
            var tcpForwardOption = new Option<string>("--tcp-forward", "The TCP endpoint to forward to.")
            {
                Arity = ArgumentArity.ExactlyOne
            };
            var mtuOption = new Option<int>("--mtu", () => 1400, "MTU.");

            command.AddOption(listenOption);
            command.AddOption(tcpForwardOption);
            command.AddOption(mtuOption);
            command.SetHandler<string, string, int, CancellationToken>(KcpTunnelServerProgram.RunAsync, listenOption, tcpForwardOption, mtuOption);

            return command;
        }

        static Command BuildClientCommand()
        {
            var command = new Command("client", "Run client side.");
            var endpointOption = new Option<string>("--endpoint", "Endpoint which the client connects to.")
            {
                Arity = ArgumentArity.ExactlyOne
            };
            var tcpListenOption = new Option<string>("--tcp-listen", "The TCP endpoint to listen on.")
            {
                Arity = ArgumentArity.ExactlyOne
            };
            var mtuOption = new Option<int>("--mtu", () => 1400, "MTU.");

            command.AddOption(endpointOption);
            command.AddOption(tcpListenOption);
            command.AddOption(mtuOption);
            command.SetHandler<string, string, int, CancellationToken>(KcpTunnelClientProgram.RunAsync, endpointOption, tcpListenOption, mtuOption);

            return command;
        }

    }
}
