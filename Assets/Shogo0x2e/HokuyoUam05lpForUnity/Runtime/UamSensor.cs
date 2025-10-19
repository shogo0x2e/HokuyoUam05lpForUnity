using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Shogo0x2e.HokuyoUam05lpForUnity.Internal;
using UnityEngine;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity
{
    public enum UamStreamMode
    {
        Standard,
        WithIntensity,
        HighResolution,
    }

    [DisallowMultipleComponent]
    public sealed class UamSensor : MonoBehaviour
    {
        private const float MilliMetersToMeters = 0.001f;

        [Header("Connection")]
        public string Ip = "192.168.0.10";
        public int Port = 10940;

        [Header("Behaviour")]
        public bool AutoReconnect = true;
        [Tooltip("コンポーネント有効化時に自動で StartSensorAsync を呼び出す")]
        public bool AutoStart = true;
        public UamStreamMode AutoStartMode = UamStreamMode.Standard;
        [Tooltip("接続/切断などの簡易ログを Unity Console に出す")]
        public bool VerboseLogging = true;

        public Action? OnConnected;
        public Action? OnDisconnected;
        public Action<Exception>? OnError;
        public Action<UamStatus>? OnStatusChanged;
        public Action<Vector2[]>? OnPositionDetected;

        private static readonly Vector2 InvalidPoint = new(float.NaN, float.NaN);

        private readonly object _clientGate = new();
        private readonly ConcurrentDictionary<int, ConcurrentQueue<Vector2[]>> _bufferPools = new();
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();

        private UamClient? _client;
        private SynchronizationContext? _unityContext;
        private UamStreamMode _currentMode = UamStreamMode.Standard;
        private bool _hasStatus;
        private UamStatus _lastStatus;

        private void Awake()
        {
            _unityContext = SynchronizationContext.Current;
        }

        private void OnEnable()
        {
            if (!AutoStart)
            {
                return;
            }

            if (VerboseLogging)
            {
                Debug.Log($"[UamSensor] AutoStart (mode={AutoStartMode})", this);
            }

            _ = StartSensorAsync(AutoStartMode);
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                action();
            }
        }

        private void OnDisable()
        {
            _ = StopSensorAsync();
        }

        private void OnDestroy()
        {
            _ = StopSensorAsync();
        }

        public Task StartSensorAsync(UamStreamMode mode)
        {
            if (VerboseLogging)
            {
                Debug.Log($"[UamSensor] Start requested ({Ip}:{Port}, mode={mode})", this);
            }

            lock (_clientGate)
            {
                _currentMode = mode;

                if (_client is { } existing)
                {
                    existing.SetStreamMode(mode);
                    TrimPoolsForMode(mode);
                    return Task.CompletedTask;
                }

                var client = new UamClient();
                client.SetStreamMode(mode);
                client.ResetEndpoint(Ip, Port);

                client.OnConnected = () => EnqueueMainThread(() =>
                {
                    if (VerboseLogging)
                    {
                        Debug.Log("[UamSensor] Connected", this);
                    }

                    OnConnected?.Invoke();
                });

                client.OnDisconnected = () => EnqueueMainThread(() =>
                {
                    if (VerboseLogging)
                    {
                        Debug.LogWarning("[UamSensor] Disconnected", this);
                    }

                    OnDisconnected?.Invoke();
                    if (!AutoReconnect)
                    {
                        _ = StopSensorAsync();
                    }
                });

                client.OnError = ex => EnqueueMainThread(() =>
                {
                    if (VerboseLogging)
                    {
                        Debug.LogError($"[UamSensor] Error: {ex.Message}", this);
                    }

                    OnError?.Invoke(ex);
                });

                client.OnFrame = HandleFrame;

                _client = client;
                TrimPoolsForMode(mode);
                client.Start();

                if (VerboseLogging)
                {
                    Debug.Log("[UamSensor] Connection loop started", this);
                }
            }

            return Task.CompletedTask;
        }

        public async Task StopSensorAsync()
        {
            UamClient? client;
            lock (_clientGate)
            {
                client = _client;
                _client = null;
            }

            if (client is not null)
            {
                client.OnFrame = null;
                client.OnConnected = null;
                client.OnDisconnected = null;
                client.OnError = null;
                await client.DisposeAsync().ConfigureAwait(false);
            }

            _hasStatus = false;
        }

        private void HandleFrame(UamFrame frame)
        {
            if (!frame.HasDistanceData)
            {
                return;
            }

            var mode = _currentMode;
            var directions = UamAngleTable.GetDirections(mode);
            int expected = directions.Length;
            var distances = frame.Distances;

            if (expected == 0)
            {
                return;
            }

            var buffer = RentBuffer(expected);
            Array.Fill(buffer, InvalidPoint);

            int copyCount = Math.Min(distances.Length, expected);
            for (int i = 0; i < copyCount; ++i)
            {
                ushort raw = distances[i];
                if (UamCodec.IsErrorDistance(raw) || raw == 0)
                {
                    continue;
                }

                float meters = raw * MilliMetersToMeters;
                var dir = directions[i];
                buffer[i] = dir * meters;
            }

            var status = new UamStatus(
                frame.OperatingMode,
                frame.AreaNumber,
                frame.ErrorState,
                frame.LockoutState,
                frame.Ossd1,
                frame.Ossd2,
                frame.Ossd3,
                frame.Ossd4,
                frame.EncoderSpeed,
                frame.Timestamp,
                mode);

            bool statusChanged;
            if (!_hasStatus)
            {
                statusChanged = true;
                _lastStatus = status;
                _hasStatus = true;
            }
            else
            {
                statusChanged = !_lastStatus.Equals(status);
                if (statusChanged)
                {
                    _lastStatus = status;
                }
            }

            EnqueueMainThread(() =>
            {
                if (statusChanged)
                {
                    OnStatusChanged?.Invoke(status);
                }

                try
                {
                    OnPositionDetected?.Invoke(buffer);
                }
                finally
                {
                    ReturnBuffer(buffer);
                }
            });
        }

        private void EnqueueMainThread(Action action)
        {
            if (_unityContext is not null && SynchronizationContext.Current == _unityContext)
            {
                action();
                return;
            }

            _mainThreadActions.Enqueue(action);
        }

        private Vector2[] RentBuffer(int length)
        {
            if (_bufferPools.TryGetValue(length, out var queue) && queue.TryDequeue(out var buffer))
            {
                return buffer;
            }

            return new Vector2[length];
        }

        private void ReturnBuffer(Vector2[] buffer)
        {
            if (buffer.Length == 0)
            {
                return;
            }

            var queue = _bufferPools.GetOrAdd(buffer.Length, _ => new ConcurrentQueue<Vector2[]>());
            queue.Enqueue(buffer);
        }

        private void TrimPoolsForMode(UamStreamMode mode)
        {
            int desiredLength = UamAngleTable.GetDirections(mode).Length;
            foreach (var kvp in _bufferPools)
            {
                if (kvp.Key != desiredLength && _bufferPools.TryRemove(kvp.Key, out var queue))
                {
                    while (queue.TryDequeue(out _))
                    {
                        // discard buffers of the wrong size
                    }
                }
            }
        }
    }
}
