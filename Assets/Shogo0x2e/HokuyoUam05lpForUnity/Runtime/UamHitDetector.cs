using System;
using UnityEngine;

namespace Shogo0x2e.HokuyoUam05lpForUnity
{
    public enum HitMode
    {
        Nearest,
        ClusterCentroid,
        RobustMedian,
    }

    /// <summary>
    /// Placeholder component for future hit detection logic.
    /// Currently unused – keep attached only if you plan to extend the library.
    /// </summary>
    public sealed class UamHitDetector : MonoBehaviour
    {
        [Header("References")]
        public UamSensor Sensor;

        [Header("Hit Criteria")]
        public float HitThresholdM = 0.45f;
        public float MinRangeM = 0.10f;
        public float MaxRangeM = 5.0f;
        public HitMode Mode = HitMode.Nearest;

        [Header("Stability")]
        public int MinConsecutiveFrames = 2;
        public int CooldownFrames = 2;
        [Range(0f, 1f)] public float SmoothingAlpha = 0.0f;

        [Header("ROI")]
        public bool UseRoi = true;
        public Transform RoiFrame;
        public Vector2 RoiSize = new(2f, 1f);

        [Header("Events")]
        public Action<Vector2> OnHit;
        public Action<Vector2> OnHitUpdated;
        public Action OnHitEnd;

        public bool TryGetLastHit(out Vector2 pos, out long timestampTicks)
        {
            pos = default;
            timestampTicks = 0;
            return false;
        }
    }
}
