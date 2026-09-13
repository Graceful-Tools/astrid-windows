<#
.SYNOPSIS
    Sign the local MSIX build with a self-signed TEST certificate so it can be installed on a
    development machine, and optionally install and launch it.

.DESCRIPTION
    scripts/package.ps1 produces UNSIGNED packages on purpose: Astrid ships through the Microsoft
    Store, which signs at submission. But an unsigned package cannot be installed anywhere, so the
    question "does the bundle install and start on this machine" needed a certificate of some kind.
    This script answers it with a self-signed one (approved by Jon on task edf273c3, 2026-09-13).

    What it does, in order:
      1. Reads the Publisher and Name from the manifest package.ps1 just built, because MSIX refuses
         a signature whose certificate Subject differs from the manifest Publisher by one byte.
      2. Finds or creates a self-signed code-signing certificate with that Subject in the current
         user's personal store (Cert:\CurrentUser\My). It never leaves the store as a .pfx.
      3. Trusts its PUBLIC half for sideloading by importing it into the Trusted People store of
         -TrustScope (CurrentUser by default, no elevation needed).
      4. Copies the per-architecture packages into dist/signed-local/, signs each, bundles them
         again (a signature changes the bytes, so the unsigned bundle cannot be reused), and signs
         the bundle. Everything at the top of dist/ stays unsigned, exactly as the Store wants it.
      5. With -Install, installs the bundle with Add-AppxPackage and reports which architecture
         Windows chose. With -Launch, starts the app from its packaged identity and checks that the
         process is alive a few seconds later.

    The certificate is the only change this makes outside the repository. -RemoveCertificate takes
    it out of both stores again, and -Uninstall removes the installed package.

    This file is ASCII only, so it reads the same under any codepage Windows PowerShell 5.1 picks.

.PARAMETER Version
    The version package.ps1 was run with. Defaults to its default.

.PARAMETER Architectures
    Which single-architecture packages to sign and bundle. Defaults to both, like package.ps1.

.PARAMETER TrustScope
    Where the public certificate is trusted: CurrentUser (default) or LocalMachine. LocalMachine
    needs an elevated shell and the script says so rather than failing halfway.

.PARAMETER Install
    Install the signed bundle after signing it.

.PARAMETER Launch
    After installing, start the app and confirm its process is running.

.PARAMETER Uninstall
    Remove the installed package and stop. Nothing is signed.

.PARAMETER RemoveCertificate
    Remove the test certificate from the personal and Trusted People stores and stop. Nothing is
    signed.

.EXAMPLE
    powershell -File scripts/package.ps1 -Version 0.1.0.0
    powershell -File scripts/sign-local.ps1 -Version 0.1.0.0 -Install -Launch

.EXAMPLE
    powershell -File scripts/sign-local.ps1 -Uninstall
    powershell -File scripts/sign-local.ps1 -RemoveCertificate
#>
param(
    [string]$Version = '0.1.0.0',
    [string[]]$Architectures = @('x64', 'arm64'),
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$TrustScope = 'CurrentUser',
    [switch]$Install,
    [switch]$Launch,
    [switch]$Uninstall,
    [switch]$RemoveCertificate
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# The name a person sees in certmgr, so nobody has to guess what this certificate is for.
$friendlyName = 'Astrid local test signing (self-signed, not for distribution)'
$dist = Join-Path $repoRoot 'dist'
$signedDir = Join-Path $dist 'signed-local'

function Get-NativeArchitecture {
    # The shells lie: Git Bash and Windows PowerShell 5.1 run emulated on ARM64 and report AMD64.
    # The registry holds the machine's real architecture, which is what predeploy.ps1 reads too.
    $environment = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
    if ($environment.PROCESSOR_ARCHITECTURE -eq 'ARM64') { return 'arm64' }
    return 'x64'
}

function Find-SdkTool([string]$name) {
    $native = Get-NativeArchitecture
    $candidates = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" `
        -Filter $name -Recurse -ErrorAction SilentlyContinue
    $tool = $candidates | Where-Object { $_.FullName -match "\\$native\\" } | Select-Object -First 1
    if (-not $tool) { $tool = $candidates | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1 }
    if (-not $tool) {
        throw "$name was not found. Restore the app project first: it comes with Microsoft.Windows.SDK.BuildTools."
    }
    return $tool.FullName
}

function Get-TestCertificate {
    Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.FriendlyName -eq $friendlyName } | Select-Object -First 1
}

function Get-BuiltIdentity {
    # The manifest inside the published folder is the one that was packed, placeholders already
    # substituted, so it is the only honest source for the Publisher the signature must match.
    $arch = $Architectures | Select-Object -First 1
    $manifestPath = Join-Path $dist "publish\win-$arch\AppxManifest.xml"
    if (-not (Test-Path $manifestPath)) {
        throw "No built package at $manifestPath. Run scripts/package.ps1 -Version $Version first."
    }
    [xml]$manifest = Get-Content $manifestPath -Raw
    return @{
        Name      = $manifest.Package.Identity.Name
        Publisher = $manifest.Package.Identity.Publisher
        AppId     = $manifest.Package.Applications.Application.Id
    }
}

function Remove-TestCertificate {
    $removed = 0
    foreach ($store in @('Cert:\CurrentUser\My', 'Cert:\CurrentUser\TrustedPeople', 'Cert:\LocalMachine\TrustedPeople')) {
        foreach ($cert in (Get-ChildItem $store -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -eq $friendlyName })) {
            Remove-Item -Path (Join-Path $store $cert.Thumbprint) -Force
            Write-Host "[-] removed $($cert.Thumbprint) from $store"
            $removed++
        }
    }
    if ($removed -eq 0) { Write-Host 'No test certificate found; nothing to remove.' }
}

