using System;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Internal
{
    internal sealed class UamClient : IAsyncDisposable
    {
        private readonly TimeSpan _connectBackoffMin = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _connectBackoffMax = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _keepAliveInterval = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _frameTimeout = TimeSpan.FromSeconds(3);
        private readonly TimeSpan _socketTimeout = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(5);

        private readonly object _runGate = new();
        private readonly ITransport _transport;
        private readonly LidarProtocol _protocol;

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

        public bool IsRunning => _runLoopTask is { IsCompleted: false };
        public bool IsConnected => _transport.IsConnected;

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

        public UamClient()
            : this(new TcpTransport())
        {
        }

        internal UamClient(ITransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _protocol = new LidarProtocol(_transport);
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

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _protocol.Dispose();
            await _transport.DisposeAsync().ConfigureAwait(false);
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

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_connectTimeout);

            await _transport.ConnectAsync(_host, _port, timeoutCts.Token).ConfigureAwait(false);
            timeoutCts.Token.ThrowIfCancellationRequested();

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
            if (!_continuous || !_transport.IsConnected)
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
                    frame = await _protocol.ReadFrameAsync(_desiredMode, cancellationToken).ConfigureAwait(false);
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

        private async Task<UamFrame> WaitForReplyAsync(Func<UamFrame, bool> predicate, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await _protocol.ReadFrameAsync(_desiredMode, cancellationToken).ConfigureAwait(false);
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
            _onFrame?.Invoke(frame);
        }

        private void InvokeError(Exception exception)
        {
            _onError?.Invoke(exception);
        }

        private void InvokeConnected()
        {
            _onConnected?.Invoke();
        }

        private void InvokeDisconnected()
        {
            _onDisconnected?.Invoke();
        }

        private async Task SendCommandAsync(string header2, string sub2, ReadOnlyMemory<byte> dataAscii = default, CancellationToken cancellationToken = default)
        {
            await _protocol.SendCommandAsync(header2, sub2, dataAscii, cancellationToken).ConfigureAwait(false);
            StampNow(ref _lastTxTicks);
        }

        private Task SafeCloseAsync()
        {
            return _transport.DisconnectAsync(CancellationToken.None);
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

        private static string GetContinuousStartSubHeader(UamStreamMode mode)
        {
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
