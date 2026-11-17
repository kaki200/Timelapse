#!/usr/bin/env bash
set -euo pipefail
installer_dir="$(cd "$(dirname "$0")" && pwd)"
cache_dir="$(cd "$installer_dir/../.installer-cache" && pwd)"
if ! grep -q 'Timelapse installer build' "$cache_dir/ffmpeg-7.1.3/compat/windows/makedef"; then
  patch -d "$cache_dir/ffmpeg-7.1.3" -p1 < "$installer_dir/FfmpegMsvcResponse.patch"
fi
mkdir -p "$cache_dir/ffmpeg-build"
cd "$cache_dir/ffmpeg-build"
if [[ ! -f ffbuild/config.mak ]]; then
../ffmpeg-7.1.3/configure \
  --toolchain=msvc --arch=x86_64 --target-os=win32 \
  --disable-gpl --disable-nonfree --disable-version3 --disable-autodetect \
  --disable-x86asm --disable-doc --disable-debug --disable-network \
  --enable-shared --disable-static --disable-ffplay --disable-ffprobe \
  --enable-d3d11va --enable-dxva2 \
  --extra-cflags=-MT --prefix="$(cygpath -m "$cache_dir/ffmpeg-install")"
fi
# GNU Make is a native Windows build: use drive paths in generated makefiles.
sed -i 's@/c/@C:/@g' Makefile ffbuild/config.mak
gnumake.exe SHELL=C:/PROGRA~1/Git/bin/bash.exe clean
gnumake.exe SHELL=C:/PROGRA~1/Git/bin/bash.exe CCDEP= CXXDEP= ASDEP= HOSTCCDEP= -j8
gnumake.exe SHELL=C:/PROGRA~1/Git/bin/bash.exe install-progs install-libs
