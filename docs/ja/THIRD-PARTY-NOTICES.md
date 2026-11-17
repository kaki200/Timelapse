# このforkで再配布するコンポーネント

[English](../../Installers/THIRD-PARTY-NOTICES.md) | **日本語**

このページは、このforkで作成した同梱コンポーネント説明の参考訳です。
英語の通知文書および各コンポーネントのライセンス原文を変更・置き換えるものではありません。
正確な条件は、インストール先のライセンス文書と各配布元の原文を確認してください。

Timelapseの著作権は、2011年以降のGreenberg Consulting Inc.およびUniversity of Calgaryにあります。
このforkでは、キーボードショートカット、Flyleaf再生と移動操作の修正、動体BBox、再配布用インストーラーを追加しています。
ソースは [kaki200/Timelapse](https://github.com/kaki200/Timelapse) です。
Timelapse本体とこれらの変更にはCC BY-NC-SA 4.0を適用します。
独立した第三者コンポーネントには、それぞれ固有のライセンスが適用されます。

## FFmpeg 7.1.3

著作権者はFFmpegの開発者で、同梱する構成はLGPL-2.1-or-laterです。
公式ソースから、GPL・nonfree・version3・外部ライブラリの自動検出・ネットワークプロトコルを無効にしてビルドしています。
内蔵のデコード・エンコード・フィルターとWindowsのハードウェアアクセラレーションは維持しています。
libx264・libx265は含めていません。

ソースは `Licenses/Sources/ffmpeg-7.1.3.tar.xz`、構成・ライセンス文書・ビルドスクリプトは
`Licenses/FFmpeg` に収録しています。2026-11-16に追加した `FfmpegMsvcResponse.patch` は、
MSVCのコマンド行長制限に対応するレスポンスファイルを使うため、Windows向けビルド用ファイルだけを変更します。
GNU Makeはビルド時だけ使い、アプリにはインストールしません。
インストール先の `FFmpeg` フォルダーは、ABI互換のライブラリ・ツールに置き換えられます。

[FFmpegのライセンス説明](https://ffmpeg.org/legal.html)

## FlyleafLib 3.10.4 / Flyleaf.FFmpeg.Bindings 7.1.1

著作権者はSuRGeoNix、ライセンスはLGPL-3.0-or-laterです。動的に読み込むアセンブリを使います。
対応ソースは `Licenses/Sources` に収録し、取得先を `Licenses/dependency-downloads.json` に固定しています。

- FlyleafLib: `3b057fd645bbbf393350152bba1c7510e47b21ef`
- Bindings 7.1.1: `e66f21d30610fa7a4cc628f52fbce6c1c578840b`

元のNuGetメタデータは `Licenses/Packages` に保持しています。
Bindingsパッケージのリポジトリメタデータは7.1.1のソースより古いため、上記の固定リビジョンを使います。
Flyleafのソースアーカイブからはビルド済みの第三者バイナリを除外し、ソース・ビルドファイル・リソース・通知文書を残しています。

このforkでは2026-11-16にBindingsのローダーを修正し、未使用でGPL専用のlibpostprocを任意にしました。
元のソース、適用パッチ、単体ビルド用プロジェクトは `Licenses/Sources` と `Licenses/FlyleafBindings` にあります。
FlyleafLib自体は変更していません。アセンブリの置き換え、およびLGPLライブラリの変更をデバッグするための
Timelapseのリバースエンジニアリングを認め、これらの権利を制限するEULA条項は設けていません。

[Flyleaf](https://github.com/SuRGeoNix/Flyleaf) · [Bindingsジェネレーター](https://github.com/SuRGeoNix/Flyleaf.FFmpeg.Generator)

## ExifTool 13.59 / Oliver Betzランチャー / Strawberry Perl

ExifToolの著作権者はPhil Harveyおよび貢献者で、Perlと同じ条件
（Artistic-1.0-PerlまたはGPL-1.0-or-later）で再配布します。この配布ではArtisticの選択肢を使います。
Perlのソースモジュールは `exiftool_files/lib` にあります。

ランチャーはCC0です。Strawberry Perlの各コンポーネントにはそれぞれ元のライセンスを適用し、
`exiftool_files/Licenses_Strawberry_Perl.zip` と `readme_windows.txt` を保持しています。
Windows配布物は、公式パッケージのミラーであるPhotoStructureの
`exiftool-vendored.exe@13.59.0` から取得したものを変更せず使います。
そのMITのパッケージ通知と元アーカイブもLicensesに収録しています。

[ExifTool](https://exiftool.org/) · [PhotoStructureのミラー](https://github.com/photostructure/exiftool-vendored.exe)

## その他の依存関係

OpenCvSharpとOpenCVはApache-2.0で、第三者の通知文書も保持しています。
フレーム取得にはFlyleafを使うため、未使用のOpenCV FFmpeg動画入出力プラグインは除外しています。
画像調整にはOpenCVを使います。NReco.VideoConverter、ImageSharp、ImageSharp.Drawing、
fork元の開発者向けの別証明書は配布アプリに含めません。旧ImageSharpは開発用の互換性テストだけが参照します。

TimelapseWpf.ToolkitとAvalonDockはMS-PL、ExifToolWrapperはパブリックドメインです。
DialogUpgradeFilesは元の条件を適用するfork元のコンポーネントです。
`Licenses/Packages` の一覧には、配置するNuGetコンポーネントのバージョン、著作権、
ライセンスメタデータ、元の通知文書とライセンスを保持し、標準的なライセンス全文を `Licenses/Texts` に収録しています。

.NETはWindows Desktop Runtimeを自己完結型で同梱し、元のライセンスと第三者通知を保持しています。
Visual C++ランタイムはVisual Studio Build Toolsの再配布用フォルダーからアプリ内へ配置し、
[Microsoftの再配布条件](https://visualstudio.microsoft.com/license-terms/)を適用します。

利用者が作成したデータベースやデータファイルは、これらのライセンスの対象ではありません。

[このforkの追加機能](README.md) · [ライセンス原文（英語）](../../LICENSE.md)
