using System;
using UnityEngine;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Detection
{
    /// <summary>
    /// Wraps a detected beam with its world-space projection for Unity-side consumption.
    /// </summary>
    [Serializable]
    public readonly struct HitObservation
    {
        public HitObservation(HitDetection detection, Vector3 worldPoint)
        {
            Detection = detection;
            WorldPoint = worldPoint;
        }

        public HitDetection Detection { get; }
        public Vector3 WorldPoint { get; }

        public int StepIndex => Detection.StepIndex;
        public float DistanceMeters => Detection.DistanceMeters;
        public Vector2 SensorPoint => Detection.SensorPoint;
        public Vector3 SensorPoint3 => Detection.SensorPoint3;
    }
}
