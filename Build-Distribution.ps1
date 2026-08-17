[CmdletBinding()]
param(
    [string]$OutputDir = "dist"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$dist = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "Publishing BtAudioMixer.exe..." -ForegroundColor Cyan
$appDir = Join-Path $dist "App"
dotnet publish (Join-Path $repoRoot "BtAudioMixer.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $appDir
if ($LASTEXITCODE -ne 0) { throw "Publishing BtAudioMixer.csproj failed." }

Write-Host "Copying SparsePackage..." -ForegroundColor Cyan
$sparsePackageDst = Join-Path $appDir "SparsePackage"
New-Item -ItemType Directory -Force -Path $sparsePackageDst | Out-Null
Copy-Item (Join-Path $repoRoot "SparsePackage\AppxManifest.xml") $sparsePackageDst -Force
Copy-Item (Join-Path $repoRoot "SparsePackage\Register-SparsePackage.ps1") $sparsePackageDst -Force
Copy-Item (Join-Path $repoRoot "SparsePackage\Unregister-SparsePackage.ps1") $sparsePackageDst -Force
Copy-Item (Join-Path $repoRoot "SparsePackage\Assets") (Join-Path $sparsePackageDst "Assets") -Recurse -Force

Write-Host "Publishing BtAudioMixer.Installer.exe..." -ForegroundColor Cyan
dotnet publish (Join-Path $repoRoot "Installer\BtAudioMixer.Installer.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $dist
if ($LASTEXITCODE -ne 0) { throw "Publishing BtAudioMixer.Installer.csproj failed." }

Write-Host "`nDistribution folder ready: $dist" -ForegroundColor Green
Write-Host "  Zip its contents and share it. End users run BtAudioMixer.Installer.exe from the extracted folder." -ForegroundColor Green
