param(
    [switch]$SkipPrepare,
    [string]$OutputDirectory = "$PSScriptRoot\..\bin\Release"
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$cache = Join-Path $repo '.installer-cache'
if (-not $SkipPrepare) { & "$PSScriptRoot\..\PrepareDependencies.ps1" -Cache $cache }
$stage = Join-Path $repo ('.installer-build\build-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$publish = Join-Path $stage 'publish'
$payload = Join-Path $cache 'payload'
if (-not (Test-Path "$payload\FFmpeg\ffmpeg.exe")) { throw 'Run PrepareDependencies.ps1 first' }
New-Item -ItemType Directory -Force $stage,$publish,$OutputDirectory | Out-Null
Push-Location $repo
try {
    & dotnet publish src\Timelapse\Timelapse.csproj -p:PublishProfile=SelfContained-win-x64 -p:Configuration=Release `
        "-p:RestorePackagesPath=$repo\.nuget-packages" "-p:BundledDependenciesDir=$payload" "-p:PublishDir=$publish\"
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed' }
    Copy-Item "$payload\Flyleaf.FFmpeg.Bindings.dll" "$publish\Flyleaf.FFmpeg.Bindings.dll" -Force
    Copy-Item "$repo\LICENSE.md","$PSScriptRoot\..\THIRD-PARTY-NOTICES.md" $publish -Force
    # This optional OpenCV plugin is never used by motion analysis. Do not convey a second FFmpeg build.
    Get-ChildItem -LiteralPath $publish -Filter 'opencv_videoio_ffmpeg*.dll' -Recurse -File | Remove-Item -Force
    foreach ($unwanted in @('NReco.VideoConverter.dll','ffmpeg.exe','Flyleaf.FFmpeg.dll','SixLabors.ImageSharp.dll','SixLabors.ImageSharp.Drawing.dll')) {
        if (Test-Path (Join-Path $publish $unwanted)) { throw "Unapproved binary in staging: $unwanted" }
    }
    $required = @('Timelapse.exe','Timelapse-ViewOnly.exe','TimelapseTemplateEditor.exe','coreclr.dll','hostfxr.dll',
        'PresentationFramework.dll','FFmpeg\ffmpeg.exe','FFmpeg\avcodec-61.dll','FFmpeg\avformat-61.dll',
        'exiftool.exe','exiftool_files','OpenCvSharpExtern.dll','LICENSE.md','THIRD-PARTY-NOTICES.md',
        'Licenses\Sources\ffmpeg-7.1.3.tar.xz','Licenses\Sources\Flyleaf-3b057fd-source.tar.gz','Licenses\Sources\Flyleaf-bindings-7.1-source.zip')
    foreach ($relative in $required) {
        if (-not (Test-Path (Join-Path $publish $relative))) { throw "Missing installer payload: $relative" }
    }
    & "$PSScriptRoot\..\WritePackageNotices.ps1" -PublishDir $publish -AssetsFile "$repo\src\Timelapse\obj\project.assets.json" -Cache $cache
    if ($LASTEXITCODE -ne 0) { throw 'Package notice collection failed' }
    & "$PSScriptRoot\..\WriteNativeNotices.ps1" -PublishDir $publish -Cache $cache
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo("$publish\Timelapse.exe").FileVersion
    $appVersion = [Version]$version
    # MSI compares three fields. Our 2.5.0.10 app is package 2.5.10.
    if ($appVersion.Build -ne 0) { throw 'Review the MSI version mapping before changing the application build field' }
    $msiVersion = '{0}.{1}.{2}' -f $appVersion.Major,$appVersion.Minor,$appVersion.Revision
    $msiName = "Timelapse-Kaki-$version-win-x64.msi"
    & "$PSScriptRoot\GenerateFileList.ps1" -SourceDir $publish -OutputFile "$stage\Files.wxs"
    Push-Location $PSScriptRoot
    try {
        & wix build Product.wxs "$stage\Files.wxs" -arch x64 -ext WixToolset.UI.wixext `
            -out "$OutputDirectory\$msiName" `
            -d "SourceDir=$publish" -d "ProductVersion=$msiVersion"
        if ($LASTEXITCODE -ne 0) { throw 'WiX MSI build failed' }
    } finally { Pop-Location }
    $msi = Join-Path $OutputDirectory $msiName
    Get-FileHash -LiteralPath $msi -Algorithm SHA256 | Format-List
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'latest-publish-path.txt'), $publish)
    Write-Host "Installer: $msi"
    Write-Host "Published app $version (self-contained): $publish"
} finally { Pop-Location }
