using System;
using System.Collections.Generic;
using Shogo0x2e.HokuyoUam05lpForUnity.Projection;
using UnityEngine;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity
{
    /// <summary>
    /// ScriptableObject that stores baseline distances (D0) captured from a reference scan.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ProjectionSurfaceCalibration",
        menuName = "Hokuyo UAM/Projection Surface Calibration",
        order = 1000)]
    public sealed class ProjectionSurfaceCalibration : ScriptableObject
    {
        [SerializeField]
        private List<SerializedBaseline> baselines = new();

        [NonSerialized]
        private ProjectionCalibrationTable? runtimeTable;

        [NonSerialized]
        private bool runtimeDirty = true;

        private void OnEnable()
        {
            runtimeDirty = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            runtimeDirty = true;
        }
#endif

        /// <summary>
        /// Returns true when a baseline exists for the provided stream mode.
        /// </summary>
        public bool TryGetBaseline(UamStreamMode streamMode, out ReadOnlyMemory<int> distances, out DateTime capturedAtUtc, out string note)
        {
            var table = EnsureRuntimeTable();
            return table.TryGetBaseline(streamMode, out distances, out capturedAtUtc, out note);
        }

        /// <summary>
        /// Creates or updates the baseline associated with <paramref name="streamMode"/>.
        /// </summary>
        public void SetBaseline(UamStreamMode streamMode, ReadOnlySpan<int> distances, DateTime capturedAtUtc, string? note = null)
        {
            if (distances.Length == 0)
            {
                throw new ArgumentException("距離配列が空です。", nameof(distances));
            }

            int expectedBeams = UamAngleTable.GetDirections(streamMode).Length;
            if (expectedBeams != 0 && distances.Length != expectedBeams)
            {
                throw new ArgumentException($"距離配列の長さがストリームモード {streamMode} のビーム数 ({expectedBeams}) と一致しません。", nameof(distances));
            }

            var normalizedTimestamp = NormalizeTimestamp(capturedAtUtc);

            bool updated = false;
            for (int i = 0; i < baselines.Count; ++i)
            {
                if (baselines[i].StreamMode == streamMode)
                {
                    baselines[i] = baselines[i].With(distances, normalizedTimestamp, note);
                    updated = true;
                    break;
                }
            }

            if (!updated)
            {
                baselines.Add(SerializedBaseline.From(streamMode, distances, normalizedTimestamp, note));
            }

            runtimeDirty = true;
        }

        /// <summary>
        /// Returns a defensive copy of the baseline distances for runtime usage.
        /// </summary>
        public int[] GetBaselineCopy(UamStreamMode streamMode)
        {
            var table = EnsureRuntimeTable();
            return table.TryGetBaseline(streamMode, out var distances, out _, out _)
                ? distances.ToArray()
                : Array.Empty<int>();
        }

        private ProjectionCalibrationTable EnsureRuntimeTable()
        {
            runtimeTable ??= new ProjectionCalibrationTable();

            if (!runtimeDirty)
            {
                return runtimeTable;
            }

            runtimeTable.Clear();
            foreach (var baseline in baselines)
            {
                runtimeTable.SetBaseline(
                    baseline.StreamMode,
                    baseline.GetDistancesSpan(),
                    baseline.GetCapturedAtUtc(),
                    baseline.GetNote());
            }

            runtimeDirty = false;
            return runtimeTable;
        }

        private static DateTime NormalizeTimestamp(DateTime timestamp)
        {
            return timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
        }

        [Serializable]
        private struct SerializedBaseline
        {
            [SerializeField]
            private UamStreamMode streamMode;

            [SerializeField]
            private int[] distances;

            [SerializeField]
            private long capturedAtTicks;

            [SerializeField]
            private string note;

            public UamStreamMode StreamMode => streamMode;

            public ReadOnlySpan<int> GetDistancesSpan()
            {
                return distances ?? Array.Empty<int>();
            }

            public DateTime GetCapturedAtUtc()
            {
                return capturedAtTicks == 0
                    ? DateTime.MinValue
                    : new DateTime(capturedAtTicks, DateTimeKind.Utc);
            }

            public string GetNote()
            {
                return note ?? string.Empty;
            }

            public SerializedBaseline With(ReadOnlySpan<int> source, DateTime capturedAtUtc, string? updatedNote)
            {
                return From(streamMode, source, capturedAtUtc, updatedNote);
            }

            public static SerializedBaseline From(UamStreamMode mode, ReadOnlySpan<int> source, DateTime capturedAtUtc, string? note)
            {
                return new SerializedBaseline
                {
                    streamMode = mode,
                    distances = source.ToArray(),
                    capturedAtTicks = capturedAtUtc.Ticks,
                    note = note ?? string.Empty,
                };
            }
        }
    }
}
