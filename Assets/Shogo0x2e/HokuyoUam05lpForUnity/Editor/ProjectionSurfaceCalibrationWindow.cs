using System;
using System.IO;
using Shogo0x2e.HokuyoUam05lpForUnity.Internal;
using UnityEditor;
using UnityEngine;

#nullable enable

namespace Shogo0x2e.HokuyoUam05lpForUnity.Editor
{
    /// <summary>
    /// Editor tool that captures baseline scans (D0) and writes them into a calibration asset.
    /// </summary>
    public sealed class ProjectionSurfaceCalibrationWindow : EditorWindow
    {
        private const string WindowName = "Projection Calibration";

        private UamSensor? sensor;
        private ProjectionSurface? surface;
        private ProjectionSurfaceCalibration? targetAsset;
        private UamStreamMode streamMode = UamStreamMode.Standard;
        private string note = string.Empty;

        private int[]? latestDistances;
        private UamStreamMode latestStreamMode;
        private uint latestTimestamp;
        private DateTime latestReceivedAtUtc;

        [MenuItem("Hokuyo UAM/Projection Surface Calibration", priority = 100)]
        public static void Open()
        {
            var window = GetWindow<ProjectionSurfaceCalibrationWindow>();
            window.titleContent = new GUIContent(WindowName);
            window.Show();
        }

        private void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;
        }

        private void OnDisable()
        {
            AttachSensor(null);
            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
        }

        private void HandleBeforeAssemblyReload()
        {
            AttachSensor(null);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            DrawSourceSection();
            EditorGUILayout.Space(10f);
            DrawLatestScanSection();
            EditorGUILayout.Space(10f);
            DrawCalibrationSection();
        }

        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("入力ソース", EditorStyles.boldLabel);

            var newSensor = (UamSensor?)EditorGUILayout.ObjectField("Sensor", sensor, typeof(UamSensor), true);
            if (newSensor != sensor)
            {
                AttachSensor(newSensor);
            }

            surface = (ProjectionSurface?)EditorGUILayout.ObjectField("Surface (optional)", surface, typeof(ProjectionSurface), true);

            streamMode = (UamStreamMode)EditorGUILayout.EnumPopup("Target Stream Mode", streamMode);

            if (sensor is null)
            {
                EditorGUILayout.HelpBox("Play Mode で動作中の UamSensor を指定してください。", MessageType.Info);
            }
            else if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("キャリブレーションは Play Mode 中に実センサまたはモックから取得します。", MessageType.Warning);
            }
        }

        private void DrawLatestScanSection()
        {
            EditorGUILayout.LabelField("取得済みデータ", EditorStyles.boldLabel);

            if (latestDistances is null)
            {
                EditorGUILayout.HelpBox("まだスキャンデータを受信していません。センサがストリームを送出していることを確認してください。", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Last Stream Mode", latestStreamMode);
                EditorGUILayout.IntField("Beam Count", latestDistances.Length);
                EditorGUILayout.TextField("Sensor Timestamp", latestTimestamp.ToString());
                EditorGUILayout.TextField("Captured (UTC)", latestReceivedAtUtc.ToString("u"));
            }

            if (latestStreamMode != streamMode)
            {
                EditorGUILayout.HelpBox($"最新データのストリームモード ({latestStreamMode}) がターゲット設定 ({streamMode}) と異なります。", MessageType.Warning);
            }

            int expected = UamAngleTable.GetDirections(streamMode).Length;
            if (expected != 0 && latestDistances.Length != expected)
            {
                EditorGUILayout.HelpBox($"ビーム数 {latestDistances.Length} がモード {streamMode} の想定値 {expected} と一致しません。センサ設定を確認してください。", MessageType.Error);
            }
        }

        private void DrawCalibrationSection()
        {
            EditorGUILayout.LabelField("アセット書き込み", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                targetAsset = (ProjectionSurfaceCalibration?)EditorGUILayout.ObjectField("Calibration Asset", targetAsset, typeof(ProjectionSurfaceCalibration), false);

                if (GUILayout.Button("新規作成...", GUILayout.Width(100f)))
                {
                    CreateNewAsset();
                }
            }

            note = EditorGUILayout.TextField("Note", note);

            using (new EditorGUI.DisabledScope(latestDistances is null || targetAsset is null))
            {
                if (GUILayout.Button("最新スキャンを保存"))
                {
                    SaveCurrentDistances();
                }
            }

            if (targetAsset is not null)
            {
                if (targetAsset.TryGetBaseline(streamMode, out var stored, out var capturedAt, out var storedNote))
                {
                    EditorGUILayout.LabelField("既存のベースライン", EditorStyles.miniBoldLabel);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.IntField("Beam Count", stored.Length);
                        EditorGUILayout.TextField("Captured (UTC)", capturedAt == DateTime.MinValue ? "-" : capturedAt.ToString("u"));
                        EditorGUILayout.TextField("Note", storedNote);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox($"このアセットにはモード {streamMode} のベースラインがまだ登録されていません。", MessageType.Info);
                }
            }
        }

        private void SaveCurrentDistances()
        {
            if (targetAsset is null || latestDistances is null)
            {
                return;
            }

            try
            {
                Undo.RecordObject(targetAsset, "Update Projection Surface Calibration");
                targetAsset.SetBaseline(streamMode, latestDistances.AsSpan(), DateTime.UtcNow, note);
                EditorUtility.SetDirty(targetAsset);
                AssetDatabase.SaveAssetIfDirty(targetAsset);
                ShowNotification(new GUIContent("Calibration saved."));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ShowNotification(new GUIContent("保存に失敗しました。Console を確認してください。"));
            }
        }

        private void CreateNewAsset()
        {
            string directory = "Assets";
            if (surface is not null)
            {
                var assetPath = AssetDatabase.GetAssetPath(surface);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    directory = Path.GetDirectoryName(assetPath) ?? "Assets";
                }
            }

            var path = EditorUtility.SaveFilePanelInProject(
                "Projection Surface Calibration",
                "ProjectionSurfaceCalibration.asset",
                "asset",
                "キャリブレーションアセットの保存先を指定してください。",
                directory);

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var asset = ScriptableObject.CreateInstance<ProjectionSurfaceCalibration>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            targetAsset = asset;
            Selection.activeObject = asset;
        }

        private void AttachSensor(UamSensor? newSensor)
        {
            if (sensor == newSensor)
            {
                return;
            }

            if (sensor is not null)
            {
                sensor.OnScan -= HandleOnScan;
            }

            sensor = newSensor;

            if (sensor is not null)
            {
                sensor.OnScan += HandleOnScan;
            }

            latestDistances = null;
            latestTimestamp = 0;
        }

        private void HandleOnScan(IPolarScan scan)
        {
            var distances = scan.Distances.Span;
            var copy = new int[distances.Length];
            for (int i = 0; i < distances.Length; ++i)
            {
                copy[i] = distances[i];
            }

            latestDistances = copy;
            latestStreamMode = scan.StreamMode;
            latestTimestamp = scan.Timestamp;
            latestReceivedAtUtc = DateTime.UtcNow;

            EditorApplication.delayCall += Repaint;
        }
    }
}
