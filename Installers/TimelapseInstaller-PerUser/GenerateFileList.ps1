param(
    [Parameter(Mandatory=$true)][string]$SourceDir,
    [string]$OutputFile = "$PSScriptRoot\Files.wxs"
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $SourceDir).Path
$sha = [System.Security.Cryptography.SHA256]::Create()
function Id-For([string]$value) {
    'P' + ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($value.ToLowerInvariant())))).Replace('-','').Substring(0,32)
}
function Escape-Xml([string]$value) { [System.Security.SecurityElement]::Escape($value) }
$xml = [Text.StringBuilder]::new()
[void]$xml.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment>')
$directories = @('') + @(Get-ChildItem -LiteralPath $root -Directory -Recurse | ForEach-Object { $_.FullName.Substring($root.Length + 1) })
foreach ($relative in $directories) {
    if ($relative -eq '') { continue }
    $parent = Split-Path $relative -Parent
    $parentId = if ($parent) { Id-For $parent } else { 'INSTALLFOLDER' }
    $id = Id-For $relative
    $name = Escape-Xml (Split-Path $relative -Leaf)
    [void]$xml.AppendLine("<DirectoryRef Id=`"$parentId`"><Directory Id=`"$id`" Name=`"$name`" /></DirectoryRef>")
}
[void]$xml.AppendLine('<ComponentGroup Id="ProductComponents" Directory="INSTALLFOLDER">')
foreach ($file in (Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName)) {
    $relative = $file.FullName.Substring($root.Length + 1)
    $id = Id-For $relative
    $componentGuid = ([guid]::ParseExact($id.Substring(1), 'N')).ToString()
    $directory = Split-Path $relative -Parent
    $directoryId = if ($directory) { Id-For $directory } else { 'INSTALLFOLDER' }
    $source = Escape-Xml $relative
    [void]$xml.AppendLine("<Component Id=`"C$id`" Guid=`"$componentGuid`" Directory=`"$directoryId`"><File Id=`"F$id`" Source=`"`$(var.SourceDir)\$source`" /><RegistryValue Root=`"HKCU`" Key=`"Software\kaki200\TimelapseFork\Components`" Name=`"$id`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" /></Component>")
}
foreach ($relative in $directories) {
    $id = if ($relative) { Id-For $relative } else { 'INSTALLFOLDER' }
    [void]$xml.AppendLine("<Component Id=`"Remove$id`" Guid=`"*`" Directory=`"$id`"><RemoveFolder Id=`"R$id`" On=`"uninstall`" /><RegistryValue Root=`"HKCU`" Key=`"Software\kaki200\TimelapseFork\Directories`" Name=`"$id`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" /></Component>")
}
[void]$xml.AppendLine('</ComponentGroup></Fragment></Wix>')
[IO.File]::WriteAllText($OutputFile, $xml.ToString(), [Text.UTF8Encoding]::new($false))
Write-Host "Generated installer file list: $OutputFile"
