#Requires -Version 5.1
<#
.SYNOPSIS
    The MSIX must declare the Windows App SDK runtime it is framework-dependent on.

.DESCRIPTION
    THE BUG. On 2026-10-18, asked to install the latest app and open it, the package built, signed
    and installed cleanly and then died on its first line. The crash was

        System.Runtime.InteropServices.COMException (0x80040154): Class not registered
          at WinRT.ActivationFactory.Get(String typeName, Guid iid)
          at Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent()
          at Astrid.App.Program.Main(String[] args) in app/Astrid.App/Program.cs:line 38

    `AppInstance.GetCurrent()` is the FIRST statement in Main, so the app could not reach its own
    second line. `packaging/AppxManifest.xml` declared only a TargetDeviceFamily, and
    `git log -S PackageDependency` showed one had never been in the file: no MSIX this repo ever
    built could start. Installing was as far as the flow in packaging/README.md had been tested,
    and an install proves nothing about a launch.

    WHY A SELF-CONTAINED PUBLISH IS NOT ENOUGH, which is the part that makes this easy to re-break.
    `package.ps1` publishes with the .NET runtime included, and that covers CoreCLR and the BCL
    only. WinUI and everything under `Microsoft.Windows.*` come from the `Microsoft.WindowsAppRuntime`
    FRAMEWORK PACKAGE, and inside an MSIX, WinRT activation resolves only through the package
    dependencies the manifest declares. Unpackaged, the same binaries find the runtime through the
    bootstrapper instead - which is why `F5` and the UI smoke tests, which drive the unpackaged
    build, never saw this.

    WHAT THIS FILE CHECKS, and why it asserts on the manifest rather than launching the app. The
    honest end-to-end test is "install the bundle and see a window", and that needs a machine-wide
    certificate, an elevated shell and about four minutes of publishing - none of which belongs in
    `predeploy`. What regressed here is WIRING: one element absent from one file. So this reads the
    manifest and the csproj and checks they agree, which is seconds long and fails for exactly the
    reason the app failed.

    THE VERSIONS HAVE TO AGREE. The runtime's package name carries the SDK's major.minor
    (`Microsoft.WindowsAppSDK` 1.7.x needs `Microsoft.WindowsAppRuntime.1.7`), so bumping the
    PackageReference without the manifest reintroduces the same crash with a different version
    number. That pairing is asserted below rather than left to whoever does the bump.

    This file is ASCII only, so it reads the same under any codepage Windows PowerShell 5.1 picks -
    the same reason sign-local.ps1 says so about itself.
#>
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ManifestPath = Join-Path $RepoRoot 'packaging\AppxManifest.xml'
$CsprojPath = Join-Path $RepoRoot 'app\Astrid.App\Astrid.App.csproj'
foreach ($required in @($ManifestPath, $CsprojPath)) {
    if (-not (Test-Path $required)) {
        Write-Host "FAIL: no file at $required" -ForegroundColor Red
        exit 1
    }
}

$script:Failures = 0
$script:Ran = 0
# Assertions MADE, not passed - see fixall-loop-guards.Tests.ps1 for why a case that exits early
# is the one outcome a failure count cannot see.
$script:Checks = 0

function Assert-Equal([string]$What, $Expected, $Actual) {
    $script:Checks++
    if ([string]$Expected -ceq [string]$Actual) { return }
    Write-Host "    FAIL $What" -ForegroundColor Red
    Write-Host "      expected: [$Expected]" -ForegroundColor Red
    Write-Host "      actual:   [$Actual]" -ForegroundColor Red
    $script:Failures++
}

function Assert-Match([string]$What, [string]$Pattern, [string]$Actual) {
    $script:Checks++
    if ($Actual -match $Pattern) { return }
    Write-Host "    FAIL $What" -ForegroundColor Red
    Write-Host "      expected to match: [$Pattern]" -ForegroundColor Red
    Write-Host "      actual:            [$Actual]" -ForegroundColor Red
    $script:Failures++
}

function Assert-True([string]$What, $Actual) { Assert-Equal $What 'True' ([bool]$Actual) }

function Invoke-Case([string]$Name, [scriptblock]$Body) {
    $script:Ran++
    Write-Host "  - $Name"
    $before = $script:Checks
    try { & $Body }
    catch {
        Write-Host "    FAIL the case threw: $($_.Exception.Message)" -ForegroundColor Red
        $script:Failures++
    }
    finally {
        if ($script:Checks -eq $before) {
            Write-Host '    FAIL the case made no assertions - it exited early' -ForegroundColor Red
            $script:Failures++
        }
    }
}

