using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace KcpSharp.Tests
{
    public class ConversationDisposedTests
    {
        [Fact]
        public async Task TestDispose()
        {
            var blackholeConnection = new Mock<IKcpTransport>();
            blackholeConnection.Setup(conn => conn.SendPacketAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            using var conversation = new KcpConversation(blackholeConnection.Object, 42, null);
            conversation.Dispose();

            KcpConversationReceiveResult result;
            Assert.False(conversation.TryPeek(out result));
            Assert.True(result.TransportClosed);
            Assert.False(conversation.TryReceive(default, out result));
            Assert.True(result.TransportClosed);

            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await conversation.SendAsync(new byte[100], CancellationToken.None));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await conversation.FlushAsync(CancellationToken.None));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await conversation.WaitToReceiveAsync(CancellationToken.None));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await conversation.ReceiveAsync(new byte[100], CancellationToken.None));
        }
    }
}
