using System.Threading;
using UnityEngine;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Examples
{
    /// <summary>
    /// <see cref="UamSensor"/> のイベントを Play Mode 上で可視化する簡易ロガー。
    /// </summary>
    [AddComponentMenu("Hokuyo UAM/UAM Sensor Event Logger")]
    public sealed class UamSensorEventLogger : MonoBehaviour
    {
        public UamSensor? Sensor;
        [Tooltip("ログ出力時に参照するビーム番号 (0 始まり)。")]
        public int SampleBeamIndex = 540;
        [Tooltip("OnScan をログ出力するかどうか。")]
        public bool LogOnScan = true;
        [Tooltip("OnPositionDetected をログ出力するかどうか。")]
        public bool LogOnPositionDetected = true;

        private void Awake()
        {
            Sensor ??= GetComponent<UamSensor>();
        }

        private void OnEnable()
        {
            if (Sensor is null)
            {
                Debug.LogWarning("[UamSensorEventLogger] UamSensor が未割り当てです。", this);
                return;
            }

            Sensor.OnScan += HandleScan;
            Sensor.OnPositionDetected += HandlePositions;
        }

        private void OnDisable()
        {
            if (Sensor is null)
            {
                return;
            }

            Sensor.OnScan -= HandleScan;
            Sensor.OnPositionDetected -= HandlePositions;
        }

        private void HandleScan(IPolarScan scan)
        {
            if (!LogOnScan)
            {
                return;
            }

            var distances = scan.Distances.Span;
            if (distances.Length == 0)
            {
                return;
            }

            int index = Mathf.Clamp(SampleBeamIndex, 0, distances.Length - 1);
            ushort distance = distances[index];
            Debug.Log($"[UamSensorEventLogger] OnScan timestamp={scan.Timestamp} beam[{index}]={distance}mm thread={Thread.CurrentThread.ManagedThreadId}", this);
        }

        private void HandlePositions(Vector2[] positions)
        {
            if (!LogOnPositionDetected)
            {
                return;
            }

            if (positions.Length == 0)
            {
                return;
            }

            int index = Mathf.Clamp(SampleBeamIndex, 0, positions.Length - 1);
            Vector2 sample = positions[index];
            Debug.Log($"[UamSensorEventLogger] OnPositionDetected pos[{index}]={sample} thread={Thread.CurrentThread.ManagedThreadId}", this);
        }
    }
}
