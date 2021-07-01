using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace KcpSharp.Tests
{
    public class CancelPendingOperationsTests
    {
        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestCancelPendingSend(bool useFullParameters)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            using var cts = new CancellationTokenSource();
            Exception? innerException = null;
            CancellationToken cancellationToken = default;
            if (useFullParameters)
            {
                innerException = new InvalidDataException();
                cancellationToken = cts.Token;
            }

            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { Mtu = 100, SendWindow = 2, SendQueueSize = 2 });
            Task sendTask = conversation.SendAsync(new byte[500], CancellationToken.None).AsTask();
            if (useFullParameters)
            {
                conversation.CancelPendingSend(innerException, cancellationToken);
            }
            else
            {
                conversation.CancelPendingSend();
            }
            OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(() => sendTask);

            Assert.True(ReferenceEquals(innerException, exception.InnerException));
            Assert.True(cancellationToken.Equals(exception.CancellationToken));
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestCancelPendingFlush(bool useFullParameters)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            using var cts = new CancellationTokenSource();
            Exception? innerException = null;
            CancellationToken cancellationToken = default;
            if (useFullParameters)
            {
                innerException = new InvalidDataException();
                cancellationToken = cts.Token;
            }

            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { Mtu = 100, SendWindow = 2, SendQueueSize = 2 });
            Task sendTask = conversation.SendAsync(new byte[50], CancellationToken.None).AsTask();
            Assert.True(sendTask.IsCompletedSuccessfully);
            await sendTask;
            Task flushTask = conversation.FlushAsync(CancellationToken.None).AsTask();
            if (useFullParameters)
            {
                conversation.CancelPendingSend(innerException, cancellationToken);
            }
            else
            {
                conversation.CancelPendingSend();
            }
            OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(() => flushTask);

            Assert.True(ReferenceEquals(innerException, exception.InnerException));
            Assert.True(cancellationToken.Equals(exception.CancellationToken));
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestCancelPendingReceive(bool useFullParameters)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            using var cts = new CancellationTokenSource();
            Exception? innerException = null;
            CancellationToken cancellationToken = default;
            if (useFullParameters)
            {
                innerException = new InvalidDataException();
                cancellationToken = cts.Token;
            }

            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { Mtu = 100 });
            Task receiveTask = conversation.ReceiveAsync(new byte[500], CancellationToken.None).AsTask();
            if (useFullParameters)
            {
                conversation.CancelPendingReceive(innerException, cancellationToken);
            }
            else
            {
                conversation.CancelPendingReceive();
            }
            OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(() => receiveTask);

            Assert.True(ReferenceEquals(innerException, exception.InnerException));
            Assert.True(cancellationToken.Equals(exception.CancellationToken));
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestCancelPendingWaitToReceive(bool useFullParameters)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            using var cts = new CancellationTokenSource();
            Exception? innerException = null;
            CancellationToken cancellationToken = default;
            if (useFullParameters)
            {
                innerException = new InvalidDataException();
                cancellationToken = cts.Token;
            }

            var conversation = new KcpConversation(blackholeConnection.Object, 0, new KcpConversationOptions { Mtu = 100 });
            Task waitTask = conversation.WaitToReceiveAsync(CancellationToken.None).AsTask();
            if (useFullParameters)
            {
                conversation.CancelPendingReceive(innerException, cancellationToken);
            }
            else
            {
                conversation.CancelPendingReceive();
            }
            OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(() => waitTask);

            Assert.True(ReferenceEquals(innerException, exception.InnerException));
            Assert.True(cancellationToken.Equals(exception.CancellationToken));
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestCancelPendingSendForRawChannel(bool useFullParameters)
        {
            var tcs = new TaskCompletionSource();

            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(tcs.Task));

            using var cts = new CancellationTokenSource();
            Exception? innerException = null;
            CancellationToken cancellationToken = default;
            if (useFullParameters)
            {
                innerException = new InvalidDataException();
                cancellationToken = cts.Token;
            }

            var conversation = new KcpRawChannel(blackholeConnection.Object, 0, new KcpRawChannelOptions { Mtu = 100 });
            Task sendTask = conversation.SendAsync(new byte[50], CancellationToken.None).AsTask();
            await Task.Delay(500);
            Assert.True(sendTask.IsCompletedSuccessfully);
            await sendTask;
            sendTask = conversation.SendAsync(new byte[50], CancellationToken.None).AsTask();
            if (useFullParameters)
            {
                conversation.CancelPendingSend(innerException, cancellationToken);
            }
            else
            {
                conversation.CancelPendingSend();
            }
            OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(() => sendTask);

            Assert.True(ReferenceEquals(innerException, exception.InnerException));
            Assert.True(cancellationToken.Equals(exception.CancellationToken));

            tcs.TrySetResult();
        }


        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestCancelPendingReceiveForRawChannel(bool useFullParameters)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            using var cts = new CancellationTokenSource();
            Exception? innerException = null;
            CancellationToken cancellationToken = default;
            if (useFullParameters)
            {
                innerException = new InvalidDataException();
                cancellationToken = cts.Token;
            }

            var conversation = new KcpRawChannel(blackholeConnection.Object, 0, new KcpRawChannelOptions { Mtu = 100 });
            Task receiveTask = conversation.ReceiveAsync(new byte[500], CancellationToken.None).AsTask();
            if (useFullParameters)
            {
                conversation.CancelPendingReceive(innerException, cancellationToken);
            }
            else
            {
                conversation.CancelPendingReceive();
            }
            OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(() => receiveTask);

            Assert.True(ReferenceEquals(innerException, exception.InnerException));
            Assert.True(cancellationToken.Equals(exception.CancellationToken));
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public async Task TestCancelPendingWaitToReceiveForRawChannel(bool useFullParameters)
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            using var cts = new CancellationTokenSource();
            Exception? innerException = null;
            CancellationToken cancellationToken = default;
            if (useFullParameters)
            {
                innerException = new InvalidDataException();
                cancellationToken = cts.Token;
            }

            var conversation = new KcpRawChannel(blackholeConnection.Object, 0, new KcpRawChannelOptions { Mtu = 100 });
            Task waitTask = conversation.WaitToReceiveAsync(CancellationToken.None).AsTask();
            if (useFullParameters)
            {
                conversation.CancelPendingReceive(innerException, cancellationToken);
            }
            else
            {
                conversation.CancelPendingReceive();
            }
            OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(() => waitTask);

            Assert.True(ReferenceEquals(innerException, exception.InnerException));
            Assert.True(cancellationToken.Equals(exception.CancellationToken));
        }

    }
}
