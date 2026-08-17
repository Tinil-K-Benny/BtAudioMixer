[CmdletBinding()]
param(
    [string]$ExeDir,
    [string]$MakeAppxPath,
    [string]$SignToolPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "`nThis script needs Administrator privileges to install the self-signed certificate." -ForegroundColor Yellow
    Write-Host "Please approve the UAC prompt..." -ForegroundColor Yellow
    Start-Process powershell -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`" -ExeDir `"$ExeDir`" -MakeAppxPath `"$MakeAppxPath`" -SignToolPath `"$SignToolPath`"" -Verb RunAs
    exit
}

$scriptDir  = $PSScriptRoot
$projectDir = Split-Path $scriptDir -Parent

if (-not $ExeDir) {
    $ExeDir = Join-Path $projectDir "bin\Debug\net8.0-windows10.0.19041.0"
}

$ExeDir = [IO.Path]::GetFullPath($ExeDir)

if (-not (Test-Path (Join-Path $ExeDir "BtAudioMixer.exe"))) {
    Write-Error ("BtAudioMixer.exe not found in '$ExeDir'.`n" +
                 "Build the project first (Ctrl+Shift+B in Visual Studio), then re-run this script.")
}

$manifestSrc = Join-Path $scriptDir "AppxManifest.xml"
$workDir     = Join-Path $scriptDir "_build"
$msixPath    = Join-Path $workDir   "BtAudioMixer.msix"
$pfxPath     = Join-Path $workDir   "BtAudioMixer.pfx"

if (Test-Path $workDir) { Remove-Item $workDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

$manifestDst = Join-Path $workDir "AppxManifest.xml"
Copy-Item $manifestSrc $manifestDst -Force

$assetsWork = Join-Path $workDir "Assets"
New-Item -ItemType Directory -Force -Path $assetsWork | Out-Null
$assetsSrc = Join-Path $scriptDir "Assets"
if (Test-Path $assetsSrc) {
    Copy-Item (Join-Path $assetsSrc "*") $assetsWork -Force
}

function Find-SdkTool([string]$ToolName) {
    $sdkBase = "C:\Program Files (x86)\Windows Kits\10\bin"
    $versions = Get-ChildItem $sdkBase -Directory |
                Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
                Sort-Object { [Version]$_.Name } -Descending
    foreach ($ver in $versions) {
        $path = Join-Path $ver.FullName "x64\$ToolName"
        if (Test-Path $path) { return $path }
    }
    throw "Could not find '$ToolName' in Windows SDK under '$sdkBase'. " +
          "Install the Windows 10 SDK via Visual Studio Installer."
}

$makeAppx = if ($MakeAppxPath -and (Test-Path $MakeAppxPath)) { $MakeAppxPath } else { Find-SdkTool "makeappx.exe" }
$signTool = if ($SignToolPath -and (Test-Path $SignToolPath)) { $SignToolPath } else { Find-SdkTool "signtool.exe" }

Write-Host "Using SDK tools from: $(Split-Path $makeAppx -Parent)" -ForegroundColor Cyan

$certSubject = "CN=BtAudioMixer"
$existingCert = Get-ChildItem Cert:\LocalMachine\My |
                Where-Object { $_.Subject -eq $certSubject } |
                Sort-Object NotAfter -Descending |
                Select-Object -First 1

if ($existingCert -and $existingCert.NotAfter -gt (Get-Date).AddDays(30)) {
    Write-Host "Reusing existing certificate (thumbprint: $($existingCert.Thumbprint), expires: $($existingCert.NotAfter.ToString('yyyy-MM-dd')))" -ForegroundColor Green
    $cert = $existingCert
} else {
    Write-Host "Creating new self-signed certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate `
        -Subject        $certSubject `
        -CertStoreLocation "Cert:\LocalMachine\My" `
        -Type           CodeSigningCert `
        -KeyUsage       DigitalSignature `
        -HashAlgorithm  SHA256 `
        -NotAfter       (Get-Date).AddYears(5)
    Write-Host "Certificate created (thumbprint: $($cert.Thumbprint))" -ForegroundColor Green
}

$certPwd = [securestring]::new()
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $certPwd | Out-Null

foreach ($store in @("TrustedPeople", "Root")) {
    $s = [System.Security.Cryptography.X509Certificates.X509Store]::new(
             $store,
             [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    $s.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $s.Add($cert)
    $s.Close()
    Write-Host "Trusted cert in LocalMachine\$store" -ForegroundColor Gray
}

Write-Host "`nPacking manifest into .msix..." -ForegroundColor Cyan
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }

& $makeAppx pack /d $workDir /p $msixPath /nv 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed (exit $LASTEXITCODE)." }

Write-Host "Signing .msix..." -ForegroundColor Cyan
& $signTool sign /fd SHA256 /a /f $pfxPath $msixPath 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) { throw "SignTool failed (exit $LASTEXITCODE)." }

$pkgName = "BtAudioMixer.SparsePackage"
$existing = Get-AppxPackage -Name $pkgName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Removing previous registration ($($existing.PackageFullName))..." -ForegroundColor Yellow
    Remove-AppxPackage $existing.PackageFullName
}

Write-Host "Registering sparse package..." -ForegroundColor Cyan
Write-Host "  Manifest : $manifestSrc"
Write-Host "  Exe dir  : $ExeDir"

$exeAssetsDir = Join-Path $ExeDir "Assets"
if (-not (Test-Path $exeAssetsDir)) {
    New-Item -ItemType Directory -Force -Path $exeAssetsDir | Out-Null
}
Copy-Item (Join-Path $scriptDir "Assets\*") $exeAssetsDir -Force

Add-AppxPackage `
    -Path             $msixPath `
    -ExternalLocation $ExeDir

$registered = Get-AppxPackage -Name $pkgName -ErrorAction SilentlyContinue
if ($registered) {
    Write-Host "`n✓ Sparse package registered successfully!" -ForegroundColor Green
    Write-Host "  PackageFullName : $($registered.PackageFullName)" -ForegroundColor Green
    Write-Host "  ExternalLocation: $ExeDir" -ForegroundColor Green
    Write-Host "`nYou can now launch BtAudioMixer.exe normally and click Connect — AudioPlaybackConnection will work." -ForegroundColor Cyan
} else {
    Write-Error "Registration appeared to complete but the package is not visible via Get-AppxPackage. Check the Windows Event Log (Application) for DISM/AppX errors."
}