# --- the manifest, parsed as the packer will parse it ------------------------------------------
# package.ps1 substitutes these before calling makeappx. Substituting them here too means this
# file parses the same bytes the packer does, so a malformed edit fails the gate in a second
# instead of failing `makeappx pack` four minutes into a publish - with its stderr swallowed by
# `| Out-Null`, which is how the first attempt at this fix reported only "makeappx pack failed".
$ManifestRaw = Get-Content $ManifestPath -Raw
$substituted = $ManifestRaw `
    -replace '\{VERSION\}', '0.0.0.0' `
    -replace '\{ARCHITECTURE\}', 'arm64' `
    -replace '\{IDENTITY_NAME\}', 'Test.Identity' `
    -replace '\{IDENTITY_PUBLISHER\}', 'CN=Test'

$Manifest = $null
try {
    $Manifest = New-Object System.Xml.XmlDocument
    $Manifest.LoadXml($substituted)
} catch {
    Write-Host "FAIL: packaging/AppxManifest.xml is not valid XML: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host '      makeappx rejects the package with error C00CEE23 for this.' -ForegroundColor Red
    exit 1
}

$Namespace = New-Object System.Xml.XmlNamespaceManager($Manifest.NameTable)
$Namespace.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')

Write-Host 'packaging/AppxManifest.xml'

Invoke-Case 'the manifest is valid XML, comments included' {
    # The first attempt at this fix wrote "--self-contained" inside an XML comment, and a double
    # hyphen is illegal there. The document above loaded, so this is already proven; asserting it
    # keeps the case from being vacuous and names what the check is for.
    Assert-True 'it parsed' ($null -ne $Manifest.DocumentElement)
    [regex]::Matches($ManifestRaw, '(?s)<!--(.*?)-->') | ForEach-Object {
        Assert-True 'no comment contains a double hyphen' (-not ($_.Groups[1].Value -match '--'))
    }
}

Invoke-Case 'the Windows App SDK runtime is declared as a PackageDependency' {
    $dependencies = $Manifest.SelectNodes('/m:Package/m:Dependencies/m:PackageDependency', $Namespace)
    Assert-True 'Dependencies declares at least one PackageDependency' ($dependencies.Count -ge 1)

    $runtime = @($dependencies | Where-Object { $_.Name -like 'Microsoft.WindowsAppRuntime.*' })
    Assert-Equal 'exactly one Windows App Runtime dependency' 1 $runtime.Count
    if ($runtime.Count -ne 1) { return }

    # Microsoft signs the framework package; a mismatched Publisher makes the dependency
    # unresolvable and the app fails exactly as it did with no dependency at all.
    Assert-Equal 'the publisher is Microsoft' `
        'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US' `
        $runtime[0].Publisher
    Assert-Match 'MinVersion is a four-part version' '^\d+\.\d+\.\d+\.\d+$' $runtime[0].MinVersion
}

Invoke-Case 'the declared runtime matches the SDK the app references' {
    $csproj = Get-Content $CsprojPath -Raw
    $sdk = [regex]::Match($csproj, 'Include="Microsoft\.WindowsAppSDK"\s+Version="(\d+)\.(\d+)\.')
    Assert-True 'the csproj references Microsoft.WindowsAppSDK' $sdk.Success
    if (-not $sdk.Success) { return }

    $expected = "Microsoft.WindowsAppRuntime.$($sdk.Groups[1].Value).$($sdk.Groups[2].Value)"
    $runtime = @($Manifest.SelectNodes('/m:Package/m:Dependencies/m:PackageDependency', $Namespace) |
        Where-Object { $_.Name -like 'Microsoft.WindowsAppRuntime.*' })
    Assert-True 'there is a runtime dependency to compare' ($runtime.Count -eq 1)
    if ($runtime.Count -ne 1) { return }
    Assert-Equal "the manifest names the runtime for SDK $($sdk.Groups[1].Value).$($sdk.Groups[2].Value)" `
        $expected $runtime[0].Name
}

Invoke-Case 'the dependency is required because the SDK is NOT self-contained' {
    # The premise of everything above. `WindowsAppSDKSelfContained=true` copies the runtime into
    # the package and the dependency stops being needed - so if someone sets it, this file is
    # asserting the wrong thing and should be revisited rather than silently passing.
    $csproj = Get-Content $CsprojPath -Raw
    Assert-True 'WindowsAppSDKSelfContained is not turned on' `
        (-not ($csproj -match '<WindowsAppSDKSelfContained>\s*true'))
}

Write-Host ''
if ($script:Checks -lt 10) {
    Write-Host "FAIL: only $($script:Checks) assertion(s) ran - this file is meant to make more" -ForegroundColor Red
    $script:Failures++
}
if ($script:Failures -gt 0) {
    Write-Host "RESULT: FAILED - $($script:Failures) failure(s) in $($script:Ran) case(s)" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: OK - $($script:Ran) case(s), $($script:Checks) assertion(s)" -ForegroundColor Green
exit 0
