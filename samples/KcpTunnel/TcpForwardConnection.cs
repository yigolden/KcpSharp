using System;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using KcpSharp;

namespace KcpTunnel
{
    internal sealed class TcpForwardConnection : IDisposable
    {
        private readonly IKcpMultiplexConnection<IDisposable> _connection;
        private readonly KcpTunnelServiceOptions _options;
        private readonly KcpConversation _conversation;
        private Socket? _socket;

        public static void Start(IKcpMultiplexConnection<IDisposable> connection, int id, KcpTunnelServiceOptions options)
        {
            var conn = new TcpForwardConnection(connection, id, options);
            _ = Task.Run(() => conn.RunAsync());
        }

        public TcpForwardConnection(IKcpMultiplexConnection<IDisposable> connection, int id, KcpTunnelServiceOptions options)
        {
            _connection = connection;
            _options = options;
            _options = options;
            _conversation = connection.CreateConversation(id, this, new KcpConversationOptions { Mtu = options.Mtu, StreamMode = true });
        }

        public async Task RunAsync()
        {
            Console.WriteLine("Processing conversation: " + _conversation.ConversationId);
            try
            {
                // connect to remote host
                {
                    using var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    KcpConversationReceiveResult result = await _conversation.WaitToReceiveAsync(timeoutToken.Token);
                    Unsafe.SkipInit(out byte b);
                    if (result.TransportClosed)
                    {
                        return;
                    }
                    if (!_conversation.TryReceive(MemoryMarshal.CreateSpan(ref b, 1), out result))
                    {
                        // We don't need to check for result.TransportClosed because there is no way TryReceive can return true when transport is closed.
                        return;
                    }
                    _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        Console.WriteLine("Connecting to forward endpoint." + _conversation.ConversationId);
                        await _socket.ConnectAsync(_options.ForwardEndPoint!, timeoutToken.Token);
                        b = 0;
                        _conversation.TrySend(MemoryMarshal.CreateSpan(ref b, 1));
                    }
                    catch
                    {
                        Console.WriteLine("Connection failed. " + _conversation.ConversationId);
                        using var replyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        b = 1;
                        _conversation.TrySend(MemoryMarshal.CreateSpan(ref b, 1));
                        await _conversation.FlushAsync(replyTimeout.Token);
                        return;
                    }
                }

                // connection is established
                {
                    Console.WriteLine("Tunnel is established. Exchanging data. " + _conversation.ConversationId);
                    var dataExchange = new TcpKcpDataExchange(_socket, _conversation);
                    await dataExchange.RunAsync();
                }
            }
            finally
            {
                _connection.UnregisterConversation(_conversation.ConversationId.GetValueOrDefault())?.Dispose();
                _conversation.Dispose();
                Console.WriteLine("Conversation closed: " + _conversation.ConversationId);
            }

        }

        public void Dispose()
        {
            _socket?.Dispose();
        }
    }
}
