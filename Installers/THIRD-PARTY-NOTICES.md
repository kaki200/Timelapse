# Timelapse (kaki200 fork): redistributed components

**English** | [日本語](https://github.com/kaki200/Timelapse/blob/develop/docs/ja/THIRD-PARTY-NOTICES.md)

Timelapse: Copyright 2011+ Greenberg Consulting Inc. and University of Calgary.
Fork changes: keyboard shortcuts, Flyleaf playback and navigation fixes, live motion
bounding boxes, and redistributable installer integration. Source:
https://github.com/kaki200/Timelapse . Timelapse and these changes are distributed
under CC BY-NC-SA 4.0. This restriction applies to Timelapse, not to independent
third-party components listed below. Each component retains its own license.

## FFmpeg 7.1.3

Copyright the FFmpeg developers. LGPL-2.1-or-later. The executable and shared
libraries are built from official source with GPL, nonfree,
version3, external autodetection and network protocols disabled. Native decoding,
encoding, filters and Windows hardware acceleration are retained. No libx264 or
libx265 is included. Sources are in `Licenses/Sources/ffmpeg-7.1.3.tar.xz`;
configuration, license texts and build scripts are in `Licenses/FFmpeg`. The
included `FfmpegMsvcResponse.patch` modifies only Windows build files to use
response files for MSVC's command-line length limit (modified 2026-11-16).
GNU Make is a build-time tool and is not installed. The FFmpeg libraries and tool
can be replaced in the installed `FFmpeg` folder with ABI-compatible versions.
https://ffmpeg.org/legal.html

## FlyleafLib 3.10.4 and Flyleaf.FFmpeg.Bindings 7.1.1

Copyright SuRGeoNix. LGPL-3.0-or-later. Dynamically loaded assemblies.
Corresponding sources are included in `Licenses/Sources`. The source revisions
are pinned in `Licenses/dependency-downloads.json`: FlyleafLib at
`3b057fd645bbbf393350152bba1c7510e47b21ef`, and bindings 7.1.1 at
`e66f21d30610fa7a4cc628f52fbce6c1c578840b`. Original NuGet metadata is preserved
in `Licenses/Packages`; the bindings package's repository metadata predates its
7.1.1 source, so the pinned source revision above is used. The Flyleaf archive omits prebuilt
third-party binaries, retaining its source, build files, resources and notices.
The bindings loader is modified by this fork (2026-11-16) to make the unused
GPL-only libpostproc optional. The original binding source, exact patch, and
standalone build project are in `Licenses/Sources` and `Licenses/FlyleafBindings`.
FlyleafLib itself is unmodified. You may replace the assemblies and reverse-engineer Timelapse for debugging
modifications to the LGPL libraries. No EULA term restricts these rights.
https://github.com/SuRGeoNix/Flyleaf
https://github.com/SuRGeoNix/Flyleaf.FFmpeg.Generator

## ExifTool 13.59, Oliver Betz launcher, and Strawberry Perl

ExifTool: Copyright Phil Harvey and contributors. Redistributed under the same
terms as Perl (Artistic-1.0-Perl or GPL-1.0-or-later). This distribution uses the
Artistic alternative. The Perl source modules are in `exiftool_files/lib`.
The launcher is CC0. Strawberry Perl components keep their original licenses;
see `exiftool_files/Licenses_Strawberry_Perl.zip` and `readme_windows.txt`.
The unchanged Windows distribution was obtained from PhotoStructure's fixed
`exiftool-vendored.exe@13.59.0` mirror of the official Windows package. Its MIT
packaging notice and complete original archive are retained in Licenses.
https://exiftool.org/ https://github.com/photostructure/exiftool-vendored.exe

## Other application and framework dependencies

OpenCvSharp and OpenCV: Apache-2.0, including their retained third-party notices.
The unused OpenCV FFmpeg video-I/O plugin is excluded; frames are acquired by
Flyleaf. Image adjustments use OpenCV. NReco.VideoConverter, ImageSharp,
ImageSharp.Drawing and the upstream project's separate developer certificate
are not included in the shipped application. The old ImageSharp version is
referenced only by the development-only compatibility test project.

TimelapseWpf.Toolkit and AvalonDock: MS-PL. ExifToolWrapper: public domain.
DialogUpgradeFiles is the upstream Timelapse component under its original terms.
The package inventory in `Licenses/Packages` preserves each deployed NuGet
component's version, copyright, license metadata, original notices and license
text. Standard license texts are included in `Licenses/Texts`.
Microsoft .NET is included as a self-contained Windows Desktop runtime with
its original license and third-party notices. Microsoft Visual C++ Runtime is
included app-local from the Visual Studio Build Tools redistributable directory
under Microsoft's redistribution terms:
https://visualstudio.microsoft.com/license-terms/

Installed data files and user-created databases are not part of these licenses.
