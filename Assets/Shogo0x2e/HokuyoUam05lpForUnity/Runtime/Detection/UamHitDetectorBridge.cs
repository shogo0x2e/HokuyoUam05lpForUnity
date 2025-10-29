using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Detection
{
    /// <summary>
    /// Bridges raw scans from <see cref="UamSensor"/> into ROI-filtered hit observations for Unity-side visualization.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Hokuyo UAM/Hit Detector Bridge")]
    public sealed class UamHitDetectorBridge : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField]
        private UamSensor? sensor;

        [SerializeField]
        private ProjectionSurface? roiSurface;

        [Tooltip("通常はセンサ本体の Transform を指定。未設定時は UamSensor の Transform を利用します。")]
        [SerializeField]
        private Transform? sensorOrigin;

        [Header("Detection")]
        [Min(0f)]
        [SerializeField]
        private float minDistanceMeters = 0.05f;

        [Tooltip("0 の場合は上限なし。")]
        [Min(0f)]
        [SerializeField]
        private float maxDistanceMeters = 0f;

        [SerializeField]
        private bool rejectZeroDistance = true;

        [Header("Debug Output")]
        [SerializeField]
        private bool logDetections;

        [SerializeField]
        private bool drawGizmos = true;

        [SerializeField]
        private Color gizmoColor = new(1f, 0.25f, 0.35f, 0.5f);

        [SerializeField]
        private float gizmoPointRadius = 0.04f;

        [Serializable]
        public sealed class HitObservationEvent : UnityEvent<List<HitObservation>>
        {
        }

        [SerializeField]
        private HitObservationEvent onDetections = new();

        private readonly HitDetector detector = new();
        private readonly List<HitDetection> detectionBuffer = new(128);
        private readonly List<HitObservation> workingObservations = new(128);
        private readonly List<HitObservation> latestObservations = new(128);
        private readonly List<HitObservation> eventObservations = new(128);
        private readonly object gate = new();

        private uint lastTimestamp;
        private UamStreamMode lastStreamMode = UamStreamMode.Standard;

        public HitObservationEvent OnDetections => onDetections;

        /// <summary>
        /// Gets a snapshot of the most recent observations. The destination list will be cleared before filling.
        /// </summary>
        public void CopyLatestObservations(List<HitObservation> destination)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            lock (gate)
            {
                destination.Clear();
                destination.AddRange(latestObservations);
            }
        }

        private void Awake()
        {
            SyncDetectorSettings();
        }

        private void Reset()
        {
            if (sensor == null)
            {
                sensor = GetComponent<UamSensor>();
            }

            if (roiSurface == null)
            {
                roiSurface = GetComponentInChildren<ProjectionSurface>();
            }
        }

        private void OnValidate()
        {
            minDistanceMeters = Mathf.Max(0f, minDistanceMeters);
            maxDistanceMeters = Mathf.Max(0f, maxDistanceMeters);
            SyncDetectorSettings();
        }

        private void OnEnable()
        {
            if (sensor == null)
            {
                sensor = GetComponent<UamSensor>();
            }

            Subscribe();

            if (sensor != null && !sensor.DispatchEventsOnUnityThread)
            {
                Debug.LogWarning("[UamHitDetectorBridge] UamSensor.DispatchEventsOnUnityThread is false. Hit detection must run on the main thread for Unity API access.", this);
            }
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
            if (sensor == null)
            {
                return;
            }

            sensor.OnScan += HandleScan;
        }

        private void Unsubscribe()
        {
            if (sensor == null)
            {
                return;
            }

            sensor.OnScan -= HandleScan;
        }

        private void HandleScan(IPolarScan scan)
        {
            Transform? origin = ResolveSensorTransform();
            Func<Vector2, bool>? roiPredicate = BuildRoiPredicate(origin);

            detector.Detect(scan, roiPredicate, detectionBuffer);

            if (logDetections)
            {
                if (roiPredicate is null)
                {
                    Debug.Log("[UamHitDetectorBridge] ROI predicate is null - using full scan.", this);
                }

                Debug.Log($"[UamHitDetectorBridge] Detect() completed. hits={detectionBuffer.Count}, beamCount={scan.BeamCount}, timestamp={scan.Timestamp}", this);
            }

            if (origin == null)
            {
                // Without a transform we cannot project into world space, but we still keep step info for logging.
                if (logDetections && detectionBuffer.Count == 0)
                {
                    Debug.Log("[UamHitDetectorBridge] Origin is null and no detections were produced.", this);
                }

                CopyDetectionsWithoutWorld(scan);
                return;
            }

            Matrix4x4 localToWorld = origin.localToWorldMatrix;

            lock (gate)
            {
                EnsureObservationCapacity(workingObservations, detectionBuffer.Count);
                workingObservations.Clear();

                for (int i = 0; i < detectionBuffer.Count; ++i)
                {
                    var detection = detectionBuffer[i];
                    Vector3 world = localToWorld.MultiplyPoint3x4(detection.SensorPoint3);
                    workingObservations.Add(new HitObservation(detection, world));
                }

                EnsureObservationCapacity(latestObservations, workingObservations.Count);
                latestObservations.Clear();
                latestObservations.AddRange(workingObservations);

                EnsureObservationCapacity(eventObservations, workingObservations.Count);
                eventObservations.Clear();
                eventObservations.AddRange(workingObservations);

                lastTimestamp = scan.Timestamp;
                lastStreamMode = scan.StreamMode;
            }

            if (logDetections && eventObservations.Count > 0)
            {
                LogDetections();
            }
            else if (logDetections && detectionBuffer.Count > 0)
            {
                Debug.Log($"[UamHitDetectorBridge] {detectionBuffer.Count} detections available but origin missing; world points set to zero.", this);
            }
            else if (logDetections && detectionBuffer.Count == 0)
            {
                Debug.Log("[UamHitDetectorBridge] No ROI hits this frame.", this);
            }

            if (eventObservations.Count > 0)
            {
                onDetections.Invoke(eventObservations);
            }
        }

        private void CopyDetectionsWithoutWorld(IPolarScan scan)
        {
            lock (gate)
            {
                EnsureObservationCapacity(latestObservations, detectionBuffer.Count);
                latestObservations.Clear();
                EnsureObservationCapacity(eventObservations, detectionBuffer.Count);
                eventObservations.Clear();

                foreach (var detection in detectionBuffer)
                {
                    latestObservations.Add(new HitObservation(detection, Vector3.zero));
                    eventObservations.Add(new HitObservation(detection, Vector3.zero));
                }

                lastTimestamp = scan.Timestamp;
                lastStreamMode = scan.StreamMode;
            }

            if (logDetections && eventObservations.Count > 0)
            {
                LogDetections();
            }

            if (eventObservations.Count > 0)
            {
                onDetections.Invoke(eventObservations);
            }
        }

        private Transform? ResolveSensorTransform()
        {
            if (sensorOrigin != null)
            {
                return sensorOrigin;
            }

            if (sensor != null)
            {
                return sensor.transform;
            }

            return transform;
        }

        private Func<Vector2, bool>? BuildRoiPredicate(Transform? origin)
        {
            if (roiSurface == null || origin == null)
            {
                return null;
            }

            return roiSurface.MakeSensorLocalRoiPredicate(origin);
        }

        private void EnsureObservationCapacity(List<HitObservation> list, int required)
        {
            if (list.Capacity < required)
            {
                list.Capacity = required;
            }
        }

        private void SyncDetectorSettings()
        {
            detector.MinDistanceMeters = minDistanceMeters;
            detector.MaxDistanceMeters = maxDistanceMeters;
            detector.RejectZeroDistance = rejectZeroDistance;
        }

        private void LogDetections()
        {
            var buffer = eventObservations;
            var stepIndices = new int[buffer.Count];
            Span<Vector3> worldPoints = buffer.Count <= 32 ? stackalloc Vector3[buffer.Count] : new Vector3[buffer.Count];

            for (int i = 0; i < buffer.Count; ++i)
            {
                var observation = buffer[i];
                stepIndices[i] = observation.StepIndex;
                worldPoints[i] = observation.WorldPoint;
            }

            var sb = new StringBuilder();
            sb.Append("[UamHitDetectorBridge] Hits=").Append(buffer.Count)
                .Append(", Steps=[").Append(string.Join(",", stepIndices)).Append(']')
              .Append(", World=[");

            for (int i = 0; i < buffer.Count; ++i)
            {
                var p = worldPoints[i];
                sb.AppendFormat("( {0:F3}, {1:F3}, {2:F3} )", p.x, p.y, p.z);
                if (i < buffer.Count - 1)
                {
                    sb.Append(", ");
                }
            }

            sb.Append("] (timestamp=").Append(lastTimestamp)
              .Append(", mode=").Append(lastStreamMode).Append(')');

            Debug.Log(sb.ToString(), this);
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos)
            {
                return;
            }

            lock (gate)
            {
                if (latestObservations.Count == 0)
                {
                    return;
                }

                Transform? origin = ResolveSensorTransform();
                Vector3 originPosition = origin != null ? origin.position : transform.position;

                Gizmos.color = gizmoColor;
                foreach (var hit in latestObservations)
                {
                    Vector3 world = hit.WorldPoint;
                    if (world == Vector3.zero && origin == null)
                    {
                        continue;
                    }

                    Gizmos.DrawLine(originPosition, world);
                    Gizmos.DrawSphere(world, Mathf.Max(0.001f, gizmoPointRadius));
                }
            }
        }
    }
}
