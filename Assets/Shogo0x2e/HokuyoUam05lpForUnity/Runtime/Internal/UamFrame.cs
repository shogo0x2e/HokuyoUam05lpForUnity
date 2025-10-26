using System;
using Shogo0x2e.HokuyoUam05lpForUnity;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Internal
{
    internal sealed class UamFrame
    {
        public string Header { get; set; } = string.Empty;
        public string SubHeader { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public byte[] RawData { get; set; } = Array.Empty<byte>();

        public byte OperatingMode { get; set; }
        public ushort AreaNumber { get; set; }
        public byte ErrorState { get; set; }
        public byte LockoutState { get; set; }
        public byte Ossd1 { get; set; }
        public byte Ossd2 { get; set; }
        public byte Ossd3 { get; set; }
        public byte Ossd4 { get; set; }
        public uint Timestamp { get; set; }
        public ushort EncoderSpeed { get; set; }
        public IPolarScan? Scan { get; set; }

        public bool HasDistanceData => Scan is { BeamCount: > 0 };
    }
}
