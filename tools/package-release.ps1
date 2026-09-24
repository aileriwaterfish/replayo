param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$racine = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projet = Join-Path $racine 'src\Replayo\Replayo.csproj'
$ffmpeg = Join-Path $racine 'tools\ffmpeg\ffmpeg.exe'
$paquets = Join-Path $racine 'dist\releases'
$publication = Join-Path $paquets "publish-v$Version"
$archive = Join-Path $paquets 'Replayo-win-x64.zip'
$sommes = Join-Path $paquets 'SHA256SUMS.txt'

if (-not (Test-Path -LiteralPath $ffmpeg -PathType Leaf)) {
    throw "ffmpeg.exe manque : $ffmpeg"
}
$versionProjet = ([xml](Get-Content -LiteralPath $projet -Raw)).Project.PropertyGroup.Version
if ($versionProjet -ne $Version) {
    throw "Version du projet $versionProjet différente de la version demandée $Version"
}

New-Item -ItemType Directory -Path $paquets -Force | Out-Null
dotnet publish $projet --configuration Release --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true --output $publication
if ($LASTEXITCODE -ne 0) { throw 'Échec de dotnet publish.' }

foreach ($fichier in @('Replayo.exe', 'ffmpeg.exe', 'assets\clip.wav', 'assets\replayo.ico', 'assets\tray.ico')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publication $fichier) -PathType Leaf)) {
        throw "Fichier absent du paquet : $fichier"
    }
}

if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -LiteralPath @(
    (Join-Path $publication 'Replayo.exe'),
    (Join-Path $publication 'ffmpeg.exe'),
    (Join-Path $publication 'assets')
) -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $sommes -Value "$hash  Replayo-win-x64.zip" -Encoding ascii
Write-Output "Paquet prêt : $archive"
Write-Output "Empreinte : $sommes"
