$ErrorActionPreference = "SilentlyContinue"
$asm = [Reflection.Assembly]::LoadFrom("C:\apps\timelaps_ex\Timelapse\src\Timelapse\bin\Debug\net10.0-windows\win-x64\FlyleafLib.dll")
try { $types = $asm.GetTypes() } catch { $types = $_.Exception.Types | Where-Object { $_ -ne $null } }
$playerType = $types | Where-Object Name -eq "Player"
$playerType.GetMethods() | Select-Object Name | Out-File methods.txt
