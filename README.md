# KcpSharp
KCP protocol implemented in C#. (ported from https://github.com/skywind3000/kcp)

KCP is a fast and reliable Automatic Repeat reQuest (ARQ) protocol. It can be used to create a bidirectional reliable data channel over unreliable transport (such as UDP). According to the original author of KCP protocol, the average latency of the KCP protocol is lower than that of TCP at the cost of higher bandwidth usage. For more information, see the [documentation](https://github.com/skywind3000/kcp/wiki/EN_Home) in KCP's official repository.

## Feature
* Implemented completely in C# code. No native dependencies.
* Runs on asynchronous APIs. Suitable for server-side applications.
* Supports both message mode and stream mode.

## Supported Runtimes
* Runtimes compatible with .NET Standard 2.0
* .NET 5
* .NET 6 (recommended)

## Installation

The recommended method to install this package is from NuGet.

```
dotnet add package KcpSharp
```

## Getting Started

### Message mode vs. Stream mode

KCP protocol support two modes: stream mode and message mode (non-stream mode). In the stream mode, the sender sends the data in the form of a stream of bytes and the also the receiver accepts the data in the form of a stream of bytes (much like a TCP stream). In the message mode, The sender packs bytes into messages and then sends these messages to the receiver. KCP protocol ensures that these messages are not lost nor duplicated, and arrive at the receiver in the same order when ther were sent. It is recommended but not required that both sides are in the same mode. KcpSharp uses message mode by default. Users can opt-in to stream mode when creating `KcpConversation` instance.

```csharp
using var conversation = new KcpConversation(transport, conversationId, new KcpConversationOptions { StreamMode = true });

// Or if you are using the built-in UDP socket transport
using var transport = KcpSocketTransport.CreateConversation(socket, endPoint, conversationId, new KcpConversationOptions { StreamMode = true });
```

### Packet format

See [the official documentation](https://github.com/skywind3000/kcp/blob/master/protocol.txt) for more details.

The KCP packet (aka. segment) structure is as following:

```
0               4   5   6       8 (BYTE)
+---------------+---+---+-------+
|     conv      |cmd|frg|  wnd  |
+---------------+---+---+-------+   8
|     ts        |     sn        |
+---------------+---------------+  16
|     una       |     len       |
+---------------+---------------+  24
|                               |
|        DATA (optional)        |
|                               |
+-------------------------------+
```

A KCP conversation represents a reliable data channel over the underlying transport. The conversation ID (`conv`) field is a 4-byte unique identifier which can be used to identify each KCP conversation. You can multiplex multiple conversation over the same transport by using different conversation IDs. The total size of the KCP packet header is 24 bytes.

If both the server side and also the client side uses KcpSharp library, and you don't need the ability to multiplex conversations, You can omit the conversation ID field when creating `KcpConversation` instances. In this case, the `conv` field is excluded from the packet header, and its size becomes 20 bytes.

### Create a KCP conversation on UDP transport

KcpSharp library provides built-in support for UDP socket. The following code shows how to create a single conversation over an UDP socket.

```csharp
public async Task ConnectAndProcessAsync(EndPoint endPoint, CancellationToken cancellationToken)
{
    const int conversationId = 1;
    using var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
    await socket.ConnectAsync(endPoint, cancellationToken);
    using var transport = KcpSocketTransport.CreateConversation(socket, endPoint, conversationId, null);
    transport.Start();
    KcpConversation conversation = transport.Connection;
    // Do something with conversation
}

```

If you want to swap out UDP socket for other custom transport, you can implement it yourself using the instructions at [docs/custom-transport.md](./docs/custom-transport.md).

### Receive messages from conversation

KcpSharp by default works in message mode. The maximum size of a single message is `256 * mss`, where `mss` is `mtu - kcp_overhead`. `mtu` defaults to 1400 and can be configured when creating `KcpConversation` instance. `kcp_overhead` is the size of the KCP packet header, which is 24 or 20, depending on whether `conv` is specified when creating `KcpConversation` instance.

In message mode, the most simple way to receive messages is to call `RecevieAsync` in a loop and process every message received. The following code shows how to do that.
```csharp
public async Task ReceiveLoop(KcpConversation conversation, CancellationToken cancellationToken)
{
    const int mss = 1400 - 24;
    byte[] buffer = new byte[256 * mss];
    while (!cancellationToken.IsCancellationRequested)
    {
        // the size of the buffer passed into ReceiveAsync must be no less than the size of the message.
        KcpConversationReceiveResult result = await conversation.ReceiveAsync(buffer, cancellationToken);
        if (result.TransportClosed)
        {
            break;
        }

        // Your processing logic goes here.
        await ProcessMessageAsync(buffer.AsMemory(0, result.BytesReceived), cancellationToken);
    }
}
```

However in mose cases, messages sent be the remote host can never be this large. The receiving side may also want to avoid buffer allocation before messages are fully received. In this case, the receiving side may use the `WaitToReceiveAsync` method to archive this.

```csharp
public async Task ReceiveLoop(KcpConversation conversation, CancellationToken cancellationToken)
{
    const int MaxMessageSize = 16384; // limit of the maximum message size.
    while (!cancellationToken.IsCancellationRequested)
    {
        // WaitToReceiveAsync call completes when there is at least one message is received or the transport is closed.
        KcpConversationReceiveResult result = await conversation.WaitToReceiveAsync(cancellationToken);
        if (result.TransportClosed)
        {
            break;
        }
        if (result.BytesReceived > MaxMessageSize)
        {
            // The message is too large.
            conversation.SetTransportClosed();
            break;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(result.BytesReceived);
        try
        {
            // TryReceive should not return false here, unless the transport is closed.
            // So we don't need to check for result.TransportClosed.
            if (!conversation.TryReceive(buffer, out result))
            {
                break;
            }

            // Your processing logic goes here.
            await ProcessMessageAsync(buffer.AsMemory(0, result.BytesReceived), cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
```

In stream mode, the buffer passed to `ReceiveAsync` can be any size. `KcpConversation` will try to fill the buffer before `ReceiveAsync` completes. The following code shows how to do this. Besides, you can also use the same code shown above leveraging `WaitToReceiveAsync` to wait for at least any data is available before buffer is allocated.
```csharp
public async Task ReceiveLoop(KcpConversation conversation, CancellationToken cancellationToken)
{
    byte[] buffer = new byte[16384]; // any size is OK.
    while (!cancellationToken.IsCancellationRequested)
    {
        KcpConversationReceiveResult result = await conversation.ReceiveAsync(buffer, cancellationToken);
        if (result.TransportClosed)
        {
            break;
        }

        // Your processing logic goes here.
        await ProcessMessageAsync(buffer.AsMemory(0, result.BytesReceived), cancellationToken);
    }
}
```

The following list shows all the methods which can be used on the receiving side. These methods should not be called concurrently with each other.

* WaitToReceiveAsync
* ReceiveAsync
* TryPeek
* TryReceive

### Send message to conversation

Sending message is pretty straightforward. Simply call `SendAsync` with your message to put the message into the send queue. If you want to make sure all the messages in the send queue have been received and acknowledged by the remote host, simply await on `FlushAsync`.

```csharp
byte[] message = new byte[500];
// Send this message.
bool transportClosed = await conversation.SendAsync(message, cancellationToken);
if (transportClosed)
{
    // Handle this situation.
}

// optionally wait for all the messages to be acknowledged by the receiver
bool transportClosed = await conversation.FlushAsync(cancellationToken);
if (transportClosed)
{
    // Handle this situation.
}
```

The following list shows all the methods which can be used on the sending side. These methods should not be called concurrently with each other.

* SendAsync
* FlushAsync
* TrySend
* TryGetSendQueueAvailableSpace

### Stream adapter

KcpSharp provides a `Stream` adapter that derives from `Stream` and wraps `KcpConversation`. In other word, you can operate on `KcpConversation` like it is a `NetworkStream`. This adapter only works in stream mode.
```csharp
using var conversation = new KcpConversation(transport, new KcpConversationOptions { StreamMode = true });
using Stream stream = new KcpStream(conversation, true);
// You can now use Stream APIs to operation on KcpConversation.
```

### Raw channels

`KcpRawChannel` is a thin wrapper over the overlying transport, but with conversation ID support and a simple receive queue. The class does not provide any reliability guarantee. It simply forwards messages into the underlying transport, or receives from the transport. This class is useful if you want to multiplex both reliable channels and unreliable channels over the same transport.

### Multiplexed conversations

You can create multiple conversations over the same transport as long as they each have unique conversation ID. KcpSharp provides a built-in helper class `KcpMultiplexConnection<T>` and its socket transport for this purpose. (`T` is the type of the state object for each conversation. It is optional if you use the built-in transport.) You can create `IKcpMultiplexConnection` instance over the UDP socket using the following code.
```csharp
const int mtu = 1400;
using IKcpTransport<IKcpMultiplexConnection> transport = KcpSocketTransport.CreateMultiplexConnection(socket, endPoint, mtu);
transport.Start();
IKcpMultiplexConnection connection = transport.Connection;
```

Now that the multiplex connection object is created, you can create `KcpConversation` or `KcpRawChannel` on this connection.
```csharp
// create a reliable KCP conversation.
using KcpConversation conversation = connection.CreateConversation(1, new KcpConversationOptions { Mtu = mtu, StreamMode = true });

// create a unreliable raw channel.
using KcpRawChannel channel = connection.CreateRawChannel(2, new KcpRawChannelOptions { Mtu = mtu });
```

For a detailed sample usage of `IKcpMultiplexConnection`, see `KcpTunnel` sample in [./samples/KcpTunnel](./samples/KcpTunnel).
