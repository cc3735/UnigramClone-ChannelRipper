[CmdletBinding()]
param(
    [string]$PackageRoot = $PSScriptRoot,
    [string]$PackageName = '38833FF26BA1D.UnigramPreview'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $PackageRoot)) {
    throw "PackageRoot does not exist: $PackageRoot"
}

$bundle = Get-ChildItem -Path $PackageRoot -Filter *.msixbundle -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $bundle) {
    throw "No .msixbundle found under $PackageRoot"
}

$cert = Get-ChildItem -Path $PackageRoot -Filter *.cer -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

$dependencies = Get-ChildItem -Path (Join-Path $PackageRoot 'Dependencies\x64') -Filter *.appx -File -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName

if ($cert) {
    Write-Host "Importing signing certificate to CurrentUser\\TrustedPeople..."
    Import-Certificate -FilePath $cert.FullName -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
}

$existing = Get-AppxPackage | Where-Object { $_.Name -eq $PackageName }
if ($existing) {
    Write-Host "Removing existing package: $($existing.PackageFullName)"
    Remove-AppxPackage -Package $existing.PackageFullName
}

Write-Host "Installing $($bundle.Name)..."
if ($dependencies) {
    Add-AppxPackage -Path $bundle.FullName -DependencyPath $dependencies -ForceApplicationShutdown
}
else {
    Add-AppxPackage -Path $bundle.FullName -ForceApplicationShutdown
}

Write-Host "Install complete."
