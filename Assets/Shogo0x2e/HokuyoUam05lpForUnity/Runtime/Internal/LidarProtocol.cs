using System;
using System.Buffers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Internal
{
    internal sealed class LidarProtocol : IDisposable
    {
        private const byte STX = 0x02;
        private const byte ETX = 0x03;

        private readonly ITransport _transport;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public LidarProtocol(ITransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task SendCommandAsync(string header2, string sub2, ReadOnlyMemory<byte> dataAscii, CancellationToken cancellationToken)
        {
            if (header2 is null)
            {
                throw new ArgumentNullException(nameof(header2));
            }

            if (sub2 is null)
            {
                throw new ArgumentNullException(nameof(sub2));
            }

            if (header2.Length != 2)
            {
                throw new ArgumentException("Header must be 2 ASCII characters.", nameof(header2));
            }

            if (sub2.Length != 2)
            {
                throw new ArgumentException("Sub header must be 2 ASCII characters.", nameof(sub2));
            }

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                const int lengthField = 4;
                const int headerField = 2;
                const int subField = 2;
                const int crcField = 4;

                int dataLength = dataAscii.Length;
                int bodyLength = lengthField + headerField + subField + dataLength + crcField;
                if (bodyLength + 2 > ushort.MaxValue)
                {
                    throw new InvalidOperationException("Frame exceeds protocol length limit.");
                }

                byte[] buffer = ArrayPool<byte>.Shared.Rent(1 + bodyLength + 1);
                try
                {
                    buffer[0] = STX;

                    UamCodec.ToAsciiU16((ushort)(bodyLength + 2), buffer.AsSpan(1, lengthField));
                    Encoding.ASCII.GetBytes(header2, 0, header2.Length, buffer, 1 + lengthField);
                    Encoding.ASCII.GetBytes(sub2, 0, sub2.Length, buffer, 1 + lengthField + headerField);
                    if (!dataAscii.IsEmpty)
                    {
                        dataAscii.CopyTo(buffer.AsMemory(1 + lengthField + headerField + subField, dataLength));
                    }

                    ushort crc = UamCodec.CrcKermit(buffer.AsSpan(1, bodyLength - crcField));
                    UamCodec.ToAsciiU16(crc, buffer.AsSpan(1 + bodyLength - crcField, crcField));

                    buffer[1 + bodyLength] = ETX;

                    await _transport.WriteAsync(buffer.AsMemory(0, 1 + bodyLength + 1), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task<UamFrame?> ReadFrameAsync(UamStreamMode streamMode, CancellationToken cancellationToken)
        {
            var singleByte = new byte[1];
            var lengthBuffer = new byte[4];

            while (!cancellationToken.IsCancellationRequested)
            {
                await ReadExactlyAsync(singleByte, cancellationToken).ConfigureAwait(false);
                if (singleByte[0] != STX)
                {
                    continue;
                }

                await ReadExactlyAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
                ushort asciiLength = UamCodec.FromAsciiU16(lengthBuffer);
                if (asciiLength < 12)
                {
                    await DrainAsync(asciiLength + 1, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                int bodyLength = asciiLength - 2;
                if (bodyLength < 10)
                {
                    await DrainAsync(asciiLength + 1, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                byte[] bodyBuffer = ArrayPool<byte>.Shared.Rent(bodyLength);
                try
                {
                    lengthBuffer.CopyTo(bodyBuffer, 0);
                    await ReadExactlyAsync(bodyBuffer.AsMemory(4, bodyLength - 4), cancellationToken).ConfigureAwait(false);
                    await ReadExactlyAsync(singleByte, cancellationToken).ConfigureAwait(false);
                    if (singleByte[0] != ETX)
                    {
                        continue;
                    }

                    if (!TryParseFrame(bodyBuffer.AsSpan(0, bodyLength), streamMode, out var frame))
                    {
                        continue;
                    }

                    return frame;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(bodyBuffer);
                }
            }

            return null;
        }

        public void Dispose()
        {
            _sendLock.Dispose();
        }

        private async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await _transport.ReadAsync(buffer.Slice(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new InvalidOperationException("Remote peer closed the connection.");
                }

                offset += read;
            }
        }

        private async Task DrainAsync(int length, CancellationToken cancellationToken)
        {
            if (length <= 0)
            {
                return;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Min(length, 1024));
            try
            {
                int remaining = length;
                while (remaining > 0)
                {
                    int read = await _transport.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static bool TryParseFrame(ReadOnlySpan<byte> frame, UamStreamMode streamMode, out UamFrame result)
        {
            result = null!;
            if (frame.Length < 12)
            {
                return false;
            }

            string header = Encoding.ASCII.GetString(frame.Slice(4, 2));
            string subHeader = Encoding.ASCII.GetString(frame.Slice(6, 2));

            int crcStart = frame.Length - 4;
            ushort expectedCrc = UamCodec.FromAsciiU16(frame.Slice(crcStart, 4));
            ushort actualCrc = UamCodec.CrcKermit(frame.Slice(0, crcStart));
            if (expectedCrc != actualCrc)
            {
                return false;
            }

            string status = Encoding.ASCII.GetString(frame.Slice(8, 2));
            ReadOnlySpan<byte> dataSpan = frame.Slice(10, crcStart - 10);

            if (!string.Equals(header, "AR", StringComparison.Ordinal))
            {
                result = new UamFrame
                {
                    Header = header,
                    SubHeader = subHeader,
                    Status = status,
                    RawData = dataSpan.Length > 0 ? dataSpan.ToArray() : Array.Empty<byte>()
                };
                return true;
            }

            return TryParseArFrame(header, subHeader, status, dataSpan, streamMode, out result);
        }

        private static bool TryParseArFrame(
            string header,
            string subHeader,
            string status,
            ReadOnlySpan<byte> data,
            UamStreamMode streamMode,
            out UamFrame frame)
        {
            byte operatingMode = 0;
            ushort areaNo = 0;
            byte errorState = 0;
            byte lockoutState = 0;
            byte ossd1 = 0;
            byte ossd2 = 0;
            byte ossd3 = 0;
            byte ossd4 = 0;
            ushort encoderSpeed = 0;
            uint timestamp = 0;

            int index = 0;

            if (index < data.Length)
            {
                operatingMode = UamCodec.FromAsciiNibble(data[index]);
                index += 1;
            }

            if (index + 1 < data.Length)
            {
                areaNo = (ushort)((UamCodec.FromAsciiNibble(data[index]) << 4) | UamCodec.FromAsciiNibble(data[index + 1]));
                index += 2;
            }

            if (index + 1 < data.Length)
            {
                errorState = (byte)((UamCodec.FromAsciiNibble(data[index]) << 4) | UamCodec.FromAsciiNibble(data[index + 1]));
                index += 2;
            }

            if (index + 1 < data.Length)
            {
                lockoutState = (byte)((UamCodec.FromAsciiNibble(data[index]) << 4) | UamCodec.FromAsciiNibble(data[index + 1]));
                index += 2;
            }

            if (index < data.Length) { ossd1 = UamCodec.FromAsciiNibble(data[index++]); }
            if (index < data.Length) { ossd2 = UamCodec.FromAsciiNibble(data[index++]); }
            if (index < data.Length) { ossd3 = UamCodec.FromAsciiNibble(data[index++]); }
            if (index < data.Length) { ossd4 = UamCodec.FromAsciiNibble(data[index++]); }

            if (index + 3 < data.Length)
            {
                encoderSpeed = UamCodec.FromAsciiU16(data.Slice(index, 4));
                index += 4;
            }

            if (index + 7 < data.Length)
            {
                timestamp = 0;
                for (int i = 0; i < 8; ++i)
                {
                    timestamp = (timestamp << 4) | UamCodec.FromAsciiNibble(data[index + i]);
                }
                index += 8;
            }

            ReadOnlySpan<byte> distanceSpan = index < data.Length ? data[index..] : ReadOnlySpan<byte>.Empty;
            if (distanceSpan.IsEmpty)
            {
                frame = new UamFrame
                {
                    Header = header,
                    SubHeader = subHeader,
                    Status = status,
                    RawData = data.Length > 0 ? data.ToArray() : Array.Empty<byte>(),
                    OperatingMode = operatingMode,
                    AreaNumber = areaNo,
                    ErrorState = errorState,
                    LockoutState = lockoutState,
                    Ossd1 = ossd1,
                    Ossd2 = ossd2,
                    Ossd3 = ossd3,
                    Ossd4 = ossd4,
                    EncoderSpeed = encoderSpeed,
                    Timestamp = timestamp,
                };
                return true;
            }

            int stride;
            if (distanceSpan.Length % 8 == 0)
            {
                stride = 8; // distance + intensity (4 ascii each)
            }
            else if (distanceSpan.Length % 4 == 0)
            {
                stride = 4; // distance only
            }
            else
            {
                frame = null!;
                return false;
            }

            int pointCount = distanceSpan.Length / stride;
            ushort[] distances = pointCount > 0 ? new ushort[pointCount] : Array.Empty<ushort>();
            ushort[] intensities = stride == 8 && pointCount > 0 ? new ushort[pointCount] : Array.Empty<ushort>();

            for (int i = 0; i < pointCount; ++i)
            {
                int offset = i * stride;
                distances[i] = UamCodec.FromAsciiU16(distanceSpan.Slice(offset, 4));
                if (stride == 8)
                {
                    intensities[i] = UamCodec.FromAsciiU16(distanceSpan.Slice(offset + 4, 4));
                }
            }

            var scan = new PolarScan(distances, intensities, timestamp, streamMode);
            frame = new UamFrame
            {
                Header = header,
                SubHeader = subHeader,
                Status = status,
                RawData = data.Length > 0 ? data.ToArray() : Array.Empty<byte>(),
                OperatingMode = operatingMode,
                AreaNumber = areaNo,
                ErrorState = errorState,
                LockoutState = lockoutState,
                Ossd1 = ossd1,
                Ossd2 = ossd2,
                Ossd3 = ossd3,
                Ossd4 = ossd4,
                EncoderSpeed = encoderSpeed,
                Timestamp = timestamp,
                Scan = scan,
            };
            return true;
        }
    }
}
