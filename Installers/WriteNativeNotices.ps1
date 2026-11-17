param([Parameter(Mandatory=$true)][string]$PublishDir, [Parameter(Mandatory=$true)][string]$Cache)
$ErrorActionPreference = 'Stop'
$destination = Join-Path $PublishDir 'Licenses\OpenCV'
$nativeCache = Join-Path $Cache 'native-notices'
New-Item -ItemType Directory -Force $destination,$nativeCache | Out-Null
$files = @('LICENSE','3rdparty/cpufeatures/LICENSE','3rdparty/dlpack/LICENSE','3rdparty/flatbuffers/LICENSE.txt',
    '3rdparty/include/opencl/LICENSE.txt','3rdparty/libjpeg-turbo/LICENSE.md','3rdparty/libpng/LICENSE',
    '3rdparty/libtiff/LICENSE.md','3rdparty/libwebp/COPYING','3rdparty/openexr/LICENSE','3rdparty/openjpeg/LICENSE',
    '3rdparty/protobuf/LICENSE','3rdparty/quirc/LICENSE','3rdparty/zlib/LICENSE')
foreach ($file in $files) {
    $name = $file.Replace('/','-')
    $cached = Join-Path $nativeCache $name
    if (-not (Test-Path $cached)) { Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/opencv/opencv/fe38fc6/$file" -OutFile $cached }
    Copy-Item $cached (Join-Path $destination $name)
}
$additional = @(
    @('OpenCvSharp-LICENSE','https://raw.githubusercontent.com/shimat/opencvsharp/b161e7e012f5101f6d5dc68a835c59db6cc88b18/LICENSE'),
    @('Tesseract-LICENSE','https://raw.githubusercontent.com/tesseract-ocr/tesseract/5.5.1/LICENSE'),
    @('Leptonica-LICENSE','https://raw.githubusercontent.com/DanBloomberg/leptonica/1.85.0/leptonica-license.txt'),
    @('ITT-LICENSE','https://raw.githubusercontent.com/intel/ittapi/v3.25.4/LICENSES/BSD-3-Clause.txt')
)
foreach ($item in $additional) {
    $cached = Join-Path $nativeCache $item[0]
    if (-not (Test-Path $cached)) { Invoke-WebRequest -UseBasicParsing $item[1] -OutFile $cached }
    Copy-Item $cached $destination
}
$ipp = Join-Path $Cache 'ippicv-2022.2.0.zip'
if (-not (Test-Path $ipp)) {
    Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/opencv/opencv_3rdparty/c934a2a15a6df020446ac3dfa07e3acf72b63a8f/ippicv/ippicv_2022.2.0_win_intel64_20250730_general.zip' -OutFile $ipp
}
if ((Get-FileHash $ipp -Algorithm MD5).Hash -ne '7c0973976ab0716bc33f03a76a50017f') { throw 'OpenCV IPP package hash mismatch' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($ipp)
try {
    foreach ($entry in $zip.Entries | Where-Object FullName -Match '(?i)(license|copying|eula|copyright|third.?party).*\.(txt|rtf|html|md)$') {
        $target = Join-Path $destination ('IPP-' + $entry.FullName.Replace('/','-'))
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
} finally { $zip.Dispose() }
# Distribution terms for app-local VC runtime, from the compiler vendor.
$vc = Join-Path $nativeCache 'Microsoft-VC-Runtime-License.html'
if (-not (Test-Path $vc)) {
    Invoke-WebRequest -UseBasicParsing 'https://visualstudio.microsoft.com/license-terms/vs2022-cruntime/' -OutFile $vc
}
Copy-Item $vc (Join-Path $PublishDir 'Licenses')
$xmp = Join-Path $nativeCache 'XMP-Adobe-BSD-LICENSE.txt'
if (-not (Test-Path $xmp)) {
    Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/adobe/XMP-Toolkit-SDK/main/LICENSE' -OutFile $xmp
}
Copy-Item $xmp (Join-Path $PublishDir 'Licenses')
Write-Host 'Collected native runtime and third-party notices.'
