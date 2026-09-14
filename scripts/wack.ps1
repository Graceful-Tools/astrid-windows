<#
.SYNOPSIS
    Run the Windows App Certification Kit over the packaged bundle.

.DESCRIPTION
    Partner Center runs these same checks at submission, so this only ever buys time: a failure
    found here arrives in ten minutes instead of days later, after certification. Worth doing once
    before the first upload; unnecessary before every later one.

    NEEDS AN ELEVATED SHELL, and that is not a policy of ours. The kit INSTALLS the package in
    order to drive it, which is why an unelevated run cannot be made to work by relaxing anything
    here. It also takes about ten minutes and moves the mouse, so leave the machine alone.

    THE KIT IS NOT PART OF THE BUILD TOOLS. The Windows SDK that arrives with the VS Build Tools
    component Windows11SDK.26100 (docs/context/stack.md) lays down the headers, libraries and
    signing tools, but not appcert.exe: the App Certification Kit is a feature of the standalone
    Windows SDK installer. On a machine without it the directory exists and holds only the
    SupportedAPIs XML, which is why this script names the install command rather than letting
    Windows say "not found" after you have already opened an elevated shell.

    The package tested is the UNSIGNED Store bundle from scripts/package.ps1, which is the artifact
    Partner Center receives. Do not point this at the signed copies under dist/signed: those exist
    only for local installation and carry a certificate the Store would replace.

    This file is saved UTF-8 WITH a byte order mark. Windows PowerShell 5.1 reads a .ps1 without
    one as the system codepage, which turns any non-ASCII character in a string into a parse error.

.PARAMETER Version
    Four parts, matching the package to test: 0.1.0.0.

.PARAMETER ReportPath
    Where the XML report lands. Defaults to wack.xml in the repo root, which is gitignored.
    Open it in a browser; it renders as a readable report.

.EXAMPLE
    powershell -File scripts/wack.ps1
#>
param(
    [string]$Version = '0.1.0.0',
    [string]$ReportPath = 'wack.xml'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must have four parts, e.g. 0.1.0.0 - it names the package built by scripts/package.ps1."
}

# --- 1. elevation ------------------------------------------------------------------------------
$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elevated) {
    throw "The App Certification Kit installs the package in order to test it, so this needs an elevated shell. Re-run it from an administrator PowerShell."
}

# --- 2. the kit itself -------------------------------------------------------------------------
# 64-bit and 32-bit Program Files both, because the kit lands under the x86 tree on x64 and ARM64
# hosts but not on every SDK layout, and $env:ProgramFiles(x86) is awkward to read in PowerShell.
$appcertCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'),
    (Join-Path $env:ProgramFiles 'Windows Kits\10\App Certification Kit\appcert.exe')
) | Where-Object { $_ -and (Test-Path $_) }
$appcert = $appcertCandidates | Select-Object -First 1

if (-not $appcert) {
    throw @"
appcert.exe was not found. The App Certification Kit is a feature of the standalone Windows SDK
installer, and the VS Build Tools SDK component does not include it.

Install it (elevated, about 5 minutes):

    winget install Microsoft.WindowsSDK.10.0.26100

or, for the kit alone rather than the whole SDK, run winsdksetup.exe with:

    winsdksetup.exe /features OptionId.WindowsSoftwareLogoToolkit /q

then re-run this script.
"@
}
Write-Host "[+] $appcert" -ForegroundColor Green

# --- 3. the package ----------------------------------------------------------------------------
$bundle = Join-Path $repoRoot "dist\Astrid-$Version.msixbundle"
if (-not (Test-Path $bundle)) {
    throw "dist\Astrid-$Version.msixbundle does not exist. Build it first: powershell -File scripts/package.ps1 -Version $Version"
}
Write-Host "[+] $bundle" -ForegroundColor Green

$report = if ([System.IO.Path]::IsPathRooted($ReportPath)) { $ReportPath } else { Join-Path $repoRoot $ReportPath }

# --- 4. run ------------------------------------------------------------------------------------
# reset first: the kit keeps state from the previous run, and a stale one reports the previous
# package's results against this one.
Write-Host ''
Write-Host 'Resetting the kit...'
& $appcert reset
if ($LASTEXITCODE -ne 0) { throw "appcert reset failed with exit code $LASTEXITCODE." }

Write-Host 'Testing. This takes about ten minutes and drives the app - leave the machine alone.'
& $appcert test -appxpackagepath $bundle -reportoutputpath $report
$testExit = $LASTEXITCODE

Write-Host ''
if (Test-Path $report) {
    Write-Host "Report: $report" -ForegroundColor Cyan
    Write-Host 'Open it in a browser; it renders as a readable report.'
}

# A non-zero exit here means the kit could not run, not that the package failed a test: failures
# are recorded in the report and still exit 0. So say which of the two happened.
if ($testExit -ne 0) {
    throw "appcert test could not complete (exit code $testExit). This is the kit failing to run, not the package failing a check."
}

Write-Host '[+] the kit ran. Read the report for OVERALL RESULT and any FAILED sections.' -ForegroundColor Green
