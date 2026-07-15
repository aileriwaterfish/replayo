# Télécharge ffmpeg (build essentials gyan.dev) dans tools\ffmpeg\ — à lancer une fois.
$ErrorActionPreference = "Stop"
$dest = Join-Path $PSScriptRoot "..\tools\ffmpeg"
New-Item -ItemType Directory -Force $dest | Out-Null
$zip = Join-Path $env:TEMP "ffmpeg-release-essentials.zip"
Invoke-WebRequest "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip" -OutFile $zip
Expand-Archive $zip (Join-Path $env:TEMP "ffmpeg-x") -Force
$exe = Get-ChildItem (Join-Path $env:TEMP "ffmpeg-x") -Recurse -Filter ffmpeg.exe | Select-Object -First 1
Copy-Item $exe.FullName (Join-Path $dest "ffmpeg.exe") -Force
& (Join-Path $dest "ffmpeg.exe") -version
