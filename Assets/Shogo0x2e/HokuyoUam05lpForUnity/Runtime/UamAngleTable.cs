using System;
using UnityEngine;

namespace Shogo0x2e.HokuyoUam05lpForUnity
{
    public static class UamAngleTable
    {
        private static readonly Lazy<Vector2[]> StandardDirections = new(() => BuildDirections(1081, -135f, 0.25f));
        private static readonly Lazy<Vector2[]> HighResolutionDirections = new(() => BuildDirections(2161, -135f, 0.125f));

        public static ReadOnlySpan<Vector2> GetDirections(UamStreamMode mode)
        {
            return mode switch
            {
                UamStreamMode.Standard => StandardDirections.Value,
                UamStreamMode.WithIntensity => StandardDirections.Value,
                UamStreamMode.HighResolution => HighResolutionDirections.Value,
                _ => StandardDirections.Value,
            };
        }

        private static Vector2[] BuildDirections(int pointCount, float startDegrees, float stepDegrees)
        {
            var result = new Vector2[pointCount];
            float startRad = startDegrees * Mathf.Deg2Rad;
            float stepRad = stepDegrees * Mathf.Deg2Rad;
            for (int i = 0; i < pointCount; ++i)
            {
                float angle = startRad + stepRad * i;
                float lateral = Mathf.Sin(angle);
                float forward = Mathf.Cos(angle);
                result[i] = new Vector2(lateral, forward);
            }

            return result;
        }
    }
}
