param(
    [Parameter(Mandatory=$true)][string]$PublishDir,
    [Parameter(Mandatory=$true)][string]$AssetsFile,
    [Parameter(Mandatory=$true)][string]$Cache
)
$ErrorActionPreference = 'Stop'
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json
$licenses = Join-Path $PublishDir 'Licenses'
New-Item -ItemType Directory -Force "$licenses\Packages","$licenses\Texts","$Cache\license-texts" | Out-Null
$texts = @('Apache-2.0','MIT','MS-PL','BSD-2-Clause','BSD-3-Clause','LGPL-2.1-or-later','LGPL-3.0-or-later','GPL-3.0-or-later','Artistic-1.0-Perl','CC0-1.0')
foreach ($text in $texts) {
    $cached = "$Cache\license-texts\$text.txt"
    if (-not (Test-Path -LiteralPath $cached)) {
        Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/spdx/license-list-data/v3.27.0/text/$text.txt" -OutFile $cached
    }
    Copy-Item -LiteralPath $cached -Destination "$licenses\Texts"
}
$cc = "$Cache\license-texts\CC-BY-NC-SA-4.0.txt"
if (-not (Test-Path $cc)) {
    Invoke-WebRequest -UseBasicParsing 'https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.txt' -OutFile $cc
}
Copy-Item $cc "$licenses\Texts"
$inventory = @()
foreach ($property in $assets.libraries.PSObject.Properties) {
    $library = $property.Value
    if ($library.type -ne 'package') { continue }
    $packageRoot = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $library.path
        if (Test-Path -LiteralPath $candidate) { $packageRoot = $candidate; break }
    }
    if (-not $packageRoot) { throw "Missing restored package: $($property.Name)" }
    $nuspec = Get-ChildItem -LiteralPath $packageRoot -Filter '*.nuspec' -File | Select-Object -First 1
    [xml]$document = Get-Content -LiteralPath $nuspec.FullName -Raw
    $metadata = $document.package.metadata
    $destination = Join-Path "$licenses\Packages" ($property.Name.Replace('/','-'))
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item -LiteralPath $nuspec.FullName -Destination $destination
    $packageLicense = $metadata.license
    if ($packageLicense -is [string]) { $licenseText = $packageLicense } else { $licenseText = $packageLicense.InnerText }
    foreach ($file in (Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|THIRD.?PARTY)' })) {
        $relative = $file.FullName.Substring($packageRoot.Length + 1)
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    $inventory += [pscustomobject]@{ Package=$property.Name; Authors=[string]$metadata.authors; Copyright=[string]$metadata.copyright; License=$licenseText; LicenseUrl=[string]$metadata.licenseUrl; ProjectUrl=[string]$metadata.projectUrl }
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content "$licenses\package-inventory.json" -Encoding UTF8
# Keep the runtime's license files that are not necessarily publish content items.
foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $root = Join-Path $folder $runtime
        if (Test-Path $root) {
            foreach ($file in (Get-ChildItem $root -Recurse -File | Where-Object Name -Match '^(LICENSE|THIRD.?PARTY)')) {
                $dest = "$licenses\Microsoft-$runtime"
                New-Item -ItemType Directory -Force $dest | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $dest
            }
        }
    }
}
Write-Host "Recorded notices for $($inventory.Count) packages."
