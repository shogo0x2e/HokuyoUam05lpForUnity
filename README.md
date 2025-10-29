# HokuyoUam05lpForUnity

Hokuyo UAM-05LP-T301 のスキャンデータを Unity で扱うための軽量ランタイムです。TCP 経由での接続制御、ASCII プロトコル解析、ROI ベースのヒット検出、Gizmo/プレハブによる可視化を提供します。Unity 6000.2.6f2 で開発・検証しています。

## 概要
- `UamSensor` がセンサの接続・再接続・フレーム取り込みを担当し、`OnScan(IPolarScan)` / `OnPositionDetected(Vector2[])` を発火します。
- 内部層 (`UamClient`, `LidarProtocol`, `TcpTransport`) が ASCII コマンド処理と CRC 検証までをカバーします。
- `ProjectionSurface` と `HitDetector` / `UamHitDetectorBridge` が ROI フィルタと距離クラスタリングにより `HitObservation` を生成します。
- `UamPointCloudVisualizer` や `HitPrefabVisualizer` で Play Mode 中に点群・ヒットを確認可能。`UamSensorMockDriver` で実機なしの検証も行えます。
- CLI からは `dotnet build HokuyoUam05lpForUnity.sln` でコンパイル確認、`dotnet test` で数値ユーティリティの単体テストを実行できます。

## コントリビューション
- Issue / Pull Request は歓迎です。バグ報告時は再現手順と使用した Unity バージョンを記載してください。
- PR を送る際は `dotnet build HokuyoUam05lpForUnity.sln` が通ることを確認し、必要に応じてテストを追加してください。
- 大きな機能追加や API 変更は事前にディスカッションを行い、互換性やランタイム負荷への影響を共有してもらえると助かります。
- Editor 拡張やシーン更新を含む場合は、対象 Unity バージョンでの手動確認結果を PR 説明に記載してください。

ライセンス表記は整備中です。企業・研究目的での利用やフィードバックもお気軽にお知らせください。
