# このforkの検証ツール

[English](../../validation/README.md) | **日本語**

このforkで追加したWindows x64 / .NET 10の小規模な検証プロジェクトで、実際のアプリ実装を確認します。
ビルド出力、手元の動画、コピーしたテンプレート・データベース、インストールログ、
PC固有の測定フォルダーはGitに登録していません。

## 動体BBoxの決定的テスト

```powershell
dotnet run --project validation/MotionBboxCoreTests/MotionBboxCoreTests.csproj -c Release
```

動く領域・静止フレーム、動画と時刻の変更時のリセット、照明変化、領域の結合、追跡、
下部帯の境界処理を10ケースで確認します。2026-11-17に10件すべて通過しました。

## 画像調整の互換性

```powershell
dotnet run --project validation/ImageAdjustmentCompatibility/ImageAdjustmentCompatibility.csproj -c Release
```

旧ImageSharpは開発用テストの比較基準としてだけ使い、Timelapseと一緒には配布しません。
生成した画像の14ケースでOpenCV実装と以前の画像調整を比較します。
2026-11-16の測定では13件が完全一致し、ヒストグラム均等化だけ最大で8ビット階調の1段階差がありました。

## インストーラーの同梱物

`Installers/TimelapseInstaller-PerUser/BuildInstaller.bat` で自己完結型アプリをビルドします。
検証用のFFmpeg・ExifTool・ネイティブランタイムを準備し、libpostprocを任意にするBindingsのパッチを適用して、
ライセンス通知と対応ソースも用意します。

```powershell
dotnet run --project validation/InstallerSmoke/InstallerSmoke.csproj -c Release -- '<publish-or-installed-folder>' '<local-result-folder>'
```

ネイティブライブラリの読み込み、FFmpegの実際のライセンス文字列、ExifTool、サムネイル取得、
ASCII以外の文字・空白を含むファイル名、不正な時刻、存在しないファイルを確認します。
2026-11-16には、発行フォルダーとインストール先の両方で8項目を通過しました。

## 再生・取得・解析・表示を含む測定

`MotionBboxBenchmark` を `-p:BundledDependenciesDir=<absolute-payload-path>` 付きでビルドし、
出力先の `Flyleaf.FFmpeg.Bindings.dll` を同じ依存ファイル群の修正版DLLに置き換えます。
これはインストーラービルドと同じ処理です。その後、次を実行します。

```powershell
MotionBboxBenchmark.exe '<local-video>' 12 640 15 frame_diff '<local-result.json>'
```

Flyleafの動画再生、フレーム取得、OpenCV解析、WPF表示を含めて測定します。
LGPL構成のインストーラー同梱物を使った開発用ラップトップの2026-11-16の結果は、
720p AVIが解析済み14.92フレーム/秒、1080p H.264が14.75フレーム/秒でした。
表示更新は約15回/秒で、どちらも取得失敗はありませんでした。
数値はPCと動画に依存し、すべての環境・動画での性能を保証するものではありません。

## データ入力欄のレイアウト回帰テスト

`DataEntryLayoutTests` を `-p:TimelapseBin=<absolute-published-app-folder>` 付きでビルドします。
DLLを、発行フォルダーとは別に作ったテスト用コピーへ配置し、アプリのランタイム設定と依存定義で実行します。

```powershell
dotnet exec --runtimeconfig '<test-copy>/Timelapse.runtimeconfig.json' --depsfile '<test-copy>/Timelapse.deps.json' '<test-copy>/DataEntryLayoutTests.dll'
```

2026-11-17に、プロセス内のWPFチェック20項目を通過しました。
縦一列の位置、スクロール、幅の制限、長いラベルの折り返し、隣同士の色分け、チェックボックスの位置、
10往復の配置切替、チェック値と複数選択の保持、狭い幅への変更、安全なリセットを確認しています。
WPFのサイズ計算・配置の自動テストであり、すべての画面構成での手動操作確認に代わるものではありません。

[このforkの追加機能](README.md) · [インストーラー](INSTALLER.md)
