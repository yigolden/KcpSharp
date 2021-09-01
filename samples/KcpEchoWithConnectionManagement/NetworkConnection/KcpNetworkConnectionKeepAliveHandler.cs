using System.Buffers.Binary;
using System.Diagnostics;
using KcpSharp;

namespace KcpEchoWithConnectionManagement.NetworkConnection
{
    internal sealed class KcpNetworkConnectionKeepAliveHandler : IThreadPoolWorkItem, IDisposable
    {
        private readonly KcpNetworkConnection _networkConnection;
        private readonly IKcpConnectionKeepAliveContext? _keepAliveContext;
        private uint _remoteNextSerial;
        private uint _lastSerial;

        private Timer? _timer;
        private int _isOperationActive;
        private bool _isDisposed;

        public KcpNetworkConnectionKeepAliveHandler(KcpNetworkConnection networkConnection, IKcpConnectionKeepAliveContext? keepAliveContext, TimeSpan? interval)
        {
            _networkConnection = networkConnection;
            _keepAliveContext = keepAliveContext;

            if (interval.HasValue)
            {
                _timer = new Timer(state =>
                {
                    var reference = (WeakReference<KcpNetworkConnectionKeepAliveHandler>)state!;
                    if (reference.TryGetTarget(out KcpNetworkConnectionKeepAliveHandler? handler))
                    {
                        handler.Execute();
                    }
                }, new WeakReference<KcpNetworkConnectionKeepAliveHandler>(this), TimeSpan.Zero, interval.GetValueOrDefault());
            }
        }

        public bool ProcessKeepAlivePacket(ReadOnlySpan<byte> packet)
        {
            if (_keepAliveContext is null)
            {
                return false;
            }
            if (packet.Length < 16 || packet[0] != 2 || packet[1] != 0)
            {
                return false;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2));
            if ((packet.Length - 4) < length || length < 12)
            {
                return false;
            }
            ReadOnlySpan<byte> payload = packet.Slice(4);

            uint lastSerial = BinaryPrimitives.ReadUInt32BigEndian(payload);
            uint nextSerial = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4));
            uint packetsAcknowledged = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8));
            payload = payload.Slice(12);

            if ((int)(nextSerial - _remoteNextSerial) < 0)
            {
                return false;
            }
            _remoteNextSerial = nextSerial;

            ReadOnlySpan<byte> customPayload = default;
            if (payload.Length > 4)
            {
                if (BinaryPrimitives.ReadUInt16BigEndian(payload) == 0x1 && payload[2] == 0)
                {
                    length = payload[3];
                    payload = payload.Slice(4);
                    if (payload.Length > length)
                    {
                        customPayload = payload.Slice(0, length);
                    }
                }
            }

            _keepAliveContext.UpdateSample(nextSerial - lastSerial, packetsAcknowledged, customPayload);
            return true;
        }

        public void ProduceKeepAlivePacket(uint serial, uint packetsReceived)
        {
            uint lastSerial = _lastSerial;
            _lastSerial = serial;

            IKcpBufferPool bufferPool = _networkConnection.GetAllocator();
            using KcpRentedBuffer rentedBuffer = bufferPool.Rent(new KcpBufferPoolRentOptions(20 + 256, false));
            Span<byte> buffer = rentedBuffer.Span;

            if (buffer.Length < 20 + 256)
            {
                Debug.Fail("Invalid buffer.");
                return;
            }

            buffer[0] = 2;
            buffer[1] = 0;
            buffer[2] = 0;
            buffer[3] = 12;

            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(4), lastSerial);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(8), serial);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(12), packetsReceived);

            byte bytesWritten = _keepAliveContext is null ? (byte)0 : _keepAliveContext.PreparePayload(buffer.Slice(20, 256));
            int totalPacketSize = 16;
            if (bytesWritten > 0)
            {
                buffer[16] = 0;
                buffer[17] = 1;
                buffer[18] = 0;
                buffer[19] = bytesWritten;
                BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2), (ushort)(16 + bytesWritten));
                totalPacketSize = 20 + bytesWritten;
            }

            _networkConnection.QueueRawPacket(buffer.Slice(0, totalPacketSize));
        }

        public void Send()
        {
            if (_isDisposed || _isOperationActive != 0)
            {
                return;
            }
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }

        public void Execute()
        {
            if (Interlocked.Exchange(ref _isOperationActive, 1) != 0)
            {
                return;
            }
            try
            {
                if (_isDisposed)
                {
                    return;
                }
                (uint serial, uint packetsReceived) = _networkConnection.GatherPacketStatistics();
                ProduceKeepAlivePacket(serial, packetsReceived);
            }
            catch
            {
                // can not leak exceptions
            }
            finally
            {
                Interlocked.Exchange(ref _isOperationActive, 0);
            }
        }

        public void Dispose()
        {
            _isDisposed = true;
            Timer? timer = Interlocked.Exchange(ref _timer, null);
            if (timer is not null)
            {
                timer.Dispose();
            }
        }

    }
}
