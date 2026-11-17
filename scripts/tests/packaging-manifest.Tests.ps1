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

    THE VERSIONS HAVE TO AGREE, and the SDK is the only honest source for what they are.
    Bumping the PackageReference without the manifest reintroduces the same crash with a
    different version number, so that pairing is asserted below rather than left to whoever
    does the bump.

    THE PAIRING RULE IS NOT DERIVABLE FROM THE VERSION STRING, which is what task 57603c11
    found on the way from SDK 1.7 to 2.5. This file used to compute the expected name as
    `Microsoft.WindowsAppRuntime.<major>.<minor>`, true of every 1.x release and wrong from 2.0
    on, where the family is `Microsoft.WindowsAppRuntime.<major>` alone. Nor is MinVersion
    derivable: 1.7.250606001 shipped runtime 7000.785.2325.0, while 2.5.1 ships 2.5.1.0.

    So the restored `Microsoft.WindowsAppSDK.Runtime` package's `WindowsAppSDK-VersionInfo.json`
    is read instead, which states both outright. It is in the NuGet cache rather than the repo,
    and `predeploy` runs these script tests BEFORE any build, so on a checkout that has never
    restored there is nothing to read: that case SAYS SO and the surrounding cases still run.
    A skip that announces itself is the price of not re-encoding a scheme Microsoft has already
    changed once.

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
    $sdk = [regex]::Match($csproj, 'Include="Microsoft\.WindowsAppSDK"\s+Version="([^"]+)"')
    Assert-True 'the csproj references Microsoft.WindowsAppSDK' $sdk.Success
    if (-not $sdk.Success) { return }
    $sdkVersion = $sdk.Groups[1].Value

    $runtime = @($Manifest.SelectNodes('/m:Package/m:Dependencies/m:PackageDependency', $Namespace) |
        Where-Object { $_.Name -like 'Microsoft.WindowsAppRuntime.*' })
    Assert-True 'there is a runtime dependency to compare' ($runtime.Count -eq 1)
    if ($runtime.Count -ne 1) { return }

    # What the SDK itself says it ships. Not computed from $sdkVersion - see the header note.
    $packages = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windowsappsdk.runtime'
    $versionInfo = Join-Path $packages "$sdkVersion\WindowsAppSDK-VersionInfo.json"
    if (-not (Test-Path $versionInfo)) {
        Write-Host "    SKIPPED the runtime identity check: $versionInfo is not restored yet"
        Write-Host '      (script tests run before any build; `dotnet restore` then re-run)'
        return
    }

    # PS 5.1's ConvertFrom-Json returns a PSCustomObject, and the file carries a BOM.
    $info = (Get-Content $versionInfo -Raw) -replace '^\xEF\xBB\xBF', '' | ConvertFrom-Json
    $family = $info.Runtime.Packages.Framework.PackageFamilyName
    Assert-True 'the SDK states a framework package family' (-not [string]::IsNullOrWhiteSpace($family))
    if ([string]::IsNullOrWhiteSpace($family)) { return }

    # The family name is "<name>_<publisherId>"; the manifest declares the name half.
    $expectedName = ($family -split '_')[0]
    Assert-Equal "the manifest names the runtime SDK $sdkVersion ships" `
        $expectedName $runtime[0].Name

    # MinVersion is the other half, and the half nothing checked before: a correct name with a
    # version the installed runtime does not satisfy fails at install, not at the first call.
    Assert-Equal "MinVersion is the runtime SDK $sdkVersion ships" `
        $info.Runtime.Version.DotQuadNumber $runtime[0].MinVersion
}

