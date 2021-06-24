using System;
using System.Net;
using System.Net.Sockets;

namespace KcpSharp
{

    /// <summary>
    /// Helper methods to create socket transports for KCP conversations.
    /// </summary>
    public static class KcpSocketTransport
    {
        /// <summary>
        /// Create a socket transport for KCP covnersation.
        /// </summary>
        /// <param name="socket">The socket instance.</param>
        /// <param name="endPoint">The remote endpoint.</param>
        /// <param name="conversationId">The conversation ID.</param>
        /// <param name="options">The options of the <see cref="KcpConversation"/>.</param>
        /// <returns>The created socket transport instance.</returns>
        public static IKcpTransport<KcpConversation> CreateConversation(Socket socket, EndPoint endPoint, int conversationId, KcpConversationOptions? options)
        {
            if (socket is null)
            {
                throw new ArgumentNullException(nameof(socket));
            }
            if (endPoint is null)
            {
                throw new ArgumentNullException(nameof(endPoint));
            }

            return new KcpSocketTransportForConversation(socket, endPoint, conversationId, options);
        }

        /// <summary>
        /// Create a socket transport for raw channel.
        /// </summary>
        /// <param name="socket">The socket instance.</param>
        /// <param name="endPoint">The remote endpoint.</param>
        /// <param name="conversationId">The conversation ID.</param>
        /// <param name="options">The options of the <see cref="KcpRawChannel"/>.</param>
        /// <returns>The created socket transport instance.</returns>
        public static IKcpTransport<KcpRawChannel> CreateRawChannel(Socket socket, EndPoint endPoint, int conversationId, KcpRawChannelOptions? options)
        {
            if (socket is null)
            {
                throw new ArgumentNullException(nameof(socket));
            }
            if (endPoint is null)
            {
                throw new ArgumentNullException(nameof(endPoint));
            }

            return new KcpSocketTransportForRawChannel(socket, endPoint, conversationId, options);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="endPoint"></param>
        /// <param name="mtu"></param>
        /// <returns></returns>
        public static IKcpTransport<IKcpMultiplexConnection> CreateMultiplexConnection(Socket socket, EndPoint endPoint, int mtu)
        {
            if (socket is null)
            {
                throw new ArgumentNullException(nameof(socket));
            }
            if (endPoint is null)
            {
                throw new ArgumentNullException(nameof(endPoint));
            }

            return new KcpSocketTransportForMultiplexConnection<object>(socket, endPoint, mtu);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="socket"></param>
        /// <param name="endPoint"></param>
        /// <param name="mtu"></param>
        /// <returns></returns>
        public static IKcpTransport<IKcpMultiplexConnection<T>> CreateMultiplexConnection<T>(Socket socket, EndPoint endPoint, int mtu)
        {
            if (socket is null)
            {
                throw new ArgumentNullException(nameof(socket));
            }
            if (endPoint is null)
            {
                throw new ArgumentNullException(nameof(endPoint));
            }

            return new KcpSocketTransportForMultiplexConnection<T>(socket, endPoint, mtu);
        }
    }
}
