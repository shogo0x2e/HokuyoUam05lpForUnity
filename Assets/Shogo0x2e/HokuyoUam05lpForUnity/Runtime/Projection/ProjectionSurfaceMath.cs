using System;
using System.Numerics;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Projection
{
    /// <summary>
    /// Axis-aligned half extents expressed in the projection surface's local coordinate space (X = lateral, Z = forward, Y = normal).
    /// </summary>
    public readonly struct ProjectionSurfaceDescriptor
    {
        public ProjectionSurfaceDescriptor(float halfWidth, float halfHeight, float halfDepth)
        {
            HalfWidth = MathF.Max(halfWidth, 0f);
            HalfHeight = MathF.Max(halfHeight, 0f);
            HalfDepth = MathF.Max(halfDepth, 0f);
        }

        public float HalfWidth { get; }
        public float HalfHeight { get; }
        public float HalfDepth { get; }

        public bool IsEmpty => HalfWidth <= 0f || HalfHeight <= 0f;
    }

    /// <summary>
    /// Math helpers shared between the Unity runtime and headless unit tests.
    /// </summary>
    public static class ProjectionSurfaceMath
    {
        /// <summary>
        /// Converts size parameters (full width/height/depth tolerance) into a descriptor that
        /// operates on half extents.
        /// </summary>
        public static ProjectionSurfaceDescriptor CreateDescriptor(float width, float height, float depthTolerance)
        {
            return new ProjectionSurfaceDescriptor(
                MathF.Max(width, 0f) * 0.5f,
                MathF.Max(height, 0f) * 0.5f,
                MathF.Max(depthTolerance, 0f) * 0.5f);
        }

        /// <summary>
        /// Checks whether a 2D point expressed in the sensor's local XZ plane lies within the projection surface
        /// after transforming it into the surface's local coordinate frame.
        /// </summary>
        /// <param name="descriptor">Half extents for the surface bounds and depth tolerance.</param>
        /// <param name="sensorToSurface">
        /// Affine matrix that converts points from sensor-local (x,0,z,1) into the surface's local space.
        /// </param>
        /// <param name="sensorPoint">Point on the sensor plane (x = lateral, y = forward).</param>
        /// <returns>True when the transformed point is inside the bounds and depth tolerance.</returns>
        public static bool Contains(in ProjectionSurfaceDescriptor descriptor, in Matrix4x4 sensorToSurface, in Vector2 sensorPoint)
        {
            if (descriptor.IsEmpty)
            {
                return false;
            }

            var sensorLocalPoint = new Vector3(sensorPoint.X, 0f, sensorPoint.Y);
            var surfaceLocalPoint = Vector3.Transform(sensorLocalPoint, sensorToSurface);

            return MathF.Abs(surfaceLocalPoint.X) <= descriptor.HalfWidth     // lateral (sensor X)
                && MathF.Abs(surfaceLocalPoint.Z) <= descriptor.HalfHeight    // forward (sensor Y)
                && MathF.Abs(surfaceLocalPoint.Y) <= descriptor.HalfDepth;    // normal (sensor up)
        }
    }
}
