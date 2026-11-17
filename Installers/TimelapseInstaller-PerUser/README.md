# Self-contained installer for the kaki200 fork

Double-click `Timelapse-Kaki-2.5.0.10-win-x64.msi`, accept the licenses, and install.
Then use the desktop or Start Menu shortcut **Timelapse (kaki200 fork)**.
No separate ExifTool, FFmpeg, .NET, Perl, or VC runtime setup and no PATH editing
are needed. The default directory is `%LOCALAPPDATA%\Programs\Timelapse-Kaki`.
Windows x64 is required. The fork has a separate MSI identity from upstream.

For live motion boxes, check **Motion BBox** next to Mute/Repeat in the video
controls and play a video. Further settings are in Video Player Options.
The detector needs successive frames; a paused image cannot initialize it.

## Build

Requirements: .NET 10 SDK, Visual Studio C++ Build Tools, Git for Windows at its
default location, WiX Toolset 6 with `WixToolset.UI.wixext`, and network access
for the initial dependency/notice downloads.

From the repository root:

```powershell
.\Installers\TimelapseInstaller-PerUser\BuildInstaller.bat
```

The pipeline pins downloads by SHA-256, builds an LGPL-only FFmpeg 7.1.3 with
Windows hardware decoding, assembles ExifTool 13.59 plus its Perl files and
app-local VC runtime, publishes a fresh self-contained application, excludes
legacy binaries and the unused OpenCV FFmpeg plugin, and collects license texts
and corresponding LGPL sources. Required-file and license checks run before WiX.
`GenerateFileList.ps1` creates stable component IDs and preserves the payload tree.

Output: `Installers\bin\Release\Timelapse-Kaki-2.5.0.10-win-x64.msi`.
The exact staging directory is recorded in `latest-publish-path.txt` beside it.
`BuildInstaller.ps1 -SkipPrepare` reuses an already verified dependency payload.
Caches and staging folders are ignored by Git. No publisher signing certificate
is borrowed from upstream; the local MSI is unsigned unless separately signed
by the fork owner.

The old `UpdateVersion.ps1` is no longer invoked. MSI version `2.5.10` maps to app
version `2.5.0.10`, because Windows Installer compares only the first three fields.
The package version and filename are derived from the application's FileVersion.
Increment AssemblyVersion and FileVersion in `Timelapse.csproj` for a new release.

## Licenses and sources

`THIRD-PARTY-NOTICES.md`, `LICENSE.md`, package notices and full license texts are
installed with the app. `Licenses\Sources` contains corresponding LGPL library
sources, and `Licenses\FFmpeg` contains the build patch and configuration.
The decoder DLLs and command-line FFmpeg tool are shared rather than statically
linked into Timelapse and can be replaced with ABI-compatible versions.

NReco.VideoConverter, ImageSharp, ImageSharp.Drawing and the unused extended
Flyleaf.FFmpeg package are removed. Image adjustments now use OpenCV and are
compared against the previous ImageSharp implementation by a development-only
compatibility test. No third-party developer license key is needed for this build.

Uninstall this fork from Windows Installed Apps. MSI removes its installed files
and shortcuts; it does not enumerate or remove users' video/database folders.
