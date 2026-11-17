# Local validation tools

These small Windows x64 / .NET 10 projects exercise the production implementation.
Build output, local videos, copied templates/databases, installation logs and
machine-specific measurement folders are deliberately not committed.

## Deterministic motion tests

```powershell
dotnet run --project validation/MotionBboxCoreTests/MotionBboxCoreTests.csproj -c Release
```

Ten cases cover motion/static frames, source and timestamp resets, illumination
changes, component merging, tracking and bottom-band clipping. All ten passed on
2026-11-17.

## Image adjustment compatibility

```powershell
dotnet run --project validation/ImageAdjustmentCompatibility/ImageAdjustmentCompatibility.csproj -c Release
```

The old ImageSharp dependency is used only as a development test baseline; it is
not deployed with Timelapse. Fourteen synthetic-image cases compare the OpenCV
implementation with the old adjustments. In the 2026-11-16 run, thirteen matched
exactly; histogram equalization differed by at most one 8-bit level.

## Complete installer payload

Build the self-contained app using
`Installers/TimelapseInstaller-PerUser/BuildInstaller.bat`. This prepares the
verified FFmpeg/ExifTool/native runtime payload, applies the optional-postproc
bindings patch, and supplies license notices and corresponding sources.

```powershell
dotnet run --project validation/InstallerSmoke/InstallerSmoke.csproj -c Release -- '<publish-or-installed-folder>' '<local-result-folder>'
```

This checks native loading, actual FFmpeg library license strings, ExifTool,
thumbnail extraction, non-ASCII/spaced filenames, invalid timestamps and missing
files. Both staged and installed payloads passed the eight checks on 2026-11-16.

## Playback + capture + analysis + display benchmark

Build `MotionBboxBenchmark` with `-p:BundledDependenciesDir=<absolute-payload-path>`
and replace its output `Flyleaf.FFmpeg.Bindings.dll` with the patched DLL from the
same payload (as the installer build does). Then run:

```powershell
MotionBboxBenchmark.exe '<local-video>' 12 640 15 frame_diff '<local-result.json>'
```

The measured path includes Flyleaf playback, frame capture, OpenCV analysis and
WPF display. On the development laptop with the LGPL-only installer payload,
2026-11-16 measurements yielded 14.92 analyzed frames/s for a 720p AVI and 14.75
frames/s for a 1080p H.264 video. Display updates were approximately 15/s and
neither run had failed captures. These figures are hardware/content dependent,
not a guarantee for every computer or video.

## Data-entry layout regression checks

Build `DataEntryLayoutTests` with `-p:TimelapseBin=<absolute-published-app-folder>`.
Copy its DLL into a separate test copy of that published folder, then run it with
the app's runtime configuration and dependency manifest:

```powershell
dotnet exec --runtimeconfig '<test-copy>/Timelapse.runtimeconfig.json' --depsfile '<test-copy>/Timelapse.deps.json' '<test-copy>/DataEntryLayoutTests.dll'
```

Twenty in-process WPF checks passed on 2026-11-17: vertical row alignment,
scrollable content, bounded widths, long-label wrapping, adjacent colors,
checkbox alignment, ten layout-switch round trips, retained flag/multi-choice
values, narrow resizing and safe reset. This is an automated measure/arrange
test, not a substitute for full interactive testing on every display setup.
