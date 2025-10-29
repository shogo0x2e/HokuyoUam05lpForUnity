using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Shogo0x2e.HokuyoUam05lpForUnity.Detection;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Examples
{
    /// <summary>
    /// Spawns pooled prefabs at detected hit locations for quick spatial debugging in the Scene/Game view.
    /// </summary>
    [AddComponentMenu("Hokuyo UAM/Hit Prefab Visualizer")]
    public sealed class HitPrefabVisualizer : MonoBehaviour
    {
        [Header("Source")]
        public UamHitDetectorBridge? Bridge;

        [Header("Visuals")]
        public GameObject? Prefab;
        [Tooltip("Fallback sphere radius (meters) when Prefab is not assigned.")]
        [Min(0f)]
        public float FallbackSphereRadius = 0.05f;
        [Tooltip("表示したマーカーを維持する時間 (秒)。0 で即時消灯。")]
        [Min(0f)]
        public float MarkerLifetimeSeconds = 5f;
        [Tooltip("Update transform rotation so that the markers face up.")]
        public bool AlignUpAxis = true;
        [Tooltip("Assign step index to marker name for quick inspection in the Hierarchy window.")]
        public bool AnnotateNameWithStep = true;

        private sealed class MarkerState
        {
            public GameObject GameObject = null!;
            public float ExpireAt;
        }

        private readonly Dictionary<int, MarkerState> activeMarkers = new();
        private readonly Queue<GameObject> pooledMarkers = new();
        private readonly List<int> removalBuffer = new();
        private readonly UnityAction<List<HitObservation>> handleDetectionsAction;

        public HitPrefabVisualizer()
        {
            handleDetectionsAction = HandleDetections;
        }

        private void Awake()
        {
            Bridge ??= GetComponent<UamHitDetectorBridge>();
        }

        private void OnEnable()
        {
            if (Bridge == null)
            {
                Debug.LogWarning("[HitPrefabVisualizer] UamHitDetectorBridge が未割り当てです。", this);
                return;
            }

            Bridge.OnDetections.AddListener(handleDetectionsAction);
            Debug.Log("[HitPrefabVisualizer] Listener registered.", this);
        }

        private void OnDisable()
        {
            if (Bridge != null)
            {
                Bridge.OnDetections.RemoveListener(handleDetectionsAction);
            }

            ClearAllMarkers();
            Debug.Log("[HitPrefabVisualizer] Listener removed and markers cleared.", this);
        }

        private void Update()
        {
            CleanupExpiredMarkers();
        }

        private void HandleDetections(List<HitObservation> observations)
        {
            Debug.Log($"[HitPrefabVisualizer] HandleDetections count={observations.Count}", this);

            if (observations.Count == 0)
            {
                return;
            }

            float now = Time.time;
            float lifetime = Mathf.Max(0f, MarkerLifetimeSeconds);

            for (int i = 0; i < observations.Count; ++i)
            {
                var hit = observations[i];
                var state = GetOrCreateMarkerState(hit.StepIndex);
                GameObject marker = state.GameObject;

                if (!marker.activeSelf)
                {
                    marker.SetActive(true);
                }

                Transform markerTransform = marker.transform;
                markerTransform.position = hit.WorldPoint;
                if (AlignUpAxis)
                {
                    markerTransform.rotation = Quaternion.identity;
                }

                if (AnnotateNameWithStep)
                {
                    marker.name = BuildMarkerName(hit.StepIndex);
                }

                state.ExpireAt = now + lifetime;
            }

            CleanupExpiredMarkers();
        }

        private MarkerState GetOrCreateMarkerState(int stepIndex)
        {
            if (activeMarkers.TryGetValue(stepIndex, out var state))
            {
                Debug.Log($"[HitPrefabVisualizer] Updating existing marker for step={stepIndex}", this);
                return state;
            }

            GameObject marker = pooledMarkers.Count > 0 ? pooledMarkers.Dequeue() : CreateMarkerInstance();
            state = new MarkerState
            {
                GameObject = marker,
                ExpireAt = 0f,
            };

            activeMarkers[stepIndex] = state;
            marker.SetActive(true);
            Debug.Log($"[HitPrefabVisualizer] Activated marker for step={stepIndex}", this);
            return state;
        }

        private void CleanupExpiredMarkers()
        {
            if (activeMarkers.Count == 0)
            {
                return;
            }

            float now = Time.time;
            removalBuffer.Clear();

            foreach (var pair in activeMarkers)
            {
                if (pair.Value.ExpireAt > 0f && pair.Value.ExpireAt <= now)
                {
                    removalBuffer.Add(pair.Key);
                }
            }

            if (removalBuffer.Count == 0)
            {
                return;
            }

            for (int i = 0; i < removalBuffer.Count; ++i)
            {
                ReleaseMarker(removalBuffer[i]);
            }

            removalBuffer.Clear();
            Debug.Log("[HitPrefabVisualizer] Expired markers cleaned up.", this);
        }

        private void ReleaseMarker(int stepIndex)
        {
            if (!activeMarkers.TryGetValue(stepIndex, out var state))
            {
                return;
            }

            activeMarkers.Remove(stepIndex);

            var marker = state.GameObject;
            if (marker == null)
            {
                Debug.LogWarning($"[HitPrefabVisualizer] Marker state missing GameObject for step={stepIndex}", this);
                return;
            }

            marker.SetActive(false);
            pooledMarkers.Enqueue(marker);
            Debug.Log($"[HitPrefabVisualizer] Marker returned to pool step={stepIndex}", this);
        }

        private void ClearAllMarkers()
        {
            removalBuffer.Clear();
            foreach (var key in activeMarkers.Keys)
            {
                removalBuffer.Add(key);
            }

            for (int i = 0; i < removalBuffer.Count; ++i)
            {
                ReleaseMarker(removalBuffer[i]);
            }

            removalBuffer.Clear();
            Debug.Log("[HitPrefabVisualizer] Cleared all active markers.", this);
        }

        private GameObject CreateMarkerInstance()
        {
            GameObject instance;
            if (Prefab != null)
            {
                instance = Instantiate(Prefab, transform);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                instance.transform.SetParent(transform, false);
                float diameter = Mathf.Max(0.001f, FallbackSphereRadius) * 2f;
                instance.transform.localScale = Vector3.one * diameter;
                instance.name = "Hit Marker";

                var collider = instance.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(collider);
                    }
                    else
                    {
#if UNITY_EDITOR
                        DestroyImmediate(collider);
#else
                        Destroy(collider);
#endif
                    }
                }
            }

            instance.transform.SetParent(transform, false);
            instance.SetActive(false);
            Debug.Log("[HitPrefabVisualizer] Created new marker instance.", this);
            return instance;
        }

        private static string BuildMarkerName(int step)
        {
            return step >= 0 ? $"Hit Marker (step {step})" : "Hit Marker";
        }
    }
}
