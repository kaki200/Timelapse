# このforkの自己完結型インストーラー

[English](../../Installers/TimelapseInstaller-PerUser/README.md) | **日本語**

このページは、このforkで作成したインストーラーとビルド手順の説明です。
fork元のインストーラー資料や既存のREADMEは変更していません。

## 使い方

`Timelapse-Kaki-2.5.0.10-win-x64.msi` をダブルクリックし、ライセンスを確認してインストールします。
その後、デスクトップまたはスタートメニューの **Timelapse (kaki200 fork)** を使ってください。

ExifTool・FFmpeg・.NET・Perl・Visual C++ランタイムの個別セットアップやPATH編集は不要です。
標準のインストール先は `%LOCALAPPDATA%\Programs\Timelapse-Kaki` です。
Windows x64向けで、fork元とは別のMSI識別子を使います。

動画の再生欄でMute/Repeatの隣にある **Motion BBox** をONにすると動体枠を表示できます。
詳しい設定はVideo Player Optionsにあります。検出には連続フレームが必要で、停止画像だけでは開始できません。

## ビルド

必要なものは、.NET 10 SDK、Visual Studio C++ Build Tools、標準の場所にインストールしたGit for Windows、
WiX Toolset 6と `WixToolset.UI.wixext`、初回の依存ファイル・ライセンス文書を取得するためのネットワーク接続です。

リポジトリのルートから実行します。

```powershell
.\Installers\TimelapseInstaller-PerUser\BuildInstaller.bat
```

この処理は、ダウンロードをSHA-256で固定し、Windowsのハードウェアデコードを有効にしたLGPL構成の
FFmpeg 7.1.3をビルドします。ExifTool 13.59とPerlファイル、アプリ内のVCランタイムを集め、
.NET同梱アプリを新しい作業フォルダーへ発行します。旧ネイティブファイルと未使用のOpenCV FFmpegプラグインを除外し、
ライセンス文書・対応するLGPLソースを収集します。WiXを実行する前に、必須ファイルとライセンスを確認します。
`GenerateFileList.ps1` は、安定したコンポーネントIDで配布ファイルの構造を維持します。

出力先は `Installers\bin\Release\Timelapse-Kaki-2.5.0.10-win-x64.msi` です。
実際に使った発行フォルダーは、その隣の `latest-publish-path.txt` に記録されます。
`BuildInstaller.ps1 -SkipPrepare` は、検証済みの依存ファイルを再利用します。
キャッシュと作業フォルダーはGitの管理対象外です。fork元の署名証明書は流用しません。
forkの所有者が別途署名しない限り、作成したMSIは未署名です。

旧 `UpdateVersion.ps1` は呼び出しません。Windows Installerが最初の3フィールドだけを比較するため、
MSIのバージョン `2.5.10` をアプリの `2.5.0.10` に対応させています。
パッケージのバージョンとファイル名はアプリのFileVersionから生成します。
新しいリリースでは、`Timelapse.csproj` のAssemblyVersionとFileVersionを更新してください。

## ライセンスとソース

`THIRD-PARTY-NOTICES.md`、`LICENSE.md`、パッケージの通知文書とライセンス全文をアプリに同梱しています。
`Licenses\Sources` に対応するLGPLライブラリのソースを、`Licenses\FFmpeg` にビルド用パッチと設定を収録しています。
FFmpegのDLLとコマンドラインツールはTimelapseへの静的リンクではなく、ABI互換のものに置き換えられます。

NReco.VideoConverter、ImageSharp、ImageSharp.Drawing、未使用の拡張Flyleaf.FFmpegパッケージは除去しました。
画像調整にはOpenCVを使い、開発用の互換性テストで以前のImageSharp実装と比較しています。
このビルドに第三者の開発者ライセンスキーは不要です。

アンインストールはWindowsの「インストールされているアプリ」から行います。
MSIはインストールしたファイルとショートカットを削除し、利用者の動画・データベースのフォルダーは列挙・削除しません。

[このforkの追加機能](README.md) · [同梱コンポーネント](THIRD-PARTY-NOTICES.md)
