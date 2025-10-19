using System;
using System.Runtime.CompilerServices;

namespace Shogo0x2e.HokuyoUam05lpForUnity.Internal
{
    internal static class UamCodec
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte FromAsciiNibble(byte ch)
        {
            if ((uint)(ch - 0x30) <= 9)
            {
                return (byte)(ch - 0x30);
            }

            if ((uint)(ch - 0x41) <= 5)
            {
                return (byte)(ch - 0x37);
            }

            throw new FormatException($"Invalid hex nibble: 0x{ch:X2}");
        }

        public static void ToAsciiU16(ushort value, Span<byte> destination)
        {
            if (destination.Length < 4)
            {
                throw new ArgumentException("Destination span must have length >= 4", nameof(destination));
            }

            for (int i = 3; i >= 0; --i)
            {
                byte nibble = (byte)(value & 0xF);
                destination[i] = (byte)(nibble < 10 ? nibble + 0x30 : nibble + 0x37);
                value >>= 4;
            }
        }

        public static ushort FromAsciiU16(ReadOnlySpan<byte> source)
        {
            if (source.Length < 4)
            {
                throw new ArgumentException("Source span must have length >= 4", nameof(source));
            }

            ushort result = 0;
            for (int i = 0; i < 4; ++i)
            {
                result = (ushort)((result << 4) | FromAsciiNibble(source[i]));
            }

            return result;
        }

        public static ushort CrcKermit(ReadOnlySpan<byte> data)
        {
            ushort crc = 0x0000;
            foreach (byte b in data)
            {
                int tmp = (crc ^ b) & 0xFF;
                tmp ^= (tmp << 4) & 0xFF;
                crc = (ushort)(((crc >> 8) ^ (tmp << 8) ^ (tmp << 3) ^ (tmp >> 4)) & 0xFFFF);
            }

            return crc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsErrorDistance(ushort distance)
        {
            return distance is 0xFFFF or 0xFFFE or 0xFFFD or 0xFFFC;
        }
    }
}
