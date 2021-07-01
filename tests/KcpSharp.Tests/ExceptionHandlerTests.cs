using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KcpSharp.Tests
{
    public class ExceptionHandlerTests
    {
        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TestExceptionHandlerWithThreeArgumentAndReturn(bool continueExecution)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var exception = new InvalidDataException();
                int exceptionThrownCount = 0;
                Func<Exception> exceptionFunc = () =>
                {
                    exceptionThrownCount++;
                    return exception;
                };

                int handlerInvokedCount = 0;
                Exception? exceptionThrown = null;
                object obj = new();

                using var conversation = new KcpConversation(new ThrowingTransport(exceptionFunc, 250), 0, null);
                conversation.SetExceptionHandler((ex, conv, state) =>
                {
                    handlerInvokedCount++;
                    exceptionThrown = ex;
                    Assert.True(ReferenceEquals(exception, ex));
                    Assert.True(ReferenceEquals(conversation, conv));
                    Assert.True(ReferenceEquals(obj, state));
                    return continueExecution;
                }, obj);

                _ = conversation.SendAsync(default, cancellationToken).AsTask();
                await Task.Delay(1000, cancellationToken);

                Assert.True(ReferenceEquals(exception, exceptionThrown));

                if (continueExecution)
                {
                    Assert.True(exceptionThrownCount > 1);
                    Assert.Equal(exceptionThrownCount, handlerInvokedCount);
                    Assert.False(conversation.TransportClosed);
                }
                else
                {
                    Assert.Equal(1, exceptionThrownCount);
                    Assert.Equal(exceptionThrownCount, handlerInvokedCount);
                    Assert.True(conversation.TransportClosed);
                }
            });
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TestExceptionHandlerWithTwoArgumentAndReturn(bool continueExecution)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var exception = new InvalidDataException();
                int exceptionThrownCount = 0;
                Func<Exception> exceptionFunc = () =>
                {
                    exceptionThrownCount++;
                    return exception;
                };

                int handlerInvokedCount = 0;
                Exception? exceptionThrown = null;

                using var conversation = new KcpConversation(new ThrowingTransport(exceptionFunc, 250), 0, null);
                conversation.SetExceptionHandler((ex, conv) =>
                {
                    handlerInvokedCount++;
                    exceptionThrown = ex;
                    Assert.True(ReferenceEquals(exception, ex));
                    Assert.True(ReferenceEquals(conversation, conv));
                    return continueExecution;
                });

                _ = conversation.SendAsync(default, cancellationToken).AsTask();
                await Task.Delay(1000, cancellationToken);

                Assert.True(ReferenceEquals(exception, exceptionThrown));

                if (continueExecution)
                {
                    Assert.True(exceptionThrownCount > 1);
                    Assert.Equal(exceptionThrownCount, handlerInvokedCount);
                    Assert.False(conversation.TransportClosed);
                }
                else
                {
                    Assert.Equal(1, exceptionThrownCount);
                    Assert.Equal(exceptionThrownCount, handlerInvokedCount);
                    Assert.True(conversation.TransportClosed);
                }
            });
        }

        [InlineData(false)]
        [InlineData(true)]
        [Theory]
        public Task TestExceptionHandlerWithOneArgumentAndReturn(bool continueExecution)
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var exception = new InvalidDataException();
                int exceptionThrownCount = 0;
                Func<Exception> exceptionFunc = () =>
                {
                    exceptionThrownCount++;
                    return exception;
                };

                int handlerInvokedCount = 0;
                Exception? exceptionThrown = null;

                using var conversation = new KcpConversation(new ThrowingTransport(exceptionFunc, 250), 0, null);
                conversation.SetExceptionHandler((ex) =>
                {
                    handlerInvokedCount++;
                    exceptionThrown = ex;
                    Assert.True(ReferenceEquals(exception, ex));
                    return continueExecution;
                });

                _ = conversation.SendAsync(default, cancellationToken).AsTask();
                await Task.Delay(1000, cancellationToken);

                Assert.True(ReferenceEquals(exception, exceptionThrown));

                if (continueExecution)
                {
                    Assert.True(exceptionThrownCount > 1);
                    Assert.Equal(exceptionThrownCount, handlerInvokedCount);
                    Assert.False(conversation.TransportClosed);
                }
                else
                {
                    Assert.Equal(1, exceptionThrownCount);
                    Assert.Equal(exceptionThrownCount, handlerInvokedCount);
                    Assert.True(conversation.TransportClosed);
                }
            });
        }

        [Fact]
        public Task TestExceptionHandlerWithThreeArgumentAndNoReturn()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var exception = new InvalidDataException();
                int exceptionThrownCount = 0;
                Func<Exception> exceptionFunc = () =>
                {
                    exceptionThrownCount++;
                    return exception;
                };

                int handlerInvokedCount = 0;
                Exception? exceptionThrown = null;
                object obj = new();

                using var conversation = new KcpConversation(new ThrowingTransport(exceptionFunc, 250), 0, null);
                conversation.SetExceptionHandler((ex, conv, state) =>
                {
                    handlerInvokedCount++;
                    exceptionThrown = ex;
                    Assert.True(ReferenceEquals(exception, ex));
                    Assert.True(ReferenceEquals(conversation, conv));
                    Assert.True(ReferenceEquals(obj, state));
                }, obj);

                _ = conversation.SendAsync(default, cancellationToken).AsTask();
                await Task.Delay(1000, cancellationToken);

                Assert.True(ReferenceEquals(exception, exceptionThrown));

                Assert.Equal(1, exceptionThrownCount);
                Assert.Equal(exceptionThrownCount, handlerInvokedCount);
                Assert.True(conversation.TransportClosed);
            });
        }

        [Fact]
        public Task TestExceptionHandlerWithTwoArgumentAndNoReturn()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var exception = new InvalidDataException();
                int exceptionThrownCount = 0;
                Func<Exception> exceptionFunc = () =>
                {
                    exceptionThrownCount++;
                    return exception;
                };

                int handlerInvokedCount = 0;
                Exception? exceptionThrown = null;

                using var conversation = new KcpConversation(new ThrowingTransport(exceptionFunc, 250), 0, null);
                conversation.SetExceptionHandler((ex, conv) =>
                {
                    handlerInvokedCount++;
                    exceptionThrown = ex;
                    Assert.True(ReferenceEquals(exception, ex));
                    Assert.True(ReferenceEquals(conversation, conv));
                });

                _ = conversation.SendAsync(default, cancellationToken).AsTask();
                await Task.Delay(1000, cancellationToken);

                Assert.True(ReferenceEquals(exception, exceptionThrown));

                Assert.Equal(1, exceptionThrownCount);
                Assert.Equal(exceptionThrownCount, handlerInvokedCount);
                Assert.True(conversation.TransportClosed);
            });
        }

        [Fact]
        public Task TestExceptionHandlerWithOneArgumentAndNoReturn()
        {
            return TestHelper.RunWithTimeout(TimeSpan.FromSeconds(10), async cancellationToken =>
            {
                var exception = new InvalidDataException();
                int exceptionThrownCount = 0;
                Func<Exception> exceptionFunc = () =>
                {
                    exceptionThrownCount++;
                    return exception;
                };

                int handlerInvokedCount = 0;
                Exception? exceptionThrown = null;

                using var conversation = new KcpConversation(new ThrowingTransport(exceptionFunc, 250), 0, null);
                conversation.SetExceptionHandler((ex) =>
                {
                    handlerInvokedCount++;
                    exceptionThrown = ex;
                    Assert.True(ReferenceEquals(exception, ex));
                });

                _ = conversation.SendAsync(default, cancellationToken).AsTask();
                await Task.Delay(1000, cancellationToken);

                Assert.True(ReferenceEquals(exception, exceptionThrown));

                Assert.Equal(1, exceptionThrownCount);
                Assert.Equal(exceptionThrownCount, handlerInvokedCount);
                Assert.True(conversation.TransportClosed);
            });
        }

        class ThrowingTransport : IKcpTransport
        {
            private readonly Func<Exception> _exceptionFunc;
            private readonly int _delay;

            public ThrowingTransport(Func<Exception> exceptionFunc, int delay)
            {
                _exceptionFunc = exceptionFunc;
                _delay = delay;
            }

            public async ValueTask SendPacketAsync(Memory<byte> packet, CancellationToken cancellationToken)
            {
                await Task.Delay(_delay, cancellationToken);

                throw _exceptionFunc();
            }
        }
    }
}
