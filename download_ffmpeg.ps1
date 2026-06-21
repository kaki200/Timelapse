$url = "https://github.com/GyanD/codexffmpeg/releases/download/7.1/ffmpeg-7.1-full_build-shared.zip"
$zipFile = "ffmpeg.zip"
$outFolder = "C:\apps\timelaps_ex\Timelapse\src\Timelapse\bin\Debug\net10.0-windows\win-x64\FFmpeg"

Write-Host "Downloading..."
Invoke-WebRequest -Uri $url -OutFile $zipFile

Write-Host "Extracting..."
Expand-Archive -Path $zipFile -DestinationPath "ffmpeg_temp" -Force

Write-Host "Copying DLLs..."
New-Item -ItemType Directory -Force -Path $outFolder
Copy-Item -Path "ffmpeg_temp\*\bin\*.dll" -Destination $outFolder -Force

Write-Host "Cleaning up..."
Remove-Item -Path $zipFile -Force
Remove-Item -Path "ffmpeg_temp" -Recurse -Force

Write-Host "Done."
