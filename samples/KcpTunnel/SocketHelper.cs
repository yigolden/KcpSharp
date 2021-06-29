using System;
using System.Net.Sockets;

namespace KcpTunnel
{
    internal static class SocketHelper
    {
        public static void PatchSocket(Socket socket)
        {
            if (OperatingSystem.IsWindows())
            {
                uint IOC_IN = 0x80000000;
                uint IOC_VENDOR = 0x18000000;
                uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                socket.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
            }
        }
    }
}
