<#
.SYNOPSIS
    Build MSIX packages for x64 and ARM64, and a bundle over both.

.DESCRIPTION
    Publishes the app per architecture, lays each publish folder out with the manifest and the
    assets, and calls makeappx. The result is `dist/Astrid-<version>.msixbundle` plus the two
    single-architecture packages it was made from.

    UNSIGNED. A package has to be signed before Windows will install it, and signing is a
    deliberate act with a real certificate — see docs/ASTRID.md and the approvals section of
    CLAUDE.md. This script stops one step short of that on purpose.

    makeappx comes from the Windows SDK build tools the app already depends on, so there is nothing
    extra to install.

    This file is saved UTF-8 WITH a byte order mark. Windows PowerShell 5.1 reads a .ps1 without
    one as the system codepage, which turns any non-ASCII character in a string into a parse error —
    and the error it prints names a line nowhere near the real one.

.PARAMETER Version
    Four parts, as MSIX requires: 0.1.0.0. The last must be 0 for a Store submission.
#>
param(
    [string]$Version = '0.1.0.0',
    [string[]]$Architectures = @('x64', 'arm64'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must have four parts, e.g. 0.1.0.0 — MSIX will not accept anything else."
}

$makeappx = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" `
    -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Select-Object -First 1
if (-not $makeappx) {
    throw "makeappx.exe was not found. Restore the app project first: it comes with Microsoft.Windows.SDK.BuildTools."
}

$dist = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$packages = @()

foreach ($architecture in $Architectures) {
    $rid = "win-$architecture"
    Write-Host ""
    Write-Host "[*] publishing $rid" -ForegroundColor Cyan
    $publish = Join-Path $repoRoot "dist\publish\$rid"
    Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue

    # Self-contained, so the package carries its own runtime: a person installing from the Store
    # should not then be told to install .NET.
    dotnet publish (Join-Path $repoRoot 'app/Astrid.App/Astrid.App.csproj') `
        -c $Configuration -r $rid --self-contained true -o $publish | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }

    # The manifest, with this architecture and version in it.
    $manifest = Get-Content (Join-Path $repoRoot 'packaging/AppxManifest.xml') -Raw
    $manifest = $manifest.Replace('{VERSION}', $Version).Replace('{ARCHITECTURE}', $architecture)
    Set-Content -Path (Join-Path $publish 'AppxManifest.xml') -Value $manifest -Encoding utf8

    Copy-Item -Recurse -Force (Join-Path $repoRoot 'packaging/Assets') (Join-Path $publish 'Assets')

    $package = Join-Path $dist "Astrid-$Version-$architecture.msix"
    Remove-Item -Force $package -ErrorAction SilentlyContinue
    & $makeappx.FullName pack /d $publish /p $package /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed for $architecture" }
    Write-Host "[+] $package" -ForegroundColor Green
    $packages += $package
}

if ($packages.Count -gt 1) {
    # One bundle, both architectures: Windows installs the right one, which is the question the
    # M0 spike asked about ARM64.
    $bundleInput = Join-Path $dist 'bundle'
    Remove-Item -Recurse -Force $bundleInput -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $bundleInput | Out-Null
    foreach ($package in $packages) { Copy-Item $package $bundleInput }

    $bundle = Join-Path $dist "Astrid-$Version.msixbundle"
    Remove-Item -Force $bundle -ErrorAction SilentlyContinue
    & $makeappx.FullName bundle /d $bundleInput /p $bundle /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed" }
    Write-Host ""
    Write-Host "[+] $bundle" -ForegroundColor Green
}

Write-Host ""
Write-Host "Unsigned. Signing is a separate, deliberate act — see CLAUDE.md." -ForegroundColor Yellow
