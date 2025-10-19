using System;
using UnityEngine;

namespace Shogo0x2e.HokuyoUam05lpForUnity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class UamPointCloudVisualizer : MonoBehaviour
    {
        [Header("Source")]
        public UamSensor Sensor;
        public Transform SensorOrigin;

        [Header("Display")]
        public bool DrawRays = true;
        public bool DrawPoints = true;
        public Color RayColor = new(0f, 0.7f, 1f, 0.25f);
        public Color PointColor = new(1f, 0.45f, 0f, 0.9f);
        public float PointRadius = 0.03f;
        public float MaxVisualizedDistance = 0f;

        private readonly object _gate = new();
        private Vector2[] _sensorPoints = Array.Empty<Vector2>();
        private int _sensorCount;
        private int _lastFrameSequence;

        private Vector3[] _worldCache = Array.Empty<Vector3>();
        private int _worldCount;
        private int _cachedSequence;

        private void OnEnable()
        {
            if (Sensor == null)
            {
                Sensor = GetComponent<UamSensor>();
            }

            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (Sensor == null)
            {
                return;
            }

            Sensor.OnPositionDetected += HandlePoints;
        }

        private void Unsubscribe()
        {
            if (Sensor == null)
            {
                return;
            }

            Sensor.OnPositionDetected -= HandlePoints;
        }

        private void HandlePoints(Vector2[] source)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            lock (_gate)
            {
                EnsureSensorBuffer(source.Length);

                int count = 0;
                for (int i = 0; i < source.Length; ++i)
                {
                    var pt = source[i];
                    if (float.IsNaN(pt.x) || float.IsNaN(pt.y))
                    {
                        continue;
                    }

                    _sensorPoints[count++] = pt;
                }

                _sensorCount = count;
                _lastFrameSequence++;
            }
        }

        private void EnsureSensorBuffer(int length)
        {
            if (_sensorPoints.Length >= length)
            {
                return;
            }

            Array.Resize(ref _sensorPoints, length);
        }

        private void EnsureWorldCache(int length)
        {
            if (_worldCache.Length >= length)
            {
                return;
            }

            Array.Resize(ref _worldCache, length);
        }

        private Transform ResolveOrigin()
        {
            if (SensorOrigin != null)
            {
                return SensorOrigin;
            }

            if (Sensor != null)
            {
                return Sensor.transform;
            }

            return transform;
        }

        private void LateUpdate()
        {
            // keep cached world positions in sync once per frame for better Scene/Game view performance
            UpdateWorldCache();
        }

        private void UpdateWorldCache()
        {
            Transform origin = ResolveOrigin();
            Vector3 originPosition = origin.position;
            Quaternion originRotation = origin.rotation;

            lock (_gate)
            {
                if (_cachedSequence == _lastFrameSequence)
                {
                    return;
                }

                EnsureWorldCache(_sensorCount);

                Matrix4x4 matrix = Matrix4x4.TRS(originPosition, originRotation, Vector3.one);
                int count = _sensorCount;
                float maxDistance = MaxVisualizedDistance > 0f ? MaxVisualizedDistance : float.MaxValue;

                int written = 0;
                for (int i = 0; i < count; ++i)
                {
                    var pt = _sensorPoints[i];
                    float distance = pt.magnitude;
                    if (distance > maxDistance)
                    {
                        continue;
                    }

                    Vector3 local = new(pt.x, 0f, pt.y);
                    _worldCache[written++] = matrix.MultiplyPoint3x4(local);
                }

                _worldCount = written;
                _cachedSequence = _lastFrameSequence;
            }
        }

        private void OnDrawGizmos()
        {
            UpdateWorldCache();

            lock (_gate)
            {
                int count = _worldCount;
                if (count == 0)
                {
                    return;
                }

                Transform origin = ResolveOrigin();
                Vector3 originPos = origin.position;

                if (DrawRays)
                {
                    Gizmos.color = RayColor;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3 world = _worldCache[i];
                        Gizmos.DrawLine(originPos, world);
                    }
                }

                if (DrawPoints)
                {
                    Gizmos.color = PointColor;
                    float radius = Mathf.Max(0.001f, PointRadius);
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3 world = _worldCache[i];
                        Gizmos.DrawSphere(world, radius);
                    }
                }
            }
        }
    }
}
