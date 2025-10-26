using System;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity
{
    /// <summary>
    /// Immutable view over a single polar scan reported by the UAM device.
    /// </summary>
    /// <remarks>
    /// The scan exposes raw per-beam distance (and optional intensity) samples while carrying the
    /// accompanying frame metadata (timestamp, stream mode). Higher layers can transform the
    /// polar samples into Cartesian space or run hit detection without needing to know whether the
    /// data originated from a real sensor, captured log, or synthetic test fixture.
    /// </remarks>
    public interface IPolarScan
    {
        /// <summary>
        /// Distance samples in millimetres, indexed by beam order defined in <see cref="UamAngleTable"/>.
        /// </summary>
        ReadOnlyMemory<ushort> Distances { get; }

        /// <summary>
        /// Optional intensity samples that align with <see cref="Distances"/>. Empty when the active
        /// stream mode does not emit intensity data.
        /// </summary>
        ReadOnlyMemory<ushort> Intensities { get; }

        /// <summary>
        /// Number of valid beams in this scan (equivalent to <c>Distances.Length</c>).
        /// </summary>
        int BeamCount { get; }

        /// <summary>
        /// True when intensity data is populated.
        /// </summary>
        bool HasIntensity { get; }

        /// <summary>
        /// Device-provided 32-bit timestamp extracted from the AR frame.
        /// </summary>
        uint Timestamp { get; }

        /// <summary>
        /// Stream configuration used when the scan was acquired.
        /// </summary>
        UamStreamMode StreamMode { get; }
    }
}
