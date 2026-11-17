param([string]$Cache = "$PSScriptRoot\..\.installer-cache")
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$Cache = [IO.Path]::GetFullPath($Cache)
New-Item -ItemType Directory -Force $Cache | Out-Null
$downloads = @(
    @('ffmpeg-7.1.3.tar.xz','https://ffmpeg.org/releases/ffmpeg-7.1.3.tar.xz','F0BF043299DB9E3CAACB435A712FC541FBB07DF613C4B893E8B77E67BAF3ADBE'),
    @('make-4.4.1.tar.gz','https://ftp.gnu.org/gnu/make/make-4.4.1.tar.gz','DD16FB1D67BFAB79A72F5E8390735C49E3E8E70B4945A15AB1F81DDB78658FB3'),
    @('exiftool-13.59.tgz','https://registry.npmjs.org/exiftool-vendored.exe/-/exiftool-vendored.exe-13.59.0.tgz','1DC1F166FB04E9AF64F9C24E3389F1EDD05C2073FE861A8B68FC8FB1E6687855'),
    @('Flyleaf-source.zip','https://codeload.github.com/SuRGeoNix/Flyleaf/zip/3b057fd645bbbf393350152bba1c7510e47b21ef','999D619C2F202CC9FC49B18538FD7342A8900781FC71B1FB090F4FD9988C3D5C'),
    @('Flyleaf-bindings-7.1-source.zip','https://codeload.github.com/SuRGeoNix/Flyleaf.FFmpeg.Generator/zip/e66f21d30610fa7a4cc628f52fbce6c1c578840b','A57F62AF8A1EC7E11F4E369194B8346FB581C3849C8CF31BF81608B4B614ED3B')
)
foreach ($download in $downloads) {
    $file = Join-Path $Cache $download[0]
    if (-not (Test-Path -LiteralPath $file)) {
        Write-Host "Downloading $($download[0])"
        Invoke-WebRequest -UseBasicParsing $download[1] -OutFile $file
    }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $download[2]) {
        throw "SHA256 mismatch: $file"
    }
}
foreach ($archive in @('ffmpeg-7.1.3.tar.xz','make-4.4.1.tar.gz')) {
    $directory = if ($archive -like 'ffmpeg*') { 'ffmpeg-7.1.3' } else { 'make-4.4.1' }
    if (Test-Path (Join-Path $Cache $directory)) { continue }
    & tar -xf (Join-Path $Cache $archive) -C $Cache
    if ($LASTEXITCODE -ne 0) { throw "Extraction failed: $archive" }
}
if (-not (Test-Path "$Cache\ffmpeg-install\bin\ffmpeg.exe")) {
    & "$PSScriptRoot\BuildFfmpeg.cmd"
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg build failed' }
}
$payload = Join-Path $Cache 'payload'
New-Item -ItemType Directory -Force "$payload\FFmpeg","$payload\Licenses\Sources","$payload\Licenses\FFmpeg" | Out-Null
Copy-Item "$Cache\ffmpeg-install\bin\*.dll","$Cache\ffmpeg-install\bin\ffmpeg.exe" "$payload\FFmpeg"
$version = & "$payload\FFmpeg\ffmpeg.exe" -version 2>&1 | Out-String
$license = & "$payload\FFmpeg\ffmpeg.exe" -hide_banner -L 2>&1 | Out-String
if ($version -match '--enable-gpl|--enable-nonfree' -or $license -notmatch 'Lesser General Public License' -or $version -notmatch '--enable-d3d11va') {
    throw 'Refusing to package a GPL/nonfree or unrecognized FFmpeg build'
}
[IO.File]::WriteAllText("$payload\Licenses\FFmpeg\build-version.txt", $version)
[IO.File]::WriteAllText("$payload\Licenses\FFmpeg\build-license.txt", $license)
Copy-Item "$Cache\ffmpeg-7.1.3\COPYING*","$Cache\ffmpeg-7.1.3\LICENSE.md" "$payload\Licenses\FFmpeg"
Copy-Item "$Cache\ffmpeg-build\config.h","$Cache\ffmpeg-build\ffbuild\config.mak" "$payload\Licenses\FFmpeg"
Copy-Item "$PSScriptRoot\BuildFfmpeg.cmd","$PSScriptRoot\BuildFfmpeg.sh","$PSScriptRoot\FfmpegMsvcResponse.patch" "$payload\Licenses\FFmpeg"
Copy-Item "$Cache\ffmpeg-7.1.3.tar.xz","$Cache\Flyleaf-bindings-7.1-source.zip" "$payload\Licenses\Sources"
if (Test-Path "$payload\Licenses\Sources\Flyleaf-bindings-source.zip") { Remove-Item -LiteralPath "$payload\Licenses\Sources\Flyleaf-bindings-source.zip" }
New-Item -ItemType Directory -Force "$Cache\bindings-source","$payload\Licenses\FlyleafBindings" | Out-Null
& tar -xf "$Cache\Flyleaf-bindings-7.1-source.zip" -C "$Cache\bindings-source"
if ($LASTEXITCODE -ne 0) { throw 'Binding source extraction failed' }
$bindingRoot = "$Cache\bindings-source\Flyleaf.FFmpeg.Generator-e66f21d30610fa7a4cc628f52fbce6c1c578840b"
& 'C:\Program Files\Git\usr\bin\patch.exe' -d $bindingRoot -p1 -i "$PSScriptRoot\FlyleafOptionalPostproc.patch"
if ($LASTEXITCODE -ne 0) { throw 'Optional postproc patch failed' }
& dotnet build "$PSScriptRoot\FFmpegBindings.csproj" -c Release "-p:OutputPath=$Cache\bindings-build\"
if ($LASTEXITCODE -ne 0) { throw 'LGPL binding build failed' }
Copy-Item "$Cache\bindings-build\Flyleaf.FFmpeg.Bindings.dll" $payload
Copy-Item "$PSScriptRoot\FlyleafOptionalPostproc.patch","$PSScriptRoot\FFmpegBindings.csproj" "$payload\Licenses\FlyleafBindings"
# The upstream Flyleaf repository also contains prebuilt player/FFmpeg binaries.
# The corresponding-source archive deliberately contains source and resources only.
New-Item -ItemType Directory -Force "$Cache\Flyleaf-source-tree" | Out-Null
& tar -xf "$Cache\Flyleaf-source.zip" -C "$Cache\Flyleaf-source-tree" --exclude='*.dll' --exclude='*.exe' --exclude='*.7z' --exclude='*.zip' --exclude='*.nupkg'
if ($LASTEXITCODE -ne 0) { throw 'Flyleaf source extraction failed' }
& tar -czf "$payload\Licenses\Sources\Flyleaf-3b057fd-source.tar.gz" -C "$Cache\Flyleaf-source-tree" .
if ($LASTEXITCODE -ne 0) { throw 'Flyleaf source archive failed' }
New-Item -ItemType Directory -Force "$Cache\exiftool-npm" | Out-Null
& tar -xf "$Cache\exiftool-13.59.tgz" -C "$Cache\exiftool-npm"
if ($LASTEXITCODE -ne 0) { throw 'ExifTool extraction failed' }
Copy-Item "$Cache\exiftool-npm\package\bin\exiftool.exe" "$payload\exiftool.exe"
if (Test-Path "$payload\exiftool(-k).exe") { Remove-Item -LiteralPath "$payload\exiftool(-k).exe" -Force }
Copy-Item "$Cache\exiftool-npm\package\bin\exiftool_files" $payload -Recurse -Force
New-Item -ItemType Directory -Force "$payload\Licenses\ExifTool" | Out-Null
Copy-Item "$Cache\exiftool-npm\package\LICENSE","$Cache\exiftool-npm\package\bin\README.txt" "$payload\Licenses\ExifTool"
Copy-Item "$Cache\exiftool-13.59.tgz" "$payload\Licenses\Sources"
$previousLocale = $env:LC_ALL
try {
    $env:LC_ALL = 'C'
    if ((& "$payload\exiftool.exe" -ver) -ne '13.59') { throw 'ExifTool version check failed' }
} finally { $env:LC_ALL = $previousLocale }
# App-local VC runtime supports OpenCvSharp without a machine-wide prerequisite installer.
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$crt = Get-ChildItem "$visualStudio\VC\Redist\MSVC" -Directory | Where-Object Name -Match '^\d+\.' | Sort-Object Name -Descending | Select-Object -First 1
if (-not $crt) { throw 'Licensed Visual C++ redistributable runtime not found' }
Copy-Item "$($crt.FullName)\x64\Microsoft.VC145.CRT\*.dll" $payload
$manifest = @($downloads | ForEach-Object { [pscustomobject]@{ File=$_[0]; Url=$_[1]; Sha256=$_[2] } })
$manifest | ConvertTo-Json | Set-Content "$payload\Licenses\dependency-downloads.json" -Encoding UTF8
Copy-Item "$repo\LICENSE.md","$PSScriptRoot\THIRD-PARTY-NOTICES.md" $payload
Write-Host "Verified dependency payload: $payload"
