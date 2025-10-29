using System;
using UnityEngine;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Detection
{
    /// <summary>
    /// Describes a single sensor beam that satisfied the configured ROI filter.
    /// </summary>
    [Serializable]
    public readonly struct HitDetection
    {
        public HitDetection(int stepIndex, float distanceMeters, Vector2 sensorPoint)
        {
            StepIndex = stepIndex;
            DistanceMeters = distanceMeters;
            SensorPoint = sensorPoint;
        }

        /// <summary>
        /// Beam index as defined by the device datasheet (0-based, corresponds to <see cref="UamAngleTable"/> order).
        /// </summary>
        public int StepIndex { get; }

        /// <summary>
        /// Measured distance for the beam in metres.
        /// </summary>
        public float DistanceMeters { get; }

        /// <summary>
        /// Sensor-local intersection point expressed in metres. X: lateral, Y: forward.
        /// </summary>
        public Vector2 SensorPoint { get; }

        /// <summary>
        /// Distance converted back to millimetres for quick logging/debugging.
        /// </summary>
        public float DistanceMillimetres => DistanceMeters * 1000f;

        /// <summary>
        /// Sensor-local 3D point on the plane y = 0 for gizmo drawing convenience.
        /// </summary>
        public Vector3 SensorPoint3 => new(SensorPoint.x, 0f, SensorPoint.y);
    }
}
