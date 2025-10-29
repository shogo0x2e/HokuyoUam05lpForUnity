using System;
using System.Collections.Generic;
using UnityEngine;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Detection
{
    /// <summary>
    /// Performs simple ROI-aware beam filtering and emits the corresponding step indices.
    /// </summary>
    public sealed class HitDetector
    {
        private const float MilliMetersToMeters = 0.001f;

        /// <summary>
        /// Beams shorter than this distance (in metres) will be ignored. Defaults to 5 cm to filter sensor noise.
        /// </summary>
        [Min(0f)]
        public float MinDistanceMeters { get; set; } = 0.05f;

        /// <summary>
        /// Beams longer than this distance (in metres) will be ignored. Set to 0 to disable the upper bound.
        /// </summary>
        [Min(0f)]
        public float MaxDistanceMeters { get; set; } = 0f;

        /// <summary>
        /// When true, beams reporting 0 mm are discarded (recommended as the sensor uses zero for missing/invalid hits).
        /// </summary>
        public bool RejectZeroDistance { get; set; } = true;

        /// <summary>
        /// クラスタリングに使用する距離しきい値 (メートル)。0 以下の場合は各ビームを個別のヒットとして扱います。
        /// </summary>
        [Min(0f)]
        public float GroupingDistanceMeters { get; set; } = 0.1f;

        /// <summary>
        /// Filters the provided scan and populates <paramref name="results"/> with beams that lie inside the ROI.
        /// </summary>
        /// <remarks>
        /// The method reuses the provided <paramref name="results"/> list (it will be cleared before filling).
        /// Callers can keep a dedicated buffer to avoid per-frame allocations.
        /// </remarks>
        /// <param name="scan">Polar scan to inspect.</param>
        /// <param name="roiPredicate">Optional predicate returning true when a sensor-local point is inside the ROI.</param>
        /// <param name="results">Destination list that will receive the hits.</param>
        public void Detect(IPolarScan scan, Func<Vector2, bool>? roiPredicate, List<HitDetection> results)
        {
            if (scan is null)
            {
                throw new ArgumentNullException(nameof(scan));
            }

            if (results is null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            var distances = scan.Distances.Span;
            var directions = UamAngleTable.GetDirections(scan.StreamMode);
            int count = Math.Min(distances.Length, directions.Length);

            float minDistance = Mathf.Max(0f, MinDistanceMeters);
            float maxDistance = MaxDistanceMeters <= 0f ? float.PositiveInfinity : MaxDistanceMeters;
            bool hasRoi = roiPredicate is not null;

            float groupingDistance = Mathf.Max(0f, GroupingDistanceMeters);
            bool groupingEnabled = groupingDistance > 0f;
            float groupingDistanceSq = groupingDistance * groupingDistance;

            bool hasCluster = false;
            HitDetection clusterBest = default;
            Vector2 lastPoint = Vector2.zero;

            for (int i = 0; i < count; ++i)
            {
                ushort distanceMillimetres = distances[i];
                if (RejectZeroDistance && distanceMillimetres == 0)
                {
                    continue;
                }

                float distanceMeters = distanceMillimetres * MilliMetersToMeters;
                if (distanceMeters < minDistance || distanceMeters > maxDistance)
                {
                    continue;
                }

                Vector2 sensorPoint = directions[i] * distanceMeters;
                if (hasRoi && !roiPredicate!(sensorPoint))
                {
                    continue;
                }

                var detection = new HitDetection(i, distanceMeters, sensorPoint);

                if (!groupingEnabled)
                {
                    results.Add(detection);
                    continue;
                }

                if (!hasCluster)
                {
                    clusterBest = detection;
                    lastPoint = sensorPoint;
                    hasCluster = true;
                    continue;
                }

                float separationSq = Vector2.SqrMagnitude(sensorPoint - lastPoint);
                if (separationSq > groupingDistanceSq)
                {
                    results.Add(clusterBest);
                    clusterBest = detection;
                }
                else if (distanceMeters < clusterBest.DistanceMeters)
                {
                    clusterBest = detection;
                }

                lastPoint = sensorPoint;
            }

            if (groupingEnabled)
            {
                if (hasCluster)
                {
                    results.Add(clusterBest);
                }
            }
        }
    }
}
