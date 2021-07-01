using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using KcpSharp;

namespace KcpTunnel
{
    internal sealed class TcpSourceConnection : IDisposable
    {
        private readonly IKcpMultiplexConnection<IDisposable> _connection;
        private readonly ConnectionIdPool _idPool;
        private readonly Socket _socket;
        private readonly KcpConversation _conversation;

        public static void Start(IKcpMultiplexConnection<IDisposable> connection, Socket socket, ConnectionIdPool idPool, KcpTunnelClientOptions options)
        {
            var conn = new TcpSourceConnection(connection, socket, idPool, options);
            _ = Task.Run(() => conn.RunAsync());
        }

        public TcpSourceConnection(IKcpMultiplexConnection<IDisposable> connection, Socket socket, ConnectionIdPool idPool, KcpTunnelClientOptions options)
        {
            _connection = connection;
            _idPool = idPool;
            _socket = socket;
            _conversation = connection.CreateConversation(idPool.Rent(), this, new KcpConversationOptions { Mtu = options.Mtu, StreamMode = true });
        }

        public async Task RunAsync()
        {
            Console.WriteLine("Processing conversation: " + _conversation.ConversationId);
            try
            {
                // send connection request
                // and wait for result
                {
                    byte b = 0;
                    using var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    _conversation.TrySend(MemoryMarshal.CreateSpan(ref b, 1));
                    Console.WriteLine("Waiting for server to create tunnel. " + _conversation.ConversationId);
                    KcpConversationReceiveResult result = await _conversation.WaitToReceiveAsync(timeoutToken.Token);
                    if (result.TransportClosed)
                    {
                        return;
                    }
                    if (!_conversation.TryReceive(MemoryMarshal.CreateSpan(ref b, 1), out result))
                    {
                        // We don't need to check for result.TransportClosed because there is no way TryReceive can return true when transport is closed.
                        return;
                    }
                    if (b != 0)
                    {
                        Console.WriteLine("Failed to create tunnel. " + _conversation.ConversationId);
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
            catch (Exception ex)
            {
                Console.WriteLine("Unhandled exception in TcpSourceConnection.");
                Console.WriteLine(ex);
            }
            finally
            {
                _connection.UnregisterConversation(_conversation.ConversationId.GetValueOrDefault())?.Dispose();
                _conversation.Dispose();
                _idPool.Return((ushort)_conversation.ConversationId.GetValueOrDefault());
                Console.WriteLine("Conversation closed: " + _conversation.ConversationId);
            }
        }

        public void Dispose()
        {
            _socket.Dispose();
        }
    }
}
