using System;
using System.Numerics;
using Shogo0x2e.HokuyoUam05lpForUnity.Projection;
using Xunit;

namespace HokuyoCore.Tests
{
    public class ProjectionSurfaceMathTests
    {
        [Fact]
        public void Contains_ReturnsTrue_ForPointInsideAlignedSurface()
        {
            var descriptor = ProjectionSurfaceMath.CreateDescriptor(width: 2f, height: 1f, depthTolerance: 0.2f);
            var surfaceLocalToWorld = Matrix4x4.Identity;
            surfaceLocalToWorld.Translation = new Vector3(0f, 0f, 3f);
            Matrix4x4.Invert(surfaceLocalToWorld, out var surfaceWorldToLocal);

            var sensorToSurface = Matrix4x4.Multiply(surfaceWorldToLocal, Matrix4x4.Identity);
            var point = new Vector2(0.3f, 3.0f);

            Assert.True(ProjectionSurfaceMath.Contains(descriptor, sensorToSurface, point));
        }

        [Fact]
        public void Contains_ReturnsFalse_WhenOutsideWidth()
        {
            var descriptor = ProjectionSurfaceMath.CreateDescriptor(width: 2f, height: 1f, depthTolerance: 0.2f);
            var surfaceLocalToWorld = Matrix4x4.Identity;
            surfaceLocalToWorld.Translation = new Vector3(0f, 0f, 3f);
            Matrix4x4.Invert(surfaceLocalToWorld, out var surfaceWorldToLocal);

            var sensorToSurface = Matrix4x4.Multiply(surfaceWorldToLocal, Matrix4x4.Identity);
            var point = new Vector2(1.2f, 3.0f); // 横方向が半幅 1.0f を超える

            Assert.False(ProjectionSurfaceMath.Contains(descriptor, sensorToSurface, point));
        }

        [Fact]
        public void Contains_RespectsDepthTolerance()
        {
            var descriptor = ProjectionSurfaceMath.CreateDescriptor(width: 2f, height: 1f, depthTolerance: 0.1f);
            var surfaceLocalToWorld = Matrix4x4.Identity;
            surfaceLocalToWorld.Translation = new Vector3(0f, 0f, 3f);
            Matrix4x4.Invert(surfaceLocalToWorld, out var surfaceWorldToLocal);

            var sensorToSurface = Matrix4x4.Multiply(surfaceWorldToLocal, Matrix4x4.Identity);
            var point = new Vector2(0f, 2.85f); // 深度差 0.15f > 許容 0.05f

            Assert.False(ProjectionSurfaceMath.Contains(descriptor, sensorToSurface, point));
        }

        [Fact]
        public void Contains_HandlesRotatedSurface()
        {
            var descriptor = ProjectionSurfaceMath.CreateDescriptor(width: 1.6f, height: 1.0f, depthTolerance: 0.2f);

            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 12f);
            var surfaceLocalToWorld = Matrix4x4.CreateFromQuaternion(rotation);
            surfaceLocalToWorld.Translation = new Vector3(0f, 0f, 3f);
            Matrix4x4.Invert(surfaceLocalToWorld, out var surfaceWorldToLocal);

            var sensorToSurface = Matrix4x4.Multiply(surfaceWorldToLocal, Matrix4x4.Identity);

            var surfacePointLocal = new Vector3(0.6f, 0f, 0f);
            var worldPoint = Vector3.Transform(surfacePointLocal, surfaceLocalToWorld);
            var sensorPoint = new Vector2(worldPoint.X, worldPoint.Z);

            Assert.True(ProjectionSurfaceMath.Contains(descriptor, sensorToSurface, sensorPoint));
        }
    }
}
