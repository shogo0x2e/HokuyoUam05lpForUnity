using System;
using System.Collections.Generic;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Projection
{
    /// <summary>
    /// In-memory representation of projection surface calibration data.
    /// </summary>
    public sealed class ProjectionCalibrationTable
    {
        private readonly List<Entry> entries;

        public ProjectionCalibrationTable()
            : this(Array.Empty<Entry>())
        {
        }

        public ProjectionCalibrationTable(IEnumerable<Entry> existing)
        {
            entries = existing is null ? new List<Entry>() : new List<Entry>(existing);
        }

        public IReadOnlyList<Entry> Entries => entries;

        public void Clear()
        {
            entries.Clear();
        }

        public bool TryGetBaseline(UamStreamMode streamMode, out ReadOnlyMemory<int> distances, out DateTime capturedAtUtc, out string note)
        {
            foreach (var entry in entries)
            {
                if (entry.StreamMode == streamMode)
                {
                    distances = entry.GetDistances();
                    capturedAtUtc = entry.CapturedAtUtc;
                    note = entry.Note;
                    return true;
                }
            }

            distances = ReadOnlyMemory<int>.Empty;
            capturedAtUtc = DateTime.MinValue;
            note = string.Empty;
            return false;
        }

        public void SetBaseline(UamStreamMode streamMode, ReadOnlySpan<int> distances, DateTime capturedAtUtcUtc, string? note = null)
        {
            if (distances.Length == 0)
            {
                throw new ArgumentException("距離配列が空です。", nameof(distances));
            }

            var normalizedTimestamp = NormalizeTimestamp(capturedAtUtcUtc);

            for (int i = 0; i < entries.Count; ++i)
            {
                if (entries[i].StreamMode == streamMode)
                {
                    entries[i] = entries[i].With(distances, normalizedTimestamp, note);
                    return;
                }
            }

            entries.Add(Entry.Create(streamMode, distances, normalizedTimestamp, note));
        }

        private static DateTime NormalizeTimestamp(DateTime timestamp)
        {
            return timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
        }

        /// <summary>
        /// Serializable baseline entry. Distances are stored in millimetres.
        /// </summary>
        [Serializable]
        public struct Entry
        {
            public Entry(UamStreamMode mode, int[] distances, DateTime capturedAtUtc, string note)
            {
                StreamMode = mode;
                Distances = distances is null ? Array.Empty<int>() : (int[])distances.Clone();
                CapturedAtUtc = capturedAtUtc.Kind == DateTimeKind.Utc ? capturedAtUtc : capturedAtUtc.ToUniversalTime();
                Note = note ?? string.Empty;
            }

            public UamStreamMode StreamMode { get; private set; }
            public int[] Distances { get; private set; }
            public DateTime CapturedAtUtc { get; private set; }
            public string Note { get; private set; }

            public ReadOnlyMemory<int> GetDistances()
            {
                return Distances ?? Array.Empty<int>();
            }

            public Entry With(ReadOnlySpan<int> source, DateTime capturedAtUtc, string? note)
            {
                var updated = this;
                updated.Apply(source, capturedAtUtc, note);
                return updated;
            }

            public static Entry Create(UamStreamMode mode, ReadOnlySpan<int> source, DateTime capturedAtUtc, string? note)
            {
                return new Entry(mode, source.ToArray(), capturedAtUtc, note ?? string.Empty);
            }

            private void Apply(ReadOnlySpan<int> source, DateTime capturedAtUtc, string? note)
            {
                Distances = source.ToArray();
                CapturedAtUtc = capturedAtUtc.Kind == DateTimeKind.Utc ? capturedAtUtc : capturedAtUtc.ToUniversalTime();
                Note = note ?? string.Empty;
            }
        }
    }
}
