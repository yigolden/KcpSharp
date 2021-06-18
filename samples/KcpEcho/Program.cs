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
            command.AddOption(MtuOption());
            command.AddOption(ConversationOption());
            command.Handler = CommandHandler.Create<string, int, uint, CancellationToken>(KcpEchoServer.RunAsync);
            return command;

            Option ListenOption() =>
                new Option<string>("--listen", "Endpoint where the server listens.", ArgumentArity.ExactlyOne);

            Option MtuOption() =>
                new Option<int>("--mtu", () => 1400, "MTU. [1400]");

            Option ConversationOption() =>
                new Option<uint>("--conversation-id", () => 0, "Conversation ID. [0]");
        }

        static Command BuildClientCommand()
        {
            var command = new Command("client", "Run client side.");
            command.AddOption(ListenOption());
            command.AddOption(MtuOption());
            command.AddOption(ConversationOption());
            command.Handler = CommandHandler.Create<string, int, uint, CancellationToken>(KcpEchoClient.RunAsync);
            return command;

            Option ListenOption() =>
                new Option<string>("--endpoint", "Endpoint which the client connects to.", ArgumentArity.ExactlyOne);

            Option MtuOption() =>
                new Option<int>("--mtu", () => 1400, "MTU. [1400]");

            Option ConversationOption() =>
                new Option<uint>("--conversation-id", () => 0, "Conversation ID. [0]");

        }
    }

}
