[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$pkgName = "BtAudioMixer.SparsePackage"
$pkg = Get-AppxPackage -Name $pkgName -ErrorAction SilentlyContinue

if (-not $pkg) {
    Write-Host "Package '$pkgName' is not currently registered — nothing to remove." -ForegroundColor Yellow
    return
}

Write-Host "Removing $($pkg.PackageFullName)..." -ForegroundColor Cyan
Remove-AppxPackage $pkg.PackageFullName
Write-Host "✓ Package removed." -ForegroundColor Green

$certSubject = "CN=BtAudioMixer"
foreach ($store in @("TrustedPeople", "Root", "My")) {
    $s = [System.Security.Cryptography.X509Certificates.X509Store]::new(
             $store,
             [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    $s.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $certs = $s.Certificates | Where-Object { $_.Subject -eq $certSubject }
    foreach ($c in $certs) {
        $s.Remove($c)
        Write-Host "Removed cert from CurrentUser\$store (thumbprint: $($c.Thumbprint))" -ForegroundColor Gray
    }
    $s.Close()
}
