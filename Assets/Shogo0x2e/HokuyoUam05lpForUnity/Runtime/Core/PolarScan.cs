using System;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Internal
{
    internal readonly struct PolarScan : IPolarScan
    {
        private static readonly ushort[] EmptyDistances = Array.Empty<ushort>();
        private static readonly ushort[] EmptyIntensities = Array.Empty<ushort>();

        private readonly ushort[] _distances;
        private readonly ushort[] _intensities;

        public PolarScan(ushort[]? distances, ushort[]? intensities, uint timestamp, UamStreamMode streamMode)
        {
            _distances = distances ?? EmptyDistances;
            _intensities = intensities ?? EmptyIntensities;
            Timestamp = timestamp;
            StreamMode = streamMode;
        }

        public ReadOnlyMemory<ushort> Distances => _distances;
        public ReadOnlyMemory<ushort> Intensities => _intensities;
        public int BeamCount => _distances.Length;
        public bool HasIntensity => _intensities.Length > 0;
        public uint Timestamp { get; }
        public UamStreamMode StreamMode { get; }
    }
}
