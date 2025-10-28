using System;
using System.Linq;
using Shogo0x2e.HokuyoUam05lpForUnity;
using Shogo0x2e.HokuyoUam05lpForUnity.Projection;
using Xunit;

namespace HokuyoCore.Tests
{
    public class ProjectionCalibrationTableTests
    {
        [Fact]
        public void SetBaseline_StoresCopyAndNormalizesTimestamp()
        {
            var table = new ProjectionCalibrationTable();
            var source = new[] { 4000, 4100, 4200 };
            var captured = new DateTime(2025, 10, 26, 12, 30, 0, DateTimeKind.Local);

            table.SetBaseline(UamStreamMode.Standard, source, captured, "baseline");
            source[0] = 9999; // 改変しても保存内容は変わらない

            Assert.True(table.TryGetBaseline(UamStreamMode.Standard, out var stored, out var storedTimestamp, out var note));
            Assert.Equal(new[] { 4000, 4100, 4200 }, stored.ToArray());
            Assert.Equal(captured.ToUniversalTime(), storedTimestamp);
            Assert.Equal("baseline", note);
        }

        [Fact]
        public void SetBaseline_OverwritesExistingEntry()
        {
            var table = new ProjectionCalibrationTable();
            table.SetBaseline(UamStreamMode.Standard, new[] { 100, 200 }, DateTime.UtcNow, "initial");

            var updated = new[] { 150, 250 };
            table.SetBaseline(UamStreamMode.Standard, updated, DateTime.UtcNow.AddMinutes(5), "updated");

            Assert.True(table.TryGetBaseline(UamStreamMode.Standard, out var stored, out _, out var note));
            Assert.Equal(new[] { 150, 250 }, stored.ToArray());
            Assert.Equal("updated", note);
        }

        [Fact]
        public void TryGetBaseline_ReturnsFalse_WhenNotRegistered()
        {
            var table = new ProjectionCalibrationTable();

            Assert.False(table.TryGetBaseline(UamStreamMode.HighResolution, out var stored, out var timestamp, out var note));
            Assert.Equal(0, stored.Length);
            Assert.Equal(DateTime.MinValue, timestamp);
            Assert.Equal(string.Empty, note);
        }
    }
}
