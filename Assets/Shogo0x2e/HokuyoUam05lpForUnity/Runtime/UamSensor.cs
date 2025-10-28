using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Shogo0x2e.HokuyoUam05lpForUnity.Internal;
using UnityEngine;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity
{
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
        [Tooltip("Unity メインスレッドにイベントをディスパッチする (推奨)。オフにするとバックグラウンドスレッドから直接呼び出されます。")]
        public bool DispatchEventsOnUnityThread = true;

        public Action? OnConnected;
        public Action? OnDisconnected;
        public Action<Exception>? OnError;
        public Action<UamStatus>? OnStatusChanged;
        public Action<IPolarScan>? OnScan;
        public Action<Vector2[]>? OnPositionDetected;

        private static readonly Vector2 InvalidPoint = new(float.NaN, float.NaN);

        private readonly object _clientGate = new();
        private Func<UamClient> _clientFactory = static () => new UamClient();
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

                var client = _clientFactory();
                if (client is null)
                {
                    throw new InvalidOperationException("UamClient factory returned null.");
                }
                client.SetStreamMode(mode);
                client.ResetEndpoint(Ip, Port);

                client.OnConnected = () => DispatchEvent(() =>
                {
                    if (VerboseLogging)
                    {
                        Debug.Log("[UamSensor] Connected", this);
                    }

                    OnConnected?.Invoke();
                });

                client.OnDisconnected = () => DispatchEvent(() =>
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

                client.OnError = ex => DispatchEvent(() =>
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

        internal void SetClientFactory(Func<UamClient> factory)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            lock (_clientGate)
            {
                if (_client is not null)
                {
                    throw new InvalidOperationException("Cannot change client factory while the sensor is running.");
                }

                _clientFactory = factory;
            }
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
            var scan = frame.Scan;
            if (scan is null || scan.BeamCount == 0)
            {
                return;
            }

            var streamMode = scan.StreamMode;
            var directions = UamAngleTable.GetDirections(streamMode);
            int expected = directions.Length;
            var distances = scan.Distances.Span;

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
                streamMode);

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

            var cartesianHandler = OnPositionDetected;
            Vector2[]? cartesianBuffer = null;
            if (cartesianHandler is not null)
            {
                if (expected == 0)
                {
                    cartesianBuffer = Array.Empty<Vector2>();
                }
                else
                {
                    cartesianBuffer = RentBuffer(expected);
                    Array.Fill(cartesianBuffer, InvalidPoint);

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
                        cartesianBuffer[i] = dir * meters;
                    }
                }
            }

            var scanHandler = OnScan;

            DispatchEvent(() =>
            {
                if (statusChanged)
                {
                    OnStatusChanged?.Invoke(status);
                }

                scanHandler?.Invoke(scan);

                if (cartesianBuffer is not null)
                {
                    try
                    {
                        cartesianHandler?.Invoke(cartesianBuffer);
                    }
                    finally
                    {
                        ReturnBuffer(cartesianBuffer);
                    }
                }
            });
        }

        private void DispatchEvent(Action action)
        {
            if (DispatchEventsOnUnityThread)
            {
                if (_unityContext is not null && SynchronizationContext.Current == _unityContext)
                {
                    action();
                    return;
                }

                _mainThreadActions.Enqueue(action);
                return;
            }

            action();
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
