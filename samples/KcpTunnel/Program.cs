using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
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

            builder.UseVersionOption();

            builder.UseHelp();
            builder.UseSuggestDirective();
            builder.RegisterWithDotnetSuggest();
            builder.UseParseErrorReporting();

            Parser parser = builder.Build();
            return await parser.InvokeAsync(args).ConfigureAwait(false);
        }

        static Command BuildServerCommand()
        {
            var command = new Command("server", "Run server side.");
            command.AddOption(ListenOption());
            command.AddOption(TcpForwardOption());
            command.AddOption(MtuOption());
            command.Handler = CommandHandler.Create<string, string, int, CancellationToken>(KcpTunnelServerProgram.RunAsync);
            return command;

            Option ListenOption() =>
                new Option<string>("--listen", "Endpoint where the server listens.")
                {
                    Arity = ArgumentArity.ExactlyOne
                };

            Option TcpForwardOption() =>
                new Option<string>("--tcp-forward", "The TCP endpoint to forward to.")
                {
                    Arity = ArgumentArity.ExactlyOne
                };

            Option MtuOption() =>
                new Option<int>("--mtu", () => 1400, "MTU.");
        }

        static Command BuildClientCommand()
        {
            var command = new Command("client", "Run client side.");
            command.AddOption(EndpointOption());
            command.AddOption(TcpListenOption());
            command.AddOption(MtuOption());
            command.Handler = CommandHandler.Create<string, string, int, CancellationToken>(KcpTunnelClientProgram.RunAsync);
            return command;

            Option EndpointOption() =>
                new Option<string>("--endpoint", "Endpoint which the client connects to.")
                {
                    Arity = ArgumentArity.ExactlyOne
                };

            Option TcpListenOption() =>
                new Option<string>("--tcp-listen", "The TCP endpoint to listen on.")
                {
                    Arity = ArgumentArity.ExactlyOne
                };

            Option MtuOption() =>
                new Option<int>("--mtu", () => 1400, "MTU.");
        }

    }
}
