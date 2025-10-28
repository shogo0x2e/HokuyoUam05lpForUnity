using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Internal
{
    internal sealed class TcpTransport : ITransport
    {
        private readonly TimeSpan _connectTimeout;
        private readonly TimeSpan _readTimeout;
        private readonly TimeSpan _writeTimeout;
        private readonly object _gate = new();

        private TcpClient? _client;
        private NetworkStream? _stream;

        public TcpTransport(
            TimeSpan? connectTimeout = null,
            TimeSpan? readTimeout = null,
            TimeSpan? writeTimeout = null)
        {
            _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
            _readTimeout = readTimeout ?? TimeSpan.FromSeconds(2);
            _writeTimeout = writeTimeout ?? TimeSpan.FromSeconds(2);
        }

        public event Action? Connected;
        public event Action? Disconnected;

        public bool IsConnected
        {
            get
            {
                lock (_gate)
                {
                    return _stream is not null;
                }
            }
        }

        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Host must be provided.", nameof(host));
            }

            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);

            var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_connectTimeout);

            using var registration = timeoutCts.Token.Register(static state =>
            {
                try
                {
                    ((TcpClient)state!).Close();
                }
                catch
                {
                    // ignore; best-effort cancellation
                }
            }, client);

            await client.ConnectAsync(host, port).ConfigureAwait(false);
            timeoutCts.Token.ThrowIfCancellationRequested();

            client.NoDelay = true;

            var stream = client.GetStream();
            stream.ReadTimeout = (int)_readTimeout.TotalMilliseconds;
            stream.WriteTimeout = (int)_writeTimeout.TotalMilliseconds;

            lock (_gate)
            {
                _client = client;
                _stream = stream;
            }

            Connected?.Invoke();
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            NetworkStream? stream;
            TcpClient? client;

            lock (_gate)
            {
                stream = _stream;
                client = _client;
                _stream = null;
                _client = null;
            }

            if (stream is not null)
            {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                await stream.DisposeAsync().ConfigureAwait(false);
#else
                stream.Dispose();
                await Task.CompletedTask;
#endif
            }

            client?.Close();

            if (stream is not null || client is not null)
            {
                Disconnected?.Invoke();
            }
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var stream = EnsureStream();
            return stream.ReadAsync(buffer, cancellationToken);
        }

        public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            var stream = EnsureStream();
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private NetworkStream EnsureStream()
        {
            lock (_gate)
            {
                if (_stream is null)
                {
                    throw new InvalidOperationException("Transport is not connected.");
                }

                return _stream;
            }
        }
    }
}
