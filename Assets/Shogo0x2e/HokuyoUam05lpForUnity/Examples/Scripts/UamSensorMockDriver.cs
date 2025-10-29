using System;
using System.Collections;
using System.Reflection;
using Shogo0x2e.HokuyoUam05lpForUnity.Internal;
using UnityEngine;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Examples
{
    /// <summary>
    /// Play Mode 用の簡易モック。ネットワーク接続なしで <see cref="UamSensor"/> に AR フレームを注入し、
    /// <see cref="UamSensor.OnScan"/> / <see cref="UamSensor.OnPositionDetected"/> の挙動を確認できる。
    /// </summary>
    [AddComponentMenu("Hokuyo UAM/UAM Sensor Mock Driver")]
    public sealed class UamSensorMockDriver : MonoBehaviour
    {
        [Header("Target Sensor")]
        public UamSensor? Sensor;

        [Header("Scan Parameters")]
        [Tooltip("生成するストリームモード。方向テーブルとビーム数を決定します。")]
        public UamStreamMode StreamMode = UamStreamMode.Standard;
        [Tooltip("1 フレームあたりのタイムスタンプ増分 (μs 相当)。")]
        public uint TimestampStep = 2500;
        [Tooltip("1 フレームあたりの送出間隔 (秒)。")]
        public float EmitIntervalSeconds = 0.05f;
        [Tooltip("基準距離 (メートル)。全ビームをこの距離で初期化します。")]
        public float BaselineDistanceMeters = 4.0f;
        [Tooltip("ターゲット距離 (メートル)。ハイライトビームに設定されます。")]
        public float TargetDistanceMeters = 1.2f;
        [Tooltip("ハイライトされるビーム幅 (ビーム数)。")]
        public int TargetBeamWidth = 8;
        [Tooltip("Sweep 周期 (秒)。0 以下の場合は Static Beam Index を使用して固定ビームをハイライトします。")]
        public float SweepPeriodSeconds = 2.5f;
        [Tooltip("Sweep 無効時にハイライトするビーム番号。")]
        public int StaticBeamIndex = 540;

        [Header("Pattern")]
        [Tooltip("ハイライトされたビームに加算する距離オフセット (メートル)。配列は必要に応じて循環使用します。短い値を混ぜると ROI 内で最短ビームが選ばれやすくなります。")]
        public float[] HighlightOffsetsMeters = new float[] { 0.06f, 0.02f, -0.04f, 0.03f, -0.06f };

        [Tooltip("各ハイライトビームへ加えるランダム揺らぎ (メートル)。0 で揺らぎなし。")]
        [Min(0f)]
        public float HighlightJitterMeters = 0.01f;

        [Header("Intensity (Optional)")]
        public bool EmitIntensity;
        [Tooltip("基準強度値。")]
        public ushort BaselineIntensity = 80;
        [Tooltip("ターゲット強度値。")]
        public ushort TargetIntensity = 400;

        [Header("Lifecycle")]
        public bool AutoStart = true;

        private Action<UamFrame>? _handleFrame;
        private Coroutine? _loop;
        private uint _timestamp;
        private bool _runInBackgroundWasSet;
        private bool _previousRunInBackground;

        private void Awake()
        {
            Sensor ??= GetComponent<UamSensor>();
        }

        private void OnEnable()
        {
            if (!Application.runInBackground)
            {
                _previousRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
                _runInBackgroundWasSet = true;
            }

            if (AutoStart)
            {
                StartMock();
            }
        }

        private void OnDisable()
        {
            StopMock();

            if (_runInBackgroundWasSet)
            {
                Application.runInBackground = _previousRunInBackground;
                _runInBackgroundWasSet = false;
            }
        }

        public void StartMock()
        {
            if (_loop != null)
            {
                return;
            }

            if (!EnsureHandleBound())
            {
                Debug.LogWarning("[UamSensorMockDriver] UamSensor が見つからないためモックを開始できません。", this);
                return;
            }

            _loop = StartCoroutine(EmitLoop());
        }

        public void StopMock()
        {
            if (_loop is not null)
            {
                StopCoroutine(_loop);
                _loop = null;
            }
        }

        private IEnumerator EmitLoop()
        {
            var wait = new WaitForSeconds(Mathf.Max(EmitIntervalSeconds, 0.01f));

            while (enabled)
            {
                if (!EnsureHandleBound())
                {
                    yield return null;
                    continue;
                }

                EmitSingleFrame();
                yield return wait;
            }
        }

        private void EmitSingleFrame()
        {
            var sensor = Sensor;
            if (sensor is null || _handleFrame is null)
            {
                return;
            }

            var directions = UamAngleTable.GetDirections(StreamMode);
            int beamCount = directions.Length;
            if (beamCount == 0)
            {
                return;
            }

            int highlightIndex = ResolveHighlightIndex(beamCount);
            int halfWidth = Mathf.Clamp(TargetBeamWidth, 1, beamCount) / 2;

            ushort baseline = (ushort)Mathf.Clamp(Mathf.RoundToInt(BaselineDistanceMeters * 1000f), 1, 65534);
            ushort target = (ushort)Mathf.Clamp(Mathf.RoundToInt(TargetDistanceMeters * 1000f), 1, 65534);

            var distances = new ushort[beamCount];
            var intensities = EmitIntensity ? new ushort[beamCount] : null;

            for (int i = 0; i < beamCount; ++i)
            {
                distances[i] = baseline;
                if (intensities is not null)
                {
                    intensities[i] = BaselineIntensity;
                }
            }

            for (int offset = -halfWidth; offset <= halfWidth; ++offset)
            {
                int index = (highlightIndex + offset + beamCount) % beamCount;

                float distanceMeters = TargetDistanceMeters;
                if (HighlightOffsetsMeters != null && HighlightOffsetsMeters.Length > 0)
                {
                    int patternIndex = (offset + halfWidth) % HighlightOffsetsMeters.Length;
                    distanceMeters += HighlightOffsetsMeters[patternIndex];
                }

                if (HighlightJitterMeters > 0f)
                {
                    float jitter = UnityEngine.Random.Range(-HighlightJitterMeters, HighlightJitterMeters);
                    distanceMeters += jitter;
                }

                distanceMeters = Mathf.Clamp(distanceMeters, 0.05f, BaselineDistanceMeters);

                ushort customDistance = (ushort)Mathf.Clamp(Mathf.RoundToInt(distanceMeters * 1000f), 1, 65534);
                distances[index] = customDistance;

                if (intensities is not null)
                {
                    intensities[index] = TargetIntensity;
                }
            }

            uint timestamp = _timestamp;
            _timestamp += TimestampStep;

            var scan = new PolarScan(distances, intensities, timestamp, StreamMode);
            var frame = new UamFrame
            {
                Header = "AR",
                SubHeader = "02",
                Status = "00",
                OperatingMode = 0x02,
                AreaNumber = 1,
                ErrorState = 0,
                LockoutState = 0,
                Ossd1 = 1,
                Ossd2 = 1,
                Ossd3 = 1,
                Ossd4 = 1,
                EncoderSpeed = 0,
                Timestamp = timestamp,
                Scan = scan,
            };

            _handleFrame.Invoke(frame);
        }

        private bool EnsureHandleBound()
        {
            if (Sensor is null)
            {
                return false;
            }

            if (_handleFrame is { Target: UamSensor bound } && bound == Sensor)
            {
                return true;
            }

            var method = typeof(UamSensor).GetMethod("HandleFrame", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method is null)
            {
                Debug.LogError("[UamSensorMockDriver] UamSensor.HandleFrame が見つかりません。実装が変更された可能性があります。", this);
                return false;
            }

            try
            {
                _handleFrame = (Action<UamFrame>)Delegate.CreateDelegate(typeof(Action<UamFrame>), Sensor, method, throwOnBindFailure: false);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
                _handleFrame = null;
            }

            return _handleFrame is not null;
        }

        private int ResolveHighlightIndex(int beamCount)
        {
            if (SweepPeriodSeconds <= 0.0f)
            {
                return Mathf.Clamp(StaticBeamIndex, 0, beamCount - 1);
            }

            float normalized = Mathf.Repeat(Time.time / SweepPeriodSeconds, 1f);
            int index = Mathf.RoundToInt(normalized * (beamCount - 1));
            return Mathf.Clamp(index, 0, beamCount - 1);
        }
    }
}