Invoke-Case 'the dependency is required because the SDK is NOT self-contained' {
    # The premise of everything above. `WindowsAppSDKSelfContained=true` copies the runtime into
    # the package and the dependency stops being needed - so if someone sets it, this file is
    # asserting the wrong thing and should be revisited rather than silently passing.
    $csproj = Get-Content $CsprojPath -Raw
    Assert-True 'WindowsAppSDKSelfContained is not turned on' `
        (-not ($csproj -match '<WindowsAppSDKSelfContained>\s*true'))
}

# --- the packaged build must not carry the bootstrapper ----------------------------------------
# THE SECOND BUG, task 76fcd157, and the same shape as the first: the package installed, and then
# no process ever appeared. The Application log said
#
#     Windows App Runtime, ERROR 0x80070032:
#     Bootstrapper initialization failed while looking for version 2.x (MSIX package version >= 2.6.5.0)
#
# with that runtime installed and the manifest's PackageDependency resolving to it correctly. The
# fault was that the BOOTSTRAPPER RAN AT ALL. It is the discovery mechanism for apps WITHOUT
# package identity; called from inside an MSIX it returns 0x80070032 ERROR_NOT_SUPPORTED, and the
# auto-initializer's response to that is to fail fast - before the first line of Main, which is why
# the crash looked identical to the 2026-10-18 one from outside.
#
# HOW IT GOT IN. The csproj left WindowsPackageType unset when AstridPackaged=true, under a comment
# claiming that unset was the SDK's way of saying MSIX. It is not.
# Microsoft.Windows.SDK.BuildTools.MSIX defaults an empty WindowsPackageType to MSIX only when a
# `Package.appxmanifest` sits beside the project, and otherwise to None - and this project has no
# such file on purpose, because the manifest is hand-written with placeholders package.ps1
# substitutes. So "unset" resolved to None, None turns on WindowsAppSdkBootstrapInitialize in
# Microsoft.WindowsAppSDK.BootstrapCommon.targets, and that <Compile Include>s
# MddBootstrapAutoInitializer.cs out of the NuGet package and into the exe.
#
# WHY THIS IS CHECKED TWICE. The broken csproj READ AS CORRECT - it even carried a comment
# asserting the behaviour it did not have - so no amount of grepping the file would have found
# this. Only asking MSBuild what it actually evaluates does. The text case is still worth having:
# it needs no restore, so it runs everywhere and fails the moment the fix is deleted. The
# evaluation case is the one that would have caught the original defect, and the one that will
# catch the SDK changing these defaults again.
$Csproj = Get-Content $CsprojPath -Raw
$NuGetProps = Join-Path $RepoRoot 'app\Astrid.App\obj\Astrid.App.csproj.nuget.g.props'

# $null when the properties cannot honestly be evaluated, so the caller can say SKIPPED rather
# than fail. The Windows App SDK targets reach the project through obj\*.nuget.g.props, and
# predeploy runs these script tests BEFORE any build - so on a checkout that has never restored,
# WindowsAppSdkBootstrapInitialize would read empty for both builds and the unpackaged case would
# fail for a reason that has nothing to do with this bug.
function Get-EvaluatedProperties([bool]$Packaged) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $null }
    if (-not (Test-Path $NuGetProps)) { return $null }

    $msbuildArgs = @(
        'msbuild'
        $CsprojPath
        '-nologo'
        '-p:Configuration=Release'
        '-p:RuntimeIdentifier=win-arm64'
        '-getProperty:WindowsPackageType'
        '-getProperty:WindowsAppSdkBootstrapInitialize'
    )
    # The packaged build is the one package.ps1 runs. Absent the switch this is the F5 build.
    if ($Packaged) { $msbuildArgs += '-p:AstridPackaged=true' }

    # Not redirected with 2>&1: in PS 5.1 that wraps a native command's stderr in ErrorRecords and
    # sets $? false even on a clean exit, which would read as a broken evaluation.
    $raw = & dotnet @msbuildArgs
    if ($LASTEXITCODE -ne 0) { return $null }

    # Two -getProperty switches make MSBuild emit JSON; one would print a bare value.
    $json = $null
    try { $json = ($raw -join "`n") | ConvertFrom-Json } catch { return $null }
    if ($null -eq $json -or $null -eq $json.Properties) { return $null }

    # A hashtable, so returning it cannot unroll the way an array would.
    return @{
        WindowsPackageType  = [string]$json.Properties.WindowsPackageType
        BootstrapInitialize = [string]$json.Properties.WindowsAppSdkBootstrapInitialize
    }
}

Invoke-Case 'the packaged build turns the bootstrapper off (task 76fcd157)' {
    Assert-True 'the csproj sets WindowsAppSdkBootstrapInitialize false when AstridPackaged=true' `
        ($Csproj -match '(?s)<WindowsAppSdkBootstrapInitialize[^>]*AstridPackaged[^>]*>\s*false\s*</WindowsAppSdkBootstrapInitialize>')

    $props = Get-EvaluatedProperties $true
    if ($null -eq $props) {
        Write-Host '    SKIPPED the evaluated check: no restored obj\Astrid.App.csproj.nuget.g.props'
        Write-Host '      (script tests run before any build; `dotnet restore` then re-run)'
        return
    }

    # The invariant, stated as what the app needs rather than as a literal: anything but 'true'
    # leaves Bootstrap.CS.targets unimported, which is the whole fix.
    Assert-True 'MSBuild evaluates WindowsAppSdkBootstrapInitialize to something other than true' `
        ($props.BootstrapInitialize -ne 'true')

    # Deliberately still None. MSIX is the value that reads right and is wrong here: the MSIX
    # tooling then requires an @(AppxManifest) item and fails the publish with "Improper project
    # configuration: no AppxManifest is specified", which would drag packaging back into the csproj
    # and defeat the hand-written manifest this whole file is about.
    Assert-Equal 'WindowsPackageType stays None for the packaged build' 'None' $props.WindowsPackageType
}

Invoke-Case 'the unpackaged build still gets the bootstrapper (task 76fcd157)' {
    # The other half, and the reason the fix is conditioned rather than blanket. F5 and the UI
    # smoke tests drive the unpackaged build, which has no package identity and so reaches the
    # Windows App SDK through the bootstrapper and nothing else. Turning it off for everyone would
    # trade a dead MSIX for a dead inner loop.
    Assert-True 'the csproj leaves WindowsPackageType None when not packaged' `
        ($Csproj -match '(?s)<WindowsPackageType[^>]*AstridPackaged[^>]*>\s*None\s*</WindowsPackageType>')

    $props = Get-EvaluatedProperties $false
    if ($null -eq $props) {
        Write-Host '    SKIPPED the evaluated check: no restored obj\Astrid.App.csproj.nuget.g.props'
        return
    }

    Assert-Equal 'WindowsPackageType is None for the unpackaged build' 'None' $props.WindowsPackageType
    Assert-Equal 'the bootstrapper is initialized for the unpackaged build' 'true' $props.BootstrapInitialize
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
