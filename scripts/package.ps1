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
    certificate trusted anywhere. Before the Store listing exists, scripts/sign-local.ps1 signs a
    COPY of this output with a self-signed test certificate for a local install; see packaging/README.md.

    makeappx comes from the Windows SDK build tools the app already depends on, so there is nothing
    extra to install.

    This file is saved UTF-8 WITH a byte order mark. Windows PowerShell 5.1 reads a .ps1 without
    one as the system codepage, which turns any non-ASCII character in a string into a parse error —
    and the error it prints names a line nowhere near the real one.

.PARAMETER Version
    Four parts, as MSIX requires: 0.1.0.0. The last must be 0 for a Store submission.

.PARAMETER Brand
    A brand profile — a name resolved against packaging/brands/ then ../astrid-web/brands/, or a
    path to a *.brand.json file. It sets the display name, the publisher display name, the
    description, the URL scheme, the default server and the tile set.

    OMITTED MEANS HOSTED ASTRID, byte for byte as before this parameter existed. That is asserted
    by BrandProfileTests rather than left to care: the whole point of a brand profile is that
    Astrid's own build does not notice it.

    See scripts/lib/brand-profile.ps1 for the mapping and packaging/brands/README.md for how to add
    a partner.

.EXAMPLE
    powershell -File scripts/package.ps1 -Brand acme
