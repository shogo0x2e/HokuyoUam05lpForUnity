using System;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Internal
{
    /// <summary>
    /// Contract for a byte-stream transport used by <see cref="UamClient"/>.
    /// </summary>
    /// <remarks>
    /// Implementations encapsulate how sockets (or other communication channels) are created,
    /// connected, and disposed. The protocol layer (<see cref="LidarProtocol"/>) and higher-level
    /// sensor logic rely on these callbacks to know when a connection is available and to perform
    /// framed reads/writes without caring about the actual medium (TCP, serial-over-USB, mock transport, etc.).
    /// </remarks>
    internal interface ITransport : IAsyncDisposable
    {
        /// <summary>
        /// Indicates whether a connection is currently active.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Establishes a connection to the specified endpoint.
        /// </summary>
        /// <param name="host">Hostname or IP of the UAM sensor.</param>
        /// <param name="port">Service port.</param>
        /// <param name="cancellationToken">Cancels the connect attempt.</param>
        Task ConnectAsync(string host, int port, CancellationToken cancellationToken);

        /// <summary>
        /// Closes the active connection.
        /// </summary>
        Task DisconnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Reads raw bytes into <paramref name="buffer"/>. Returns the number of bytes read.
        /// </summary>
        ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

        /// <summary>
        /// Writes the provided raw bytes to the transport.
        /// </summary>
        Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
    }
}
