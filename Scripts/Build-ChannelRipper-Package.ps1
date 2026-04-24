[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64')]
    [string]$Platform = 'x64',
    [switch]$EnableCalls,
    [switch]$StageForTransfer = $true,
    [switch]$SignPackage
)

$ErrorActionPreference = 'Stop'

# Ensure vswhere and gperf are on PATH for ILCompiler and TDLib builds
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;C:\Users\023du\AppData\Local\Microsoft\WinGet\Packages\oss-winget.gperf_Microsoft.Winget.Source_8wekyb3d8bbwe;$env:PATH"

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

if ($SignPackage) {
    $signtool = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe'
    if (-not (Test-Path $signtool)) {
        throw "signtool.exe not found at $signtool"
    }

    $certificate = Get-ChildItem -Path $latestPackageFolder.FullName -Filter *.pfx -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $certificate) {
        Write-Warning "No .pfx found in $($latestPackageFolder.FullName). Skipping signing."
    }
    else {
        $msix = Get-ChildItem -Path $latestPackageFolder.FullName -Filter *.msix -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($msix) {
            & $signtool sign /fd SHA256 /f $certificate.FullName $msix.FullName
            if ($LASTEXITCODE -ne 0) {
                throw "Signing $($msix.FullName) failed."
            }
        }
    }
}

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

    $docs = @(
        'Documentation\Channel-Ripper.md',
        'Documentation\Channel-Ripper-User-Guide.md',
        'Documentation\Channel-Ripper-Setup.md',
        'Documentation\Channel-Ripper-Install.md',
        'Documentation\Channel-Ripper-Branding.md',
        'migration.md'
    )

    foreach ($doc in $docs) {
        $source = Join-Path $repoRoot $doc
        if (Test-Path $source) {
            Copy-Item $source (Join-Path $distRoot ([IO.Path]::GetFileName($source))) -Force
        }
    }

    Write-Host "Staged transfer folder: $distRoot"
}
