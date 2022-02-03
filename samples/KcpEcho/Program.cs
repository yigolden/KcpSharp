using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Threading;
using System.Threading.Tasks;

namespace KcpEcho
{
    internal static class Program
    {
        static async Task<int> Main(string[] args)
        {
            var builder = new CommandLineBuilder();
            builder.Command.Description = "KcpEcho";

            builder.Command.AddCommand(BuildServerCommand());
            builder.Command.AddCommand(BuildClientCommand());

            builder.UseDefaults();

            Parser parser = builder.Build();
            return await parser.InvokeAsync(args).ConfigureAwait(false);
        }

        static Command BuildServerCommand()
        {
            var command = new Command("server", "Run server side.");
            var listenOptions = new Option<string>("--listen", "Endpoint where the server listens.")
            {
                Arity = ArgumentArity.ExactlyOne
            };
            var mtuOption = new Option<int>("--mtu", () => 1400, "MTU.");
            var conversationOption = new Option<uint>("--conversation-id", () => 0, "Conversation ID.");

            command.AddOption(listenOptions);
            command.AddOption(mtuOption);
            command.AddOption(conversationOption);
            command.SetHandler<string, int, uint, CancellationToken>(KcpEchoServer.RunAsync, listenOptions, mtuOption, conversationOption);
            return command;
        }

        static Command BuildClientCommand()
        {
            var command = new Command("client", "Run client side.");
            var endpointOption = new Option<string>("--endpoint", "Endpoint which the client connects to.")
            {
                Arity = ArgumentArity.ExactlyOne
            };
            var mtuOption = new Option<int>("--mtu", () => 1400, "MTU.");
            var conversationOption = new Option<uint>("--conversation-id", () => 0, "Conversation ID.");

            command.AddOption(endpointOption);
            command.AddOption(mtuOption);
            command.AddOption(conversationOption);
            command.SetHandler<string, int, uint, CancellationToken>(KcpEchoClient.RunAsync, endpointOption, mtuOption, conversationOption);
            return command;

        }
    }

}
