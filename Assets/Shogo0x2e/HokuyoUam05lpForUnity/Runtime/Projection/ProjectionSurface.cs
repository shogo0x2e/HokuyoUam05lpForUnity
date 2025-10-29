using System;
using Shogo0x2e.HokuyoUam05lpForUnity.Projection;
using UnityEngine;
using UnityEngine.Serialization;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector2 = System.Numerics.Vector2;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity
{
    /// <summary>
    /// Represents a planar interaction area (e.g., projection screen) used for ROI filtering.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Hokuyo UAM/Projection Surface")]
    public sealed class ProjectionSurface : MonoBehaviour
    {
        private static readonly Color DefaultGizmoColor = new(0f, 0.6f, 1f, 0.25f);

        [Header("Surface Dimensions (meters)")]
        [Min(0f)]
        [SerializeField]
        private float width = 1.5f;

        [FormerlySerializedAs("height")]
        [Min(0f)]
        [SerializeField]
        private float forwardLength = 0.9f;

        [Tooltip("センサ平面に対する法線方向の許容距離 (両側合計)。0 の場合は完全に平面で判定します。")]
        [Min(0f)]
        [SerializeField]
        private float depthTolerance = 0.04f;

        [Header("Debug")]
        [SerializeField]
        private bool drawGizmos = true;

        [SerializeField]
        private Color gizmoColor = DefaultGizmoColor;

        public float Width => Mathf.Max(width, 0f);
        public float ForwardLength => Mathf.Max(forwardLength, 0f);
        public float DepthTolerance => Mathf.Max(depthTolerance, 0f);
        [Obsolete("Use ForwardLength instead.")]
        public float Height => ForwardLength;

        /// <summary>
        /// Builds an ROI predicate that evaluates sensor-local points against this surface.
        /// </summary>
        public Func<Vector2, bool> MakeSensorLocalRoiPredicate(Transform sensorTransform)
        {
            if (sensorTransform is null)
            {
                throw new ArgumentNullException(nameof(sensorTransform));
            }

            var descriptor = ProjectionSurfaceMath.CreateDescriptor(Width, ForwardLength, DepthTolerance);
            if (descriptor.IsEmpty)
            {
                return static _ => false;
            }

            var unityCombined = transform.worldToLocalMatrix * sensorTransform.localToWorldMatrix;
            var numericsMatrix = ToNumericsMatrix(unityCombined);
            return sensorPoint =>
            {
                var numericsPoint = new NumericsVector2(sensorPoint.x, sensorPoint.y);
                return ProjectionSurfaceMath.Contains(descriptor, numericsMatrix, numericsPoint);
            };
        }

        private static NumericsMatrix4x4 ToNumericsMatrix(UnityEngine.Matrix4x4 matrix)
        {
            // Unity の Matrix4x4 は列優先で平行移動が m03/m13/m23 に格納される。System.Numerics.Matrix4x4
            // は行優先で translation が M41/M42/M43 に入るため、列と行を入れ替えて詰め替える。
            return new NumericsMatrix4x4(
                matrix.m00, matrix.m10, matrix.m20, matrix.m30,
                matrix.m01, matrix.m11, matrix.m21, matrix.m31,
                matrix.m02, matrix.m12, matrix.m22, matrix.m32,
                matrix.m03, matrix.m13, matrix.m23, matrix.m33);
        }

        private void OnDrawGizmos()
        {
            DrawGizmosInternal(selected: false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawGizmosInternal(selected: true);
        }

        private void DrawGizmosInternal(bool selected)
        {
            if (!drawGizmos)
            {
                return;
            }

            var descriptor = ProjectionSurfaceMath.CreateDescriptor(Width, ForwardLength, DepthTolerance);
            if (descriptor.IsEmpty)
            {
                return;
            }

            var color = gizmoColor;
            if (selected)
            {
                color.a = Mathf.Clamp01(color.a + 0.15f);
            }

            Gizmos.color = color;
            Gizmos.matrix = transform.localToWorldMatrix;

            var size = new Vector3(
                descriptor.HalfWidth * 2f,
                Mathf.Max(descriptor.HalfDepth * 2f, 0.001f),
                descriptor.HalfHeight * 2f);
            Gizmos.DrawCube(Vector3.zero, size);
        }
    }
}
