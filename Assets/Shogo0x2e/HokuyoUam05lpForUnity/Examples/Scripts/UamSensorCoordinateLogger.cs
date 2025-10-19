using System.Text;
using UnityEngine;

namespace Shogo0x2e.HokuyoUam05lpForUnity.Examples
{
    /// <summary>
    /// シンプルなデモ: センサから拾った XY[m] を一定周期でログ出力し、
    /// TransformPoint を使って Unity ワールド座標 (X,Y,Z) に変換する例。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UamSensorCoordinateLogger : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UamSensor sensor;
        [Tooltip("センサの基準 Transform。未設定なら sensor.transform を使用")]
        [SerializeField] private Transform sensorOrigin;

        [Header("Logging")]
        [Min(1)] public int SampleCount = 5;
        [Tooltip("何フレームごとにログを出すか")] [Min(1)] public int LogEveryNFrames = 15;
        public bool LogWorldCoordinates = true;
        public bool LogSensorCoordinates = false;

        [Header("Optional Auto Run")]
        public bool StartSensorOnEnable;
        public UamStreamMode StartMode = UamStreamMode.Standard;

        private int _frameCounter;

        private void Reset()
        {
            sensor = GetComponent<UamSensor>();
        }

        private void OnEnable()
        {
            if (sensor == null)
            {
                sensor = GetComponent<UamSensor>();
                if (sensor == null)
                {
                    Debug.LogError("UamSensor が見つかりません。インスペクタで割り当ててください。", this);
                    enabled = false;
                    return;
                }
            }

            sensor.OnConnected += HandleConnected;
            sensor.OnPositionDetected += HandlePositions;

            if (StartSensorOnEnable)
            {
                _ = sensor.StartSensorAsync(StartMode);
            }
        }

        private void OnDisable()
        {
            if (sensor != null)
            {
                sensor.OnConnected -= HandleConnected;
                sensor.OnPositionDetected -= HandlePositions;
            }
        }

        private void HandleConnected()
        {
            _frameCounter = 0;
            Debug.Log("[UamExample] Sensor connected", this);
        }

        private void HandlePositions(Vector2[] points)
        {
            _frameCounter++;
            if (_frameCounter % LogEveryNFrames != 0)
            {
                return;
            }

            var origin = sensorOrigin != null ? sensorOrigin : sensor != null ? sensor.transform : transform;

            var sb = new StringBuilder();
            sb.Append("[UamExample] frame=").Append(Time.frameCount)
              .Append(" samples=").Append(points.Length);

            int emitted = 0;
            for (int i = 0; i < points.Length && emitted < SampleCount; ++i)
            {
                var pt = points[i];
                if (float.IsNaN(pt.x) || float.IsNaN(pt.y))
                {
                    continue;
                }

                sb.Append(" | ");
                if (LogSensorCoordinates)
                {
                    sb.AppendFormat("sensor({0:F3},{1:F3})", pt.x, pt.y);
                }

                if (LogWorldCoordinates)
                {
                    Vector3 world = origin.TransformPoint(new Vector3(pt.x, 0f, pt.y));
                    if (LogSensorCoordinates)
                    {
                        sb.Append(" -> ");
                    }
                    sb.AppendFormat("world({0:F3},{1:F3},{2:F3})", world.x, world.y, world.z);
                }

                emitted++;
            }

            Debug.Log(sb.ToString(), this);
        }
    }
}
