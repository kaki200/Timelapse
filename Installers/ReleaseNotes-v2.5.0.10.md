# Timelapse 2.5.0.10 (kaki200 fork)

**English** | [日本語](https://github.com/kaki200/Timelapse/blob/develop/docs/ja/RELEASE-v2.5.0.10.md)

Self-contained installer for Windows x64, with the required runtimes included.

## Installation

1. Download **Timelapse-Kaki-2.5.0.10-win-x64.msi** from the release assets and double-click it.
2. After installation, launch **Timelapse (kaki200 fork)** from the desktop or Start Menu.

FFmpeg, ExifTool (including Perl), .NET Desktop Runtime, and the required Visual C++ runtime are bundled.
No separate downloads or PATH configuration are needed. This package can upgrade the previous installer with the same fork identity.
Back up your templates and databases before updating as a precaution.

## Changes

- Added lightweight live motion bounding boxes for videos, enabled with **Motion BBox** in the playback controls.
- Made BBox outlines thinner: 1.5 display units for the main box and 1 for additional boxes.
- Added **Window > Data Entry Panel on the right (same window)** to arrange checkboxes vertically beside the video without opening a separate window.
- Color-coded flag fields with subtle backgrounds. Long labels wrap, and long lists can scroll.
- Save the sidebar placement and width with the window layout.
- Retained keyboard shortcuts, hold-to-fast-forward/rewind, mouse-wheel zoom, and drag-to-pan controls.
- Simplified dependencies for image adjustments and thumbnail extraction.

## Validation

- Passed 10 motion BBox core tests, 14 image-adjustment compatibility cases, and 20 data-entry layout checks.
- Passed smoke tests for FFmpeg, OpenCV, ExifTool, and thumbnail extraction in the distribution folder.
- Previous measurements on this laptop with the same dependency configuration, including playback, capture, analysis, and display, reached approximately 14.9 fps for 720p AVI and 14.8 fps for 1080p H.264. Performance depends on the video and hardware.

## Notes and licenses

- The installer is unsigned; Windows or your browser may display a warning.
- This is kaki200's fork, not an official upstream distribution. No pull request has been opened against upstream.
- Timelapse retains its CC BY-NC-SA 4.0 non-commercial license. Third-party components retain their own licenses.
- FFmpeg uses an LGPL configuration. License texts, corresponding sources, patches, and build settings are included in the installed **Licenses** folder.
- Use **checksums-SHA256.txt** from the release assets to verify the downloaded MSI's SHA-256.
