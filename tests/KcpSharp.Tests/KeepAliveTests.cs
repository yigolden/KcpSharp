using System;
using System.Threading.Tasks;
using Xunit;

namespace KcpSharp.Tests
{
    public class KeepAliveTests
    {

        [Fact]
        public async Task TestAliveAndThenDead()
        {
            using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(0x12345678, new KcpConversationOptions { KeepAliveOptions = new KcpKeepAliveOptions(500, 3000) });
            await Task.Delay(TimeSpan.FromSeconds(10));
            Assert.False(pipe.Alice.TransportClosed);
            Assert.False(pipe.Bob.TransportClosed);
            pipe.Alice.SetTransportClosed();
            Assert.True(pipe.Alice.TransportClosed);
            Assert.False(pipe.Bob.TransportClosed);
            await Task.Delay(TimeSpan.FromSeconds(5));
            Assert.True(pipe.Alice.TransportClosed);
            Assert.True(pipe.Bob.TransportClosed);
        }

        [Fact]
        public async Task TestDisabled()
        {
            using KcpConversationPipe pipe = KcpConversationFactory.CreatePerfectPipe(0x12345678);
            await Task.Delay(TimeSpan.FromSeconds(5));
            Assert.False(pipe.Alice.TransportClosed);
            Assert.False(pipe.Bob.TransportClosed);
            pipe.Alice.SetTransportClosed();
            Assert.True(pipe.Alice.TransportClosed);
            Assert.False(pipe.Bob.TransportClosed);
            await Task.Delay(TimeSpan.FromSeconds(5));
            Assert.True(pipe.Alice.TransportClosed);
            Assert.False(pipe.Bob.TransportClosed);
        }
    }
}
