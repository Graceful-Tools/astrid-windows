<#
.SYNOPSIS
    Build MSIX packages for x64 and ARM64, and a bundle over both.

.DESCRIPTION
    Publishes the app per architecture, lays each publish folder out with the manifest and the
    assets, and calls makeappx. The result is `dist/Astrid-<version>.msixbundle` plus the two
    single-architecture packages it was made from.

    UNSIGNED, AND THAT IS THE POINT. Astrid ships through the Microsoft Store, and the Store
    signs the package itself at submission — a certificate of ours would only be replaced by
    theirs. So the output here is what Partner Center wants AND what Windows will refuse to
    install directly; those are the same fact seen from two sides.

    To put a build on a machine for testing, use a Store package flight or a private audience
    rather than signing one by hand: a flight installs for named Microsoft accounts and needs no
    certificate trusted anywhere.

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

# The identity Partner Center assigned. Substituted rather than written into the manifest, because
# a package whose Identity differs from the reservation by one character is rejected at upload.
$identityFile = Join-Path $repoRoot 'packaging/store-identity.json'
$identity = Get-Content $identityFile -Raw | ConvertFrom-Json
$identityName = $identity.identityName
$identityPublisher = $identity.identityPublisher

if ($identityName -like 'REPLACE*' -or $identityPublisher -like 'REPLACE*') {
    # Obviously fake, so a package built before the reservation exists cannot be mistaken for one
    # that could be submitted — while still being buildable and WACK-testable today.
    $identityName = 'GracefulTools.Astrid.LOCALBUILD'
    $identityPublisher = 'CN=LOCAL BUILD - NOT FOR SUBMISSION'
    Write-Host ''
    Write-Host '!!  packaging/store-identity.json still holds placeholders.' -ForegroundColor Yellow
    Write-Host '    Building with a LOCAL identity; this package cannot be submitted.' -ForegroundColor Yellow
    Write-Host '    Fill it from Partner Center > Product management > Product identity.' -ForegroundColor Yellow
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
        -c $Configuration -r $rid --self-contained true -p:AstridPackaged=true -o $publish | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }

    # AstridPackaged=true above leaves WindowsPackageType unset, which is how the SDK is told to
    # build for MSIX. Without it these are unpackaged binaries, and the astrid:// scheme is written
    # into HKCU at startup instead of coming from the manifest — see Program.cs.

    # The manifest, with this architecture, version and identity in it.
    $manifest = Get-Content (Join-Path $repoRoot 'packaging/AppxManifest.xml') -Raw
    $manifest = $manifest.Replace('{VERSION}', $Version).Replace('{ARCHITECTURE}', $architecture)
    $manifest = $manifest.Replace('{IDENTITY_NAME}', $identityName)
    $manifest = $manifest.Replace('{IDENTITY_PUBLISHER}', $identityPublisher)
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

    # What Partner Center takes: a ZIP of the bundle, named .msixupload. A bare .msixbundle
    # uploads too, but the container is the documented shape and is where a symbol bundle goes
    # when there is one to ship.
    $upload = Join-Path $dist "Astrid-$Version.msixupload"
    # Compressed as .zip and renamed: Compress-Archive refuses any other extension, and the
    # container is a plain zip whatever it is called.
    $uploadZip = "$upload.zip"
    Remove-Item -Force $upload, $uploadZip -ErrorAction SilentlyContinue
    Compress-Archive -Path $bundle -DestinationPath $uploadZip -CompressionLevel Optimal
    Move-Item -Force $uploadZip $upload
    Write-Host "[+] $upload" -ForegroundColor Green
}

Write-Host ""
Write-Host "Unsigned, which is correct: the Store signs at submission." -ForegroundColor Cyan
if ($identityPublisher -like 'CN=LOCAL BUILD*') {
    Write-Host "LOCAL identity - fill packaging/store-identity.json before submitting." -ForegroundColor Yellow
}