#>
param(
    [string]$Version = '0.1.0.0',
    [string[]]$Architectures = @('x64', 'arm64'),
    [string]$Configuration = 'Release',
    [string]$Brand = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Runs the native tools below so that a failure prints the tool's own diagnosis. It also contains
# the reason this is not done inline: under the Stop above, a redirected native stderr is a
# terminating error even on a zero exit, so a tool that merely warns would fail the build.
. (Join-Path $PSScriptRoot 'lib\invoke-build-tool.ps1')

# Which brand this package is. Everything a partner can change comes from here, and with no -Brand
# every value is today's Astrid one.
. (Join-Path $PSScriptRoot 'lib\brand-profile.ps1')

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

$brand = Get-AstridBrandProfile -Brand $Brand -RepoRoot $repoRoot
if ($Brand) {
    Write-Host ''
    Write-Host "[*] brand: $($brand.Name) - $($brand.DisplayName), $($brand.Scheme)://, $($brand.ServerUrl)" -ForegroundColor Cyan
}

# The identity Partner Center assigned. Substituted rather than written into the manifest, because
# a package whose Identity differs from the reservation by one character is rejected at upload.
#
# A brand profile may carry its own, because the reservation belongs to the PARTNER's Partner
# Center account: store-identity.json is this repo's, and a partner cannot edit it.
$identityFile = Join-Path $repoRoot 'packaging/store-identity.json'
$identity = Get-Content $identityFile -Raw | ConvertFrom-Json
$identityName = if ($brand.IdentityName) { $brand.IdentityName } else { $identity.identityName }
$identityPublisher = if ($brand.IdentityPublisher) { $brand.IdentityPublisher } else { $identity.identityPublisher }

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
    # The brand values the RUNNING app needs, as build properties: the manifest is read by Windows,
    # not by the app, so a package with a partner's manifest over a default build would show the
    # partner's name in Start and then sign in to astrid.cc. ASTRID_SERVER_URL still overrides the
    # baked-in server at runtime - see ServerSelection - which is what makes a partner build
    # testable against a staging host.
    Invoke-BuildTool "publish failed for $rid" {
        dotnet publish (Join-Path $repoRoot 'app/Astrid.App/Astrid.App.csproj') `
            -c $Configuration -r $rid --self-contained true -p:AstridPackaged=true `
            "-p:AstridDefaultServerUrl=$($brand.ServerUrl)" `
            "-p:AstridUrlScheme=$($brand.Scheme)" `
            "-p:AstridBrandShortName=$($brand.ShortName)" `
            -o $publish
    }

    # AstridPackaged=true above leaves WindowsPackageType unset, which is how the SDK is told to
    # build for MSIX. Without it these are unpackaged binaries, and the astrid:// scheme is written
    # into HKCU at startup instead of coming from the manifest — see Program.cs.

    # The manifest, with this architecture, version, identity and brand in it.
    #
    # The brand's text is XML-escaped: a partner's name can hold an ampersand ("Smith & Sons"),
    # and makeappx rejects the whole manifest for one. The scheme is not, because the loader
    # already refuses anything outside [a-z0-9-].
    $manifest = Get-Content (Join-Path $repoRoot 'packaging/AppxManifest.xml') -Raw
    $manifest = $manifest.Replace('{VERSION}', $Version).Replace('{ARCHITECTURE}', $architecture)
    $manifest = $manifest.Replace('{IDENTITY_NAME}', $identityName)
    $manifest = $manifest.Replace('{IDENTITY_PUBLISHER}', $identityPublisher)
    $manifest = $manifest.Replace('{DISPLAY_NAME}', [System.Security.SecurityElement]::Escape($brand.DisplayName))
    $manifest = $manifest.Replace('{PUBLISHER_DISPLAY_NAME}', [System.Security.SecurityElement]::Escape($brand.PublisherDisplayName))
    $manifest = $manifest.Replace('{DESCRIPTION}', [System.Security.SecurityElement]::Escape($brand.Description))
    $manifest = $manifest.Replace('{SHORT_NAME}', [System.Security.SecurityElement]::Escape($brand.ShortName))
    $manifest = $manifest.Replace('{SCHEME}', $brand.Scheme)
    $manifest = $manifest.Replace('{SCHEME_DISPLAY_NAME}', [System.Security.SecurityElement]::Escape($brand.SchemeDisplayName))
    Set-Content -Path (Join-Path $publish 'AppxManifest.xml') -Value $manifest -Encoding utf8

    # The Store tiles go INTO the published Assets folder, alongside the app's own.
    #
    # `Copy-Item -Recurse <src> <dest>` nests when <dest> already EXISTS: it copies the folder
    # rather than its contents. The app grew an Assets folder of its own (the icons and the
    # checkbox art), so from that day this wrote Assets\Assets\SplashScreen.png while the manifest
    # asked for Assets\SplashScreen.png. The package still built, still bundled and still signed,
    # and then failed at INSTALL with 0x80073CF6, "the splash screen image cannot be located" -
    # which nobody saw, because nobody had ever installed it. Found on the first real install,
    # 2026-09-27. Copying the CONTENTS merges the two folders, which is what was always meant.
    # From the BRAND's tile set, which is packaging/Assets unless a profile names another. A
    # partner's icons are the one thing substitution cannot do: they are files, not strings.
    $assetsDest = Join-Path $publish 'Assets'
    New-Item -ItemType Directory -Force -Path $assetsDest | Out-Null
    Copy-Item -Recurse -Force (Join-Path $brand.AssetsDirectory '*') $assetsDest

    # Every asset the manifest names has to be where it says it is. Windows only checks at install
    # time, names one missing file, and stops - so a layout mistake costs a full build, a signature
    # and an elevated shell to discover. Three seconds here instead.
    # Matches both spellings the manifest uses: an attribute (Square150x150Logo="Assets\x.png")
    # and an element (<Logo>Assets\StoreLogo.png</Logo>). StoreLogo is ONLY ever an element, so an
    # attribute-only pattern would quietly not check it.
    $referenced = [regex]::Matches($manifest, 'Assets\\[^"<>\s]+') |
        ForEach-Object { $_.Value } | Sort-Object -Unique
    $missing = @($referenced | Where-Object { -not (Test-Path (Join-Path $publish $_)) })
    if ($missing.Count -gt 0) {
        throw "the manifest names $($missing.Count) asset(s) the package layout does not contain:`n  " +
            ($missing -join "`n  ")
    }

    $package = Join-Path $dist "Astrid-$Version-$architecture.msix"
    Remove-Item -Force $package -ErrorAction SilentlyContinue
    Invoke-BuildTool "makeappx pack failed for $architecture" {
        & $makeappx.FullName pack /d $publish /p $package /o
    }
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
    Invoke-BuildTool 'makeappx bundle failed' {
        & $makeappx.FullName bundle /d $bundleInput /p $bundle /o
    }
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