if ($RemoveCertificate) {
    Remove-TestCertificate
    return
}

if ($Uninstall) {
    $identity = Get-BuiltIdentity
    $installed = Get-AppxPackage -Name $identity.Name -ErrorAction SilentlyContinue
    if (-not $installed) { Write-Host "$($identity.Name) is not installed."; return }
    Remove-AppxPackage -Package $installed.PackageFullName
    Write-Host "[-] uninstalled $($installed.PackageFullName)"
    return
}

$signtool = Find-SdkTool 'signtool.exe'
$makeappx = Find-SdkTool 'makeappx.exe'
$identity = Get-BuiltIdentity
Write-Host ""
Write-Host "[*] package $($identity.Name), publisher $($identity.Publisher)" -ForegroundColor Cyan

# --- 1. the certificate -----------------------------------------------------------------------
$cert = Get-TestCertificate
if ($cert -and $cert.Subject -ne $identity.Publisher) {
    # The identity changed under it (placeholders filled in, say). A stale certificate would sign
    # happily and Windows would then refuse the package, so replace it rather than reuse it.
    Write-Host "[!] existing test certificate is for '$($cert.Subject)'; replacing it" -ForegroundColor Yellow
    Remove-TestCertificate
    $cert = $null
}
if (-not $cert) {
    Write-Host "[*] creating a self-signed certificate for '$($identity.Publisher)'" -ForegroundColor Cyan
    # Code signing EKU, no CA bit: it can sign a package and nothing else, and it cannot issue.
    $cert = New-SelfSignedCertificate -Type Custom -Subject $identity.Publisher `
        -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
        -FriendlyName $friendlyName -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears(1) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
Write-Host "[+] certificate $($cert.Thumbprint), expires $($cert.NotAfter.ToShortDateString())" -ForegroundColor Green

# --- 2. trust the public half -----------------------------------------------------------------
$trustStore = "Cert:\$TrustScope\TrustedPeople"
if ($TrustScope -eq 'LocalMachine') {
    $elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $elevated) { throw "-TrustScope LocalMachine writes to $trustStore, which needs an elevated shell." }
}
if (-not (Get-ChildItem $trustStore -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })) {
    $cer = Join-Path $env:TEMP "astrid-local-test-$($cert.Thumbprint).cer"
    try {
        Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null
        Import-Certificate -FilePath $cer -CertStoreLocation $trustStore | Out-Null
    } finally {
        Remove-Item $cer -Force -ErrorAction SilentlyContinue
    }
    Write-Host "[+] trusted in $trustStore" -ForegroundColor Green
} else {
    Write-Host "[=] already trusted in $trustStore"
}

# --- 3. sign copies, never the Store outputs --------------------------------------------------
Remove-Item -Recurse -Force $signedDir -ErrorAction SilentlyContinue
$bundleInput = Join-Path $signedDir 'bundle'
New-Item -ItemType Directory -Force -Path $bundleInput | Out-Null

foreach ($architecture in $Architectures) {
    $source = Join-Path $dist "Astrid-$Version-$architecture.msix"
    if (-not (Test-Path $source)) { throw "Missing $source. Run scripts/package.ps1 -Version $Version first." }
    $signed = Join-Path $bundleInput (Split-Path $source -Leaf)
    Copy-Item $source $signed
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint /s My $signed | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "signtool failed on $signed" }
    Write-Host "[+] signed $signed" -ForegroundColor Green
}

$bundle = Join-Path $signedDir "Astrid-$Version.msixbundle"
& $makeappx bundle /d $bundleInput /p $bundle /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed" }
& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint /s My $bundle | Out-Null
if ($LASTEXITCODE -ne 0) { throw "signtool failed on $bundle" }
Write-Host "[+] $bundle (signed)" -ForegroundColor Green

if (-not $Install) {
    Write-Host ""
    Write-Host "Not installed. Re-run with -Install, or: Add-AppxPackage -Path '$bundle'" -ForegroundColor Cyan
    return
}

# --- 4. install ---------------------------------------------------------------------------------
$existing = Get-AppxPackage -Name $identity.Name -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[*] replacing installed $($existing.PackageFullName)" -ForegroundColor Cyan
    Get-Process -Name 'Astrid.App' -ErrorAction SilentlyContinue | Stop-Process -Force
}
Add-AppxPackage -Path $bundle -ForceUpdateFromAnyVersion
$installed = Get-AppxPackage -Name $identity.Name
if (-not $installed) { throw "Add-AppxPackage returned but $($identity.Name) is not installed." }
Write-Host "[+] installed $($installed.PackageFullName)" -ForegroundColor Green
Write-Host "    architecture: $($installed.Architecture) (machine is $(Get-NativeArchitecture))"
Write-Host "    location:     $($installed.InstallLocation)"

if (-not $Launch) { return }

# --- 5. launch ----------------------------------------------------------------------------------
$aumid = "$($installed.PackageFamilyName)!$($identity.AppId)"
Write-Host "[*] launching $aumid" -ForegroundColor Cyan
Start-Process 'explorer.exe' -ArgumentList "shell:AppsFolder\$aumid"
Start-Sleep -Seconds 8
$process = Get-Process -Name 'Astrid.App' -ErrorAction SilentlyContinue
if (-not $process) { throw "Astrid.App is not running eight seconds after launch." }
$running = $process | Select-Object -First 1
Write-Host "[+] Astrid.App running, pid $($running.Id), from $($running.Path)" -ForegroundColor Green
if ($running.Path -notlike "$($installed.InstallLocation)*") {
    throw "The running process is $($running.Path), not the packaged one under $($installed.InstallLocation)."
}
Write-Host "[+] the process is the packaged one" -ForegroundColor Green
