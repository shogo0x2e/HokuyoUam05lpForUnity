using System;
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shogo0x2e.HokuyoUam05lpForUnity;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Internal
{
    internal sealed class UamClient : IAsyncDisposable
    {
        private const byte STX = 0x02;
        private const byte ETX = 0x03;

        private readonly TimeSpan _connectBackoffMin = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _connectBackoffMax = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _keepAliveInterval = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _frameTimeout = TimeSpan.FromSeconds(3);
        private readonly TimeSpan _socketTimeout = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(5);

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly object _runGate = new();

        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private CancellationTokenSource? _loopCts;
        private Task? _runLoopTask;

        private string? _host;
        private int _port;

        private volatile bool _continuous;
        private long _lastRxTicks;
        private long _lastTxTicks;

        private Action<UamFrame>? _onFrame;
        private Action<Exception>? _onError;
        private Action? _onConnected;
        private Action? _onDisconnected;

        private UamStreamMode _desiredMode = UamStreamMode.Standard;

        public event Action<UamFrame>? FrameReceived;
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<Exception>? Error;

        public bool IsRunning => _runLoopTask is { IsCompleted: false };
        public bool IsConnected => _stream is not null;

        public Action<UamFrame>? OnFrame
        {
            get => _onFrame;
            set => _onFrame = value;
        }

        public Action<Exception>? OnError
        {
            get => _onError;
            set => _onError = value;
        }

        public Action? OnConnected
        {
            get => _onConnected;
            set => _onConnected = value;
        }

        public Action? OnDisconnected
        {
            get => _onDisconnected;
            set => _onDisconnected = value;
        }

        public void Start()
        {
            StartInternal();
        }

        public void Start(string host, int port)
        {
            ResetEndpoint(host, port);
            StartInternal();
        }

        public async Task StopAsync()
        {
            CancellationTokenSource? cts;
            Task? runner;

            lock (_runGate)
            {
                cts = _loopCts;
                runner = _runLoopTask;
                _loopCts = null;
                _runLoopTask = null;
            }

            if (cts is null)
            {
                await SafeCloseAsync().ConfigureAwait(false);
                return;
            }

            cts.Cancel();
            try
            {
                if (runner is not null)
                {
                    await runner.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // expected during shutdown
            }
            finally
            {
                cts.Dispose();
            }

            try
            {
                await StopContinuousAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // ignore best-effort shutdown
            }

            await SafeCloseAsync().ConfigureAwait(false);
        }

        public void ResetEndpoint(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Host must be provided.", nameof(host));
            }

            _host = host;
            _port = port;
        }

        public void SetStreamMode(UamStreamMode mode)
        {
            _desiredMode = mode;
        }

        private void StartInternal()
        {
            lock (_runGate)
            {
                if (_loopCts is not null)
                {
                    throw new InvalidOperationException("Client is already running.");
                }

                if (_host is null)
                {
                    throw new InvalidOperationException("Endpoint is not configured. Call Start(host, port) first.");
                }

                _loopCts = new CancellationTokenSource();
                _runLoopTask = Task.Run(() => ConnectAndRunAsync(_loopCts.Token));
            }
        }

        private async Task ConnectAndRunAsync(CancellationToken cancellationToken)
        {
            var rng = new Random();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ConnectAsync(cancellationToken).ConfigureAwait(false);
                    await HandshakeAsync(cancellationToken).ConfigureAwait(false);
                    await StartContinuousAsync(cancellationToken).ConfigureAwait(false);
                    await RunIoLoopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    InvokeError(ex);
                    await SafeCloseAsync().ConfigureAwait(false);
                    int min = (int)_connectBackoffMin.TotalMilliseconds;
                    int max = (int)_connectBackoffMax.TotalMilliseconds;
                    int delayMs = rng.Next(min, Math.Max(min + 1, max));
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task ConnectAsync(CancellationToken cancellationToken)
        {
            await SafeCloseAsync().ConfigureAwait(false);

            if (_host is null)
            {
                throw new InvalidOperationException("Endpoint is not configured.");
            }

            var tcpClient = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(_connectTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var registration = linked.Token.Register(static state =>
            {
                try
                {
                    ((TcpClient)state!).Close();
                }
                catch
                {
                    // ignore; best-effort cancellation
                }
            }, tcpClient);

            await tcpClient.ConnectAsync(_host, _port).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            tcpClient.NoDelay = true;

            var stream = tcpClient.GetStream();
            stream.ReadTimeout = (int)_socketTimeout.TotalMilliseconds;
            stream.WriteTimeout = (int)_socketTimeout.TotalMilliseconds;

            _tcpClient = tcpClient;
            _stream = stream;
            _continuous = false;
            StampNow(ref _lastRxTicks);
            StampNow(ref _lastTxTicks);

            InvokeConnected();
        }

        private async Task HandshakeAsync(CancellationToken cancellationToken)
        {
            await SendCommandAsync("VR", "00", cancellationToken: cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_socketTimeout);
            var reply = await WaitForReplyAsync(static frame => string.Equals(frame.Header, "VR", StringComparison.Ordinal), timeout.Token).ConfigureAwait(false);
            if (!string.Equals(reply.Status, "00", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"VR00 failed with status {reply.Status}");
            }
        }

        private Task StartContinuousAsync(CancellationToken cancellationToken)
        {
            _continuous = true;
            return SendCommandAsync("AR", GetContinuousStartSubHeader(_desiredMode), cancellationToken: cancellationToken);
        }

        private async Task StopContinuousAsync(CancellationToken cancellationToken)
        {
            if (!_continuous || _stream is null)
            {
                _continuous = false;
                return;
            }

            try
            {
                await SendCommandAsync("AR", "03", cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _continuous = false;
            }
        }

        private async Task RunIoLoopAsync(CancellationToken cancellationToken)
        {
            var readTask = ReadRepliesAsync(cancellationToken);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await PerformMaintenanceAsync(cancellationToken).ConfigureAwait(false);

                    if (readTask.IsCompleted)
                    {
                        await readTask.ConfigureAwait(false);
                        break;
                    }

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

                await readTask.ConfigureAwait(false);
            }
            catch
            {
                if (readTask.IsFaulted)
                {
                    readTask.GetAwaiter().GetResult();
                }

                throw;
            }
            finally
            {
                InvokeDisconnected();
            }
        }

        private async Task PerformMaintenanceAsync(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            if (now - ReadTimestamp(ref _lastTxTicks) >= _keepAliveInterval)
            {
                await SendCommandAsync("VR", "00", cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (_continuous && now - ReadTimestamp(ref _lastRxTicks) >= _frameTimeout)
            {
                await SendCommandAsync("AR", GetContinuousStartSubHeader(_desiredMode), cancellationToken: cancellationToken).ConfigureAwait(false);
                StampNow(ref _lastRxTicks);
            }
        }

        private async Task ReadRepliesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UamFrame? frame;
                try
                {
                    frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (frame is null)
                {
                    continue;
                }

                if (!string.Equals(frame.Status, "00", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(frame.Header, "AR", StringComparison.Ordinal))
                {
                    StampNow(ref _lastRxTicks);
                    InvokeFrameCallbacks(frame);
                }
            }
        }

        private async Task<UamFrame?> ReadFrameAsync(CancellationToken cancellationToken)
        {
            var stream = EnsureStream();
            var singleByte = new byte[1];
            var lengthBuffer = new byte[4];

            while (!cancellationToken.IsCancellationRequested)
            {
                await ReadExactlyAsync(stream, singleByte, cancellationToken).ConfigureAwait(false);
                if (singleByte[0] != STX)
                {
                    continue;
                }

                await ReadExactlyAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false);
                ushort asciiLength = UamCodec.FromAsciiU16(lengthBuffer);

                if (asciiLength < 12)
                {
                    await DrainAsync(stream, asciiLength + 1, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                int bodyLength = asciiLength - 2;
                if (bodyLength < 10)
                {
                    await DrainAsync(stream, asciiLength + 1, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                byte[] bodyBuffer = ArrayPool<byte>.Shared.Rent(bodyLength);
                try
                {
                    lengthBuffer.CopyTo(bodyBuffer, 0);
                    await ReadExactlyAsync(stream, bodyBuffer.AsMemory(4, bodyLength - 4), cancellationToken).ConfigureAwait(false);
                    await ReadExactlyAsync(stream, singleByte, cancellationToken).ConfigureAwait(false);
                    if (singleByte[0] != ETX)
                    {
                        continue;
                    }

                    if (!TryParseFrame(bodyBuffer.AsSpan(0, bodyLength), out var frame))
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

        private async Task<UamFrame> WaitForReplyAsync(Func<UamFrame, bool> predicate, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    continue;
                }

                if (string.Equals(frame.Header, "AR", StringComparison.Ordinal) && string.Equals(frame.Status, "00", StringComparison.Ordinal))
                {
                    StampNow(ref _lastRxTicks);
                    InvokeFrameCallbacks(frame);
                }

                if (predicate(frame))
                {
                    return frame;
                }
            }

            throw new OperationCanceledException(cancellationToken);
        }

        private void InvokeFrameCallbacks(UamFrame frame)
        {
            var frameHandler = FrameReceived;
            frameHandler?.Invoke(frame);

            _onFrame?.Invoke(frame);
        }

        private void InvokeError(Exception exception)
        {
            var handler = Error;
            handler?.Invoke(exception);

            _onError?.Invoke(exception);
        }

        private void InvokeConnected()
        {
            var handler = Connected;
            handler?.Invoke();

            _onConnected?.Invoke();
        }

        private void InvokeDisconnected()
        {
            var handler = Disconnected;
            handler?.Invoke();

            _onDisconnected?.Invoke();
        }

        public async Task SendCommandAsync(string header2, string sub2, ReadOnlyMemory<byte> dataAscii = default, CancellationToken cancellationToken = default)
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
                var stream = EnsureStream();

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

                    await stream.WriteAsync(buffer, 0, 1 + bodyLength + 1, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                StampNow(ref _lastTxTicks);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private NetworkStream EnsureStream()
        {
            if (_stream is null)
            {
                throw new InvalidOperationException("Client is not connected.");
            }

            return _stream;
        }

        private static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.Slice(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new InvalidOperationException("Remote peer closed the connection.");
                }

                offset += read;
            }
        }

        private static Task DrainAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
        {
            if (length <= 0)
            {
                return Task.CompletedTask;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Min(length, 1024));
            try
            {
                int remaining = length;
                while (remaining > 0)
                {
                    int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
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

            return Task.CompletedTask;
        }

        private static void StampNow(ref long ticks)
        {
            Interlocked.Exchange(ref ticks, DateTime.UtcNow.Ticks);
        }

        private static DateTime ReadTimestamp(ref long ticks)
        {
            long current = Interlocked.Read(ref ticks);
            return new DateTime(current, DateTimeKind.Utc);
        }

        private Task SafeCloseAsync()
        {
            NetworkStream? stream = Interlocked.Exchange(ref _stream, null);
            Task disposeTask = Task.CompletedTask;

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            if (stream is not null)
            {
                disposeTask = stream.DisposeAsync().AsTask();
            }
#else
            stream?.Dispose();
#endif

            TcpClient? client = Interlocked.Exchange(ref _tcpClient, null);
            client?.Close();

            return disposeTask;
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _sendLock.Dispose();
        }

        private static bool TryParseFrame(ReadOnlySpan<byte> frame, out UamFrame result)
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

            return TryParseArFrame(header, subHeader, status, dataSpan, out result);
        }

        private static bool TryParseArFrame(string header, string subHeader, string status, ReadOnlySpan<byte> data, out UamFrame frame)
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
                Distances = distances,
                Intensities = intensities,
            };
            return true;
        }

        private static string GetContinuousStartSubHeader(UamStreamMode mode)
        {
            // NOTE: AR02 requests continuous measurement frames.
            // Additional mode-specific setup (e.g., high resolution or intensity output) should be
            // issued by higher-level code before entering the continuous stream. For now we use
            // AR02 across all modes until protocol details are finalised.
            return mode switch
            {
                UamStreamMode.Standard => "02",
                UamStreamMode.WithIntensity => "02",
                UamStreamMode.HighResolution => "02",
                _ => "02",
            };
        }
    }
}
