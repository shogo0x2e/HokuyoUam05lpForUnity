# Hokuyo UAM 05LP for Unity

Unity 向けに Hokuyo UAM-05LP センサのスキャン取得・可視化・接触検出を行うためのライブラリです。実機接続と合わせて、エディタ内で動作確認できるモック環境も含んでいます。

## プロジェクト構成
- `Assets/Shogo0x2e/HokuyoUam05lpForUnity/Runtime/` — ランタイムコード（センサ I/O、検出ロジック、補助ユーティリティ）
- `Assets/Shogo0x2e/HokuyoUam05lpForUnity/Examples/` — サンプルシーンとモック、可視化用スクリプト
- `Assets/Shogo0x2e/HokuyoUam05lpForUnity/Editor/` — エディタツール（必要に応じて追加）
- `Tests/HokuyoCore.Tests/` — ランタイムのコア計算ロジックを検証する xUnit テスト

## 動作要件
- Unity 6.0.2f2 以降（プロジェクトは `6000.2.6f2` で検証）
- .NET SDK 9.0 以降（CLI で `dotnet build/test` を利用する場合）
- 実機検証を行う場合は UAM-05LP センサとネットワーク接続環境

## 初回セットアップ手順
1. リポジトリをクローンし Unity Hub でプロジェクトフォルダを開く。
2. Unity で `Assets/Shogo0x2e/HokuyoUam05lpForUnity/Examples/Scenes/HitDetectorMock.unity` を開く。
3. シーン内の `UamSensorMockDriver` コンポーネントにある
   - `Sensor` フィールドがシーンの `UamSensor` を指していること
   - `AutoStart` がオンになっていること
   を確認。
4. `UamHitDetectorBridge` の `Projection Surface` と `Sensor Origin` が正しく割り当てられているかチェックする。
5. Play Mode に入るとモックセンサがスキャンフレームを生成し、`HitPrefabVisualizer` と Gizmo でヒット位置が表示される。

## CLI でのビルド確認
Unity を開く前にランタイムがコンパイルできるか確認したい場合は以下を実行します。

```bash
dotnet build HokuyoUam05lpForUnity.sln
```

エディタ拡張や Unity 固有アセンブリに関する警告（例: `Unity.Rider.Editor`）が出ることがありますが、Unity で解決されるため無視して構いません。

## 実機センサで利用するには
1. シーン内の `UamSensor` を選択し、IP アドレスとポートをセンサの設定値に合わせる。
2. `AutoStart` をオンにするとシーン開始時に自動接続します。任意で UI ボタン等から `StartSensor()` を呼ぶことも可能です。
3. `Dispatch Events On Unity Thread` をオンにしておくと、検出結果を安全に Unity API へ渡せます。

## ヒット検出ワークフロー
1. `UamSensor` がスキャン (`IPolarScan`) を発行。
2. `UamHitDetectorBridge` が ROI 判定、距離フィルタ、クラスタリングを経て `HitObservation` のリストを生成。
3. `HitPrefabVisualizer` やユーザ実装のリスナーが `OnDetections` を受け取り、UI やロジックへ反映。

### 主な調整パラメータ
- `UamHitDetectorBridge` → `minDistanceMeters` / `maxDistanceMeters` : ROI 内で検出したい距離範囲
- `UamHitDetectorBridge` → `groupingDistanceMeters` : 近距離ビームを同一接触として扱うしきい値（既定 0.1m）
- `UamSensorMockDriver` → `HighlightOffsetsMeters` / `AdditionalClusters` : モックで生成するターゲットパターン
- `ProjectionSurface` : 判定平面の幅・高さ・奥行き許容値

## モックシーンを利用したパラメータ調整
1. `UamHitDetectorBridge` の `logDetections` を有効にすると、Console にヒット数とステップ番号が出力されます。
2. `groupingDistanceMeters` を変更し、ヒット数の変化を見ながらクラスタリングしきい値を調整します。
3. `AdditionalClusters` を有効にして複数接触を模擬し、期待する分離になるか Play Mode 上で確認します。

## 他プロジェクトへの組み込み
1. `Assets/Shogo0x2e/HokuyoUam05lpForUnity/Runtime/` 以下を自プロジェクトへ追加するか、Unity パッケージ化して導入。
2. シーンに `UamSensor`、`ProjectionSurface`、`UamHitDetectorBridge` を配置し、必要なら `HitPrefabVisualizer` を追加。
3. ROI 判定用の平面や座標変換が異なる場合は `ProjectionSurface` を複製・カスタマイズしてください。
4. `UamHitDetectorBridge.OnDetections` に任意の MonoBehaviour を接続し、`List<HitObservation>` を受け取ってアプリケーション固有の挙動を実装します。

## トラブルシューティング
- **センサ接続が開始されない** — `UamSensor` の IP/ポート、`AutoStart`、およびファイアウォール設定を確認。Console の `[UamSensor]` ログも参考にしてください。
- **ヒットが検出されない** — ROI サイズ、`Min/MaxDistance`、`RejectZeroDistance` を見直し。モックの距離パターンが ROI 内に入っているかも確認。
- **ヒットが複数に分割される** — `groupingDistanceMeters` を大きくするか、モック/実機のビーム密度を再確認。
- **Unity API 呼び出しでエラー** — `UamSensor.DispatchEventsOnUnityThread` がオンか、またはコールバック側でメインスレッドディスパッチを実施してください。

## ライセンス / 貢献
ライセンス表記やコントリビューションポリシーが決まり次第ここに追記します。改善案や不具合報告は Pull Request または Issue で歓迎します。
