using System;
using System.Threading;
using System.Threading.Tasks;

namespace KcpSharp
{
    /// <summary>
    /// A conversation or a channel over the transport.
    /// </summary>
    public interface IKcpConversation : IDisposable
    {
        /// <summary>
        /// Put message into the receive queue of the channel.
        /// </summary>
        /// <param name="packet">The packet content with conversation ID.</param>
        /// <param name="cancellationToken">The token to cancel this operation.</param>
        /// <returns>A <see cref="ValueTask"/> that completes when the packet is put into the receive queue.</returns>
        ValueTask InputPakcetAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);

        /// <summary>
        /// Mark the underlying transport as closed. Abort all active send or receive operations.
        /// </summary>
        void SetTransportClosed();
    }
}
