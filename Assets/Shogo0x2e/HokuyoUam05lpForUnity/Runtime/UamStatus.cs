using System;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity
{
    public readonly struct UamStatus : IEquatable<UamStatus>
    {
        public readonly byte OperatingMode;
        public readonly ushort AreaNumber;
        public readonly byte ErrorState;
        public readonly byte LockoutState;
        public readonly byte Ossd1;
        public readonly byte Ossd2;
        public readonly byte Ossd3;
        public readonly byte Ossd4;
        public readonly ushort EncoderSpeed;
        public readonly uint Timestamp;
        public readonly UamStreamMode StreamMode;

        public bool HasFault => ErrorState != 0;
        public bool IsLockedOut => LockoutState != 0;

        public UamStatus(
            byte operatingMode,
            ushort areaNumber,
            byte errorState,
            byte lockoutState,
            byte ossd1,
            byte ossd2,
            byte ossd3,
            byte ossd4,
            ushort encoderSpeed,
            uint timestamp,
            UamStreamMode streamMode)
        {
            OperatingMode = operatingMode;
            AreaNumber = areaNumber;
            ErrorState = errorState;
            LockoutState = lockoutState;
            Ossd1 = ossd1;
            Ossd2 = ossd2;
            Ossd3 = ossd3;
            Ossd4 = ossd4;
            EncoderSpeed = encoderSpeed;
            Timestamp = timestamp;
            StreamMode = streamMode;
        }

        public bool Equals(UamStatus other)
        {
            return OperatingMode == other.OperatingMode
                   && AreaNumber == other.AreaNumber
                   && ErrorState == other.ErrorState
                   && LockoutState == other.LockoutState
                   && Ossd1 == other.Ossd1
                   && Ossd2 == other.Ossd2
                   && Ossd3 == other.Ossd3
                   && Ossd4 == other.Ossd4
                   && EncoderSpeed == other.EncoderSpeed
                   && Timestamp == other.Timestamp
                   && StreamMode == other.StreamMode;
        }

        public override bool Equals(object? obj)
        {
            return obj is UamStatus other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = OperatingMode.GetHashCode();
                hash = (hash * 397) ^ AreaNumber.GetHashCode();
                hash = (hash * 397) ^ ErrorState.GetHashCode();
                hash = (hash * 397) ^ LockoutState.GetHashCode();
                hash = (hash * 397) ^ Ossd1.GetHashCode();
                hash = (hash * 397) ^ Ossd2.GetHashCode();
                hash = (hash * 397) ^ Ossd3.GetHashCode();
                hash = (hash * 397) ^ Ossd4.GetHashCode();
                hash = (hash * 397) ^ EncoderSpeed.GetHashCode();
                hash = (hash * 397) ^ Timestamp.GetHashCode();
                hash = (hash * 397) ^ StreamMode.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(UamStatus left, UamStatus right) => left.Equals(right);
        public static bool operator !=(UamStatus left, UamStatus right) => !left.Equals(right);
    }
}
