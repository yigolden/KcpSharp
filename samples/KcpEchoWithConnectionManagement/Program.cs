// See https://aka.ms/new-console-template for more information
using System.Net;
using KcpEchoWithConnectionManagement.NetworkConnection;

using var listener = KcpNetworkConnectionListener.Listen(new IPEndPoint(IPAddress.Loopback, 6667), new IPEndPoint(IPAddress.Any, 6667), 1024, null);
Task<KcpNetworkConnection> acceptTask = listener.AcceptAsync().AsTask();

KcpNetworkConnection clientConnection = await KcpNetworkConnection.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 6667), 1024, null, default);
clientConnection.SkipNegotiation();
await clientConnection.SendPacketWithPreBufferAsync(new byte[10], default);

KcpNetworkConnection serverConnection = await acceptTask;
serverConnection.SkipNegotiation();

Console.WriteLine("Hello, World!");



