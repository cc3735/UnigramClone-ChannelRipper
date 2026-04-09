[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('x64')]
    [string]$Platform = 'x64',
    [switch]$EnableCalls,
    [switch]$StageForTransfer = $true
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$msbuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'

if (-not (Test-Path $msbuild)) {
    throw "MSBuild not found at $msbuild"
}

$enableCallsValue = if ($EnableCalls) { 'true' } else { 'false' }
$solutionDir = "$repoRoot\"

Write-Host "Building Telegram app ($Configuration, $Platform, EnableCalls=$enableCallsValue)..."
& $msbuild "$repoRoot\Telegram\Telegram.csproj" /p:Configuration=$Configuration /p:Platform=$Platform /p:EnableCalls=$enableCallsValue
if ($LASTEXITCODE -ne 0) {
    throw "Telegram app build failed."
}

Write-Host "Building MSIX package ($Configuration, $Platform, EnableCalls=$enableCallsValue)..."
& $msbuild "$repoRoot\Telegram.Msix\Telegram.Msix.wapproj" /t:Build /p:Configuration=$Configuration /p:Platform=$Platform /p:EnableCalls=$enableCallsValue /p:SolutionDir="$solutionDir"
if ($LASTEXITCODE -ne 0) {
    throw "MSIX package build failed."
}

$packageRoot = Join-Path $repoRoot 'Telegram.Msix\AppPackages'
$folderPattern = if ($Configuration -eq 'Release') { 'Telegram.Msix_*_Test' } else { 'Telegram.Msix_*_Debug_Test' }
$latestPackageFolder = Get-ChildItem $packageRoot -Directory -Filter $folderPattern |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $latestPackageFolder) {
    throw "Could not find a generated AppPackages folder under $packageRoot matching $folderPattern"
}

Write-Host "Latest package folder: $($latestPackageFolder.FullName)"

if ($StageForTransfer) {
    $distRoot = Join-Path $repoRoot 'dist\ChannelRipper-Installer'
    if (Test-Path $distRoot) {
        Remove-Item $distRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $distRoot | Out-Null
    Copy-Item -Path (Join-Path $latestPackageFolder.FullName '*') -Destination $distRoot -Recurse -Force

    $wrapper = Join-Path $repoRoot 'Scripts\Install-ChannelRipper-Package.ps1'
    if (Test-Path $wrapper) {
        Copy-Item $wrapper (Join-Path $distRoot 'Install-ChannelRipper-Package.ps1') -Force
    }

    Write-Host "Staged transfer folder: $distRoot"
}
