#Requires -Version 5.1
<#
.SYNOPSIS
    A failing build tool must say why, and capturing its output must not break a passing one.
    Task 5a9cb679.

.DESCRIPTION
    THE BUG. `scripts/package.ps1` and `scripts/sign-local.ps1` piped every native tool call to
    `Out-Null`, so a failure printed only the script's own one-line summary:

        makeappx pack failed for arm64

    after a four-minute publish. On 2026-10-18 the real cause was a line makeappx had already
    printed and `Out-Null` had already eaten:

        MakeAppx : error: Error info: error C00CEE23: App manifest validation error:
          The app manifest XML must be valid: Line 57, Column 39, Reason: '>' expected.

    With the line and column that is a ten-second fix. Without them the only route is to re-run
    the tool by hand against `dist/publish/win-<arch>` and read what it says, which is what that
    session had to do.

    `Out-Null` was not a mistake in itself: makeappx enumerates every packed file, 268 of them for
    this app, and these scripts print five deliberate lines. Discarding the DIAGNOSIS along with
    that noise is the accident.

    THE TRAP, which is why this is a shared helper and not three lines copied six times. Both
    scripts set `$ErrorActionPreference = 'Stop'`, and under Stop, PowerShell 5.1 turns a native
    executable's stderr into a TERMINATING error as soon as it is redirected - so the obvious fix
    breaks every build where a tool merely warns. Measured on 2026-10-18 against a .cmd that
    writes one stderr line and exits 0:

        $a = & $exe 2>&1          under Stop -> THREW (RemoteException)
        & $exe *> $file           under Stop -> THREW (RemoteException)
        EAP=Continue around it    -> OK, exit 0, both lines captured

    So `*>` to a file is no safer than `2>&1`; the only thing that works is narrowing
    `$ErrorActionPreference` around the call. Getting that wrong turns a green build red on a
    warning, which is worse than the bug being fixed - hence one helper, asserted here.

    WHAT THIS FILE CHECKS, and why it asserts on the scripts rather than running them. Reaching a
    real makeappx failure means a publish; reaching a signtool one means a certificate. Neither
    belongs in `predeploy`, and `packaging-manifest.Tests.ps1` already covers the manifest mistake
    that exposed this. What regressed is WIRING - where a native tool's output goes - so this
    reads it off the parsed AST, where one invocation is one node and `Out-Null` cannot be matched
    out of a comment.

    Cmdlet calls piped to `Out-Null` are deliberately NOT covered: `New-Item`, `Export-Certificate`
    and `Import-Certificate` throw on failure under Stop, so they lose nothing. This is about
    native executables, which only ever set an exit code.

    This file is ASCII only, so it reads the same under any codepage Windows PowerShell 5.1 picks.
#>
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$HelperPath = Join-Path $RepoRoot 'scripts\lib\invoke-build-tool.ps1'

# The scripts under test, and the native tools each one drives. makeappx and signtool are invoked
# through a variable (`& $signtool ...`, `& $makeappx.FullName ...`) because they are found by
# path; `dotnet` is on PATH and invoked bare. The AST match below accepts either shape.
#
# `dotnet publish` is in this list because it is the MOST likely of the three to fail - a compile
# error fails it every time - and it was swallowed exactly like the other two, reporting only
# "publish failed for win-arm64". It was missed when this task was written and found by the first
# RED run of this file, which flagged the unreported exit-code guard at package.ps1:85.
$Targets = [ordered]@{
    'scripts\package.ps1'    = @('makeappx', 'dotnet')
    'scripts\sign-local.ps1' = @('makeappx', 'signtool')
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

function Get-ScriptAst([string]$Path) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if ($errors -and $errors.Count -gt 0) {
        Write-Host "FAIL: $Path does not parse ($($errors.Count) error(s)):" -ForegroundColor Red
        $errors | Select-Object -First 5 | ForEach-Object { Write-Host "    $($_.Message)" -ForegroundColor Red }
        exit 1
    }
    return $ast
}

# Every invocation of one of the named tools, as a CommandAst. `& $makeappx.FullName pack ...`
# puts a member expression in CommandElements[0], `& $signtool sign ...` a variable, and
# `dotnet publish ...` a bare command name - hence the optional `$`. The match is on that
# element's own extent, not on the line, which would also catch a mention in a comment.
function Find-ToolInvocations($Ast, [string[]]$Tools) {
    $pattern = '^\$?(' + (($Tools | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\b'
    return @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) |
        Where-Object {
            $first = $_.CommandElements | Select-Object -First 1
            $first -and ($first.Extent.Text -match $pattern)
        })
}

function Get-EnclosingPipeline($Node) {
    $at = $Node
    while ($at -and -not ($at -is [System.Management.Automation.Language.PipelineAst])) { $at = $at.Parent }
    return $at
}

# ---------------------------------------------------------------------------------------------
# The helper: one place that knows about the trap.
# ---------------------------------------------------------------------------------------------
Write-Host 'scripts/lib/invoke-build-tool.ps1'

if (-not (Test-Path $HelperPath)) {
    Write-Host "    FAIL the helper does not exist at $HelperPath" -ForegroundColor Red
    $script:Failures++
    $script:Ran++
    Write-Host ''
    Write-Host "RESULT: FAILED - $($script:Failures) failure(s) in $($script:Ran) case(s)" -ForegroundColor Red
    exit 1
}

$HelperAst = Get-ScriptAst $HelperPath
$HelperText = Get-Content $HelperPath -Raw
$HelperFunction = @($HelperAst.FindAll({
            param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst]
        }, $true))

Invoke-Case 'the helper defines Invoke-BuildTool' {
    $names = @($HelperFunction | ForEach-Object { $_.Name })
    Assert-True "Invoke-BuildTool is defined (found: $($names -join ', '))" ($names -contains 'Invoke-BuildTool')
}

Invoke-Case 'the helper narrows $ErrorActionPreference around the call' {
    # THE WHOLE POINT. Without this, a tool that warns on stderr and exits 0 throws under the
    # Stop that both scripts set, and a green build goes red.
    Assert-Match 'it sets $ErrorActionPreference to Continue' "ErrorActionPreference\s*=\s*'Continue'" $HelperText
    Assert-Match 'and restores it in a finally' '(?s)finally\s*\{[^}]*ErrorActionPreference' $HelperText
}

Invoke-Case 'the helper reports the output and branches on $LASTEXITCODE' {
    Assert-Match 'it captures the output' '\$\w+\s*=\s*&' $HelperText
    Assert-Match 'it branches on $LASTEXITCODE' 'LASTEXITCODE' $HelperText
    Assert-True 'it never branches on $?' (-not ($HelperText -match 'if\s*\(\s*-not\s*\$\?'))
    Assert-Match 'it writes something on failure' 'Write-Host' $HelperText
    Assert-Match 'and then throws' 'throw' $HelperText
}

# ---------------------------------------------------------------------------------------------
# The two scripts: nothing swallowed, everything through the helper.
# ---------------------------------------------------------------------------------------------
foreach ($relative in $Targets.Keys) {
    $path = Join-Path $RepoRoot $relative
    if (-not (Test-Path $path)) {
        Write-Host "FAIL: no script at $path" -ForegroundColor Red
        exit 1
    }

    Write-Host $relative
    $ast = Get-ScriptAst $path
    $text = Get-Content $path -Raw
    $invocations = Find-ToolInvocations $ast $Targets[$relative]

    Invoke-Case "$relative drives its native tools" {
        # If this is zero the cases below would pass vacuously, which is the failure mode
        # fixall-sweep.Tests.ps1 learned the hard way.
        Assert-True 'at least one native tool invocation was found' ($invocations.Count -ge 1)
    }

    Invoke-Case "$relative dot-sources the helper" {
        Assert-Match 'it dot-sources invoke-build-tool.ps1' 'invoke-build-tool\.ps1' $text
    }

    Invoke-Case "$relative never pipes a native tool to Out-Null" {
        foreach ($invocation in $invocations) {
            $pipeline = Get-EnclosingPipeline $invocation
            Assert-True 'the invocation sits in a pipeline' ($null -ne $pipeline)
            if (-not $pipeline) { continue }
            $discards = @($pipeline.PipelineElements | Where-Object {
                    $_ -is [System.Management.Automation.Language.CommandAst] -and
                    ($_.CommandElements | Select-Object -First 1).Extent.Text -eq 'Out-Null'
                })
            Assert-Equal "$($invocation.Extent.Text.Trim()) does not discard its output" 0 $discards.Count
        }
    }

    Invoke-Case "$relative runs every native tool through Invoke-BuildTool" {
        foreach ($invocation in $invocations) {
            # The invocation must sit inside a scriptblock handed to the helper, so the trap and
            # the reporting are applied to it rather than re-implemented beside it.
            $at = $invocation
            $wrapped = $false
            while ($at) {
                if ($at -is [System.Management.Automation.Language.CommandAst]) {
                    $name = ($at.CommandElements | Select-Object -First 1).Extent.Text
                    if ($name -eq 'Invoke-BuildTool') { $wrapped = $true; break }
                }
                $at = $at.Parent
            }
            Assert-True "$($invocation.Extent.Text.Trim()) goes through Invoke-BuildTool" $wrapped
        }
    }

    Invoke-Case "$relative leaves no bare exit-code guard behind" {
        # A guard that only throws is the bug. Once the helper owns the check, a leftover
        # `if ($LASTEXITCODE -ne 0) { throw ... }` beside a wrapped call is dead code that would
        # read as if it were still doing the work.
        $guards = @($ast.FindAll({
                    param($n)
                    $n -is [System.Management.Automation.Language.IfStatementAst] -and
                    $n.Clauses[0].Item1.Extent.Text -match 'LASTEXITCODE'
                }, $true))
        Assert-Equal 'no $LASTEXITCODE guards remain in the script' 0 $guards.Count
    }
}


# ---------------------------------------------------------------------------------------------
# A typed parameter's name is not available as a local. Task af2ca695.
# ---------------------------------------------------------------------------------------------
#
# THE BUG. `package.ps1` declares `[string]$Brand` and then wrote the resolved profile back into
# the same name, differing only in case:
#
#     $brand = Get-AstridBrandProfile -Brand $Brand -RepoRoot $repoRoot
#
# PowerShell variable names are case-INSENSITIVE, so `$brand` and `$Brand` are one variable - and
# a `param([string]...)` declaration puts a TYPE CONSTRAINT on it that survives every later
# assignment. So the pscustomobject was silently converted to its string form,
# "@{Name=astrid; DisplayName=Astrid Tasks; ...}", and the next line threw under StrictMode:
#
#     The property 'Name' cannot be found on this object. Verify that the property exists.
#
# Two things made it worse than a typo. The assignment also made `if ($Brand)` true for Astrid's
# own build - the parameter now held an object instead of '' - so the guard meant to print one
# line for a partner build instead ran the failing line for EVERY build. And `npm run predeploy`
# never calls package.ps1, so the gate stayed green while packaging was broken for six days: no
# MSIX could be produced at all, which is also a Store release nobody could cut.
#
# Asserted as a RULE rather than against that one name, because the trap is the language's and
# the next typed parameter is as vulnerable as this one was.
Write-Host ''
Write-Host 'a typed parameter is not reused as a local'

foreach ($relative in $Targets.Keys) {
    Invoke-Case "$relative does not assign to any of its typed parameters" {
        $path = Join-Path $RepoRoot $relative
        $ast = Get-ScriptAst $path

        # The script's own param() block, not a nested function's.
        $paramBlock = $ast.ParamBlock
        $typed = @()
        if ($paramBlock) {
            $typed = @($paramBlock.Parameters |
                Where-Object { $_.StaticType -and $_.StaticType -ne [object] } |
                ForEach-Object { $_.Name.VariablePath.UserPath })
        }
        Assert-True "$relative declares typed parameters to check" ($typed.Count -gt 0)

        $assigned = @($ast.FindAll({
                    param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst]
                }, $true) |
                Where-Object { $_.Left -is [System.Management.Automation.Language.VariableExpressionAst] } |
                ForEach-Object { $_.Left.VariablePath.UserPath })

        foreach ($name in $typed) {
            $clash = @($assigned | Where-Object { $_ -eq $name -or $_ -ieq $name })
            Assert-Equal "nothing assigns to `$$name, whose type constraint would coerce it" 0 $clash.Count
        }
    }
}

Invoke-Case 'every brand value reaching -p: is escaped for MSBuild (task 13c6af65)' {
    # THE BUG. `package.ps1` interpolated each brand value straight into a "-p:Name=$value"
    # string. On 2026-11-13 `AuthProviders` became `passkey,google`, and MSBuild splits `-p:` on
    # commas and semicolons, so it read `passkey` as the value and then tried to parse `google` as
    # a switch:
    #
    #     MSBUILD : error MSB1006: Property is not valid.
    #     Switch: google
    #
    # No MSIX could be built at all, for any brand or architecture, until this was fixed. The gate
    # never saw it because `npm run predeploy` does not run `package.ps1` - the same blind spot
    # that hid six days of broken packaging on 2026-10-25.
    #
    # Asserted off the source rather than by running a publish, like everything else in this file:
    # reaching the real failure costs a four-minute build. What regressed is how a value is handed
    # over, and that is visible in the text.
    $package = Get-Content -Path (Join-Path $RepoRoot 'scripts\package.ps1') -Raw

    # No "-p:...=$(...)" interpolation left. This is the shape that was wrong, and a new brand
    # value added by copying the line above it would be wrong the same way.
    $raw = [regex]::Matches($package, '"-p:[A-Za-z0-9_]+=\$')
    Assert-Equal 'no brand value is interpolated straight into a -p: string' 0 $raw.Count

    # Every brand field the running app is told about goes through the helper.
    foreach ($property in @(
            'AstridDefaultServerUrl', 'AstridUrlScheme', 'AstridBrandShortName',
            'AstridBrandInboundTaskEmail', 'AstridBrandAgentName', 'AstridBrandWordmark',
            'AstridBrandAuthProviders')) {
        Assert-Match "$property is formatted by Format-MsBuildProperty" `
            ("Format-MsBuildProperty '" + $property + "'") $package
    }

    Assert-Match 'the escaping helper is dot-sourced' 'msbuild-property\.ps1' $package
}

Invoke-Case 'the escaping survives everything a partner can type (task 13c6af65)' {
    . (Join-Path $RepoRoot 'scripts\lib\msbuild-property.ps1')

    # The value that actually broke the build.
    Assert-Equal 'a comma is escaped' '-p:AstridBrandAuthProviders=passkey%2Cgoogle' `
        (Format-MsBuildProperty 'AstridBrandAuthProviders' 'passkey,google')

    # A semicolon ends a -p: value exactly as a comma does.
    Assert-Equal 'a semicolon is escaped' '-p:X=a%3Bb' (Format-MsBuildProperty 'X' 'a;b')

    # `%` is MSBuild's own escape character and MUST go first, or a value that already contains
    # `%2C` would come out the other side as a comma - the bug this fixes, reintroduced by the fix.
    Assert-Equal 'a literal %2C stays literal' '-p:X=%252C' (Format-MsBuildProperty 'X' '%2C')
    Assert-Equal 'a percent is escaped before anything else' '-p:X=100%25%2C200' `
        (Format-MsBuildProperty 'X' '100%,200')

    # MSBuild would otherwise evaluate these inside a property value.
    Assert-Equal 'a property reference is not evaluated' '-p:X=%24(Foo)' `
        (Format-MsBuildProperty 'X' '$(Foo)')
    Assert-Equal 'an item reference is not evaluated' '-p:X=%40(Bar)' `
        (Format-MsBuildProperty 'X' '@(Bar)')

    # An ordinary wordmark is untouched: escaping must not change what a build has always produced.
    Assert-Equal 'a plain value passes through unchanged' '-p:AstridBrandWordmark=astrid' `
        (Format-MsBuildProperty 'AstridBrandWordmark' 'astrid')

    # A profile that left a field out gives an empty property, which is what the old interpolation
    # did and what the csproj defaults expect.
    Assert-Equal 'an absent value is an empty property' '-p:X=' (Format-MsBuildProperty 'X' $null)
    Assert-Equal 'an empty value is an empty property' '-p:X=' (Format-MsBuildProperty 'X' '')

    # A property NAME needing an escape is a mistake, not something to encode around.
    $threw = $false
    try { Format-MsBuildProperty 'Not A Name' 'x' } catch { $threw = $true }
    Assert-True 'a malformed property name throws rather than being escaped' $threw
}

# ── The argument construction, RUN rather than read (task 181fb1b5) ───────────────────────────
# Everything above this point asserts on package.ps1 as SOURCE, off the parsed AST or its text.
# That catches a line written wrongly. It cannot catch a VALUE that breaks the command line, and
# twice now a value has:
#
#   2026-10-25  packaging broken for six days behind a green gate (a manifest error).
#   2026-11-13  AuthProviders became `passkey,google` in 7bd739e, MSBuild split the -p: list on
#               the comma, and every build died with MSB1006 "Property is not valid. Switch:
#               google". No MSIX for any brand on any architecture, for a day.
#
# Both were found by a human or an accident. `npm run predeploy` has never run package.ps1 - it is
# a ~4-minute publish per architecture - so the cheap half is run here instead: resolve the brand,
# format every argument, substitute the manifest, and stop before dotnet publish. Seconds, and it
# would have caught both.

$PackageScript = Join-Path $RepoRoot 'scripts\package.ps1'

# -Version is deliberately not the default: a dry run must not be mistakable for a real build in
# any log, and 9.9.9.9 is obviously not a release.
# CALL THIS AS `@(Invoke-PackageDryRun ...)`. PowerShell UNROLLS an array on return, so a one
# architecture dry run comes back as the bare pscustomobject rather than a one-element array, and
# `.Count` on a PSCustomObject under 5.1 is $null — which prints as empty and reads exactly like
# "the script returned nothing". It cost a wrong diagnosis here: a bare `return` and a `continue`
# in package.ps1 were both suspected and both innocent. The `@()` is at the call site because that
# is the scope the array has to survive into.
function Invoke-PackageDryRun {
    param([string]$Brand = '')
    # A HASHTABLE splat, not an array one: splatting an array passes every element POSITIONALLY,
    # so `@('-NoPublish', ...)` lands `-NoPublish` in $Version and the script dies on "Version must
    # have four parts" — which reads exactly like the switch not existing.
    $dryArgs = @{
        NoPublish     = $true
        Architectures = @('arm64')
        Version       = '9.9.9.9'
    }
    if ($Brand) { $dryArgs['Brand'] = $Brand }
    # package.ps1 does Set-Location to the repo root; restore it so a later case is unaffected.
    Push-Location $RepoRoot
    try { return @(& $PackageScript @dryArgs) }
    finally { Pop-Location }
}

# The brand profiles a partner build can select, by the name package.ps1 takes: the file stem
# before `.brand.json`. Enumerated rather than listed, so a new profile is covered the day it
# lands rather than the day someone remembers this file.
$BrandProfiles = @(Get-ChildItem -Path (Join-Path $RepoRoot 'packaging\brands') -Filter '*.brand.json' |
        ForEach-Object { $_.BaseName -replace '\.brand$', '' } | Sort-Object)

Invoke-Case 'package.ps1 computes its arguments without building anything' {
    # NOT "dist\publish\win-arm64 does not exist": a real build legitimately leaves that behind,
    # so asserting its absence fails on any machine that has ever packaged (it was there from
    # 0.1.9.0, built earlier the same morning). What must be true is that the dry run does not
    # WRITE there - the manifest is the file the real path puts in that directory - so compare it
    # across the call. Correct on a clean checkout (absent, then still absent) and on a dirty one.
    $publishManifest = Join-Path $RepoRoot 'dist\publish\win-arm64\AppxManifest.xml'
    $manifestBefore = if (Test-Path $publishManifest) { [string](Get-Item $publishManifest).LastWriteTimeUtc.Ticks } else { 'absent' }

    $result = @(Invoke-PackageDryRun)
    Assert-Equal 'one result for the one architecture asked for' '1' ([string]$result.Count)
    if ($result.Count -ne 1) { return }
    $one = $result[0]
    Assert-Equal 'it reports the architecture' 'arm64' $one.Architecture
    Assert-True 'it computed publish arguments' ($one.PublishArguments.Count -gt 0)
    Assert-True 'they name the app project' (@($one.PublishArguments) -match 'Astrid\.App\.csproj').Count
    Assert-True 'they carry AstridPackaged' (@($one.PublishArguments) -contains '-p:AstridPackaged=true')
    Assert-True 'it computed a manifest' ($one.Manifest -and $one.Manifest.Length -gt 0)

    # The point of the switch: nothing was published, so nothing took four minutes.
    $manifestAfter = if (Test-Path $publishManifest) { [string](Get-Item $publishManifest).LastWriteTimeUtc.Ticks } else { 'absent' }
    Assert-Equal 'the dry run wrote no manifest into the publish layout' $manifestBefore $manifestAfter
}

Invoke-Case 'every manifest placeholder is substituted, for every brand' {
    # 2026-10-25's incident was a manifest error. A leftover {PLACEHOLDER} is the shape of it, and
    # makeappx would reject the whole file for one.
    foreach ($brand in @('') + $BrandProfiles) {
        $label = if ($brand) { $brand } else { '(default)' }
        $result = @(Invoke-PackageDryRun -Brand $brand)
        if ($result.Count -lt 1) {
            Assert-True "$label produced a result" $false
            continue
        }
        $leftover = [regex]::Matches($result[0].Manifest, '\{[A-Z_]+\}') |
            ForEach-Object { $_.Value } | Sort-Object -Unique
        Assert-Equal "$label leaves no placeholder in the manifest" '' ($leftover -join ',')
    }
}

Invoke-Case 'every -p: argument is one well-formed property, for every brand (task 181fb1b5)' {
    # THE 2026-11-13 REGRESSION, as a test. A raw comma or semicolon in a value ends it on
    # MSBuild's command line and the remainder is parsed as a switch; $ @ ' " are evaluated inside
    # a property value. Format-MsBuildProperty escapes all seven, so a well-formed argument has
    # none of them raw after the `=`.
    foreach ($brand in @('') + $BrandProfiles) {
        $label = if ($brand) { $brand } else { '(default)' }
        $result = @(Invoke-PackageDryRun -Brand $brand)
        if ($result.Count -lt 1) {
            Assert-True "$label produced a result" $false
            continue
        }
        $properties = @(@($result[0].PublishArguments) | Where-Object { $_ -like '-p:*' })
        Assert-True "$label passes brand properties at all" ($properties.Count -ge 7)

        $malformed = @()
        foreach ($property in $properties) {
            if ($property -notmatch '^-p:[A-Za-z_][A-Za-z0-9_]*=') {
                $malformed += $property
                continue
            }
            $value = $property.Substring($property.IndexOf('=') + 1)
            if ($value -match '[,;$@''"]') { $malformed += $property }
        }
        Assert-Equal "$label has no argument MSBuild would split or evaluate" '' ($malformed -join ' | ')
    }
}

Invoke-Case 'the value that broke the build arrives escaped (task 181fb1b5)' {
    # Named explicitly rather than left to the sweep above, because this is the exact string from
    # 7bd739e and a future change to the default brand should have to look at this line.
    $default = @(Invoke-PackageDryRun)
    Assert-True 'the default brand escapes its comma-separated auth providers' `
        (@($default[0].PublishArguments) -contains '-p:AstridBrandAuthProviders=passkey%2Cgoogle')

    # And a partner profile, whose list is different and also commas.
    if ($BrandProfiles -contains 'acme') {
        $acme = @(Invoke-PackageDryRun -Brand 'acme')
        Assert-True 'a partner brand escapes its own list' `
            (@($acme[0].PublishArguments) -contains '-p:AstridBrandAuthProviders=sso%2Cgithub%2Cpasskey')
    }
}

Write-Host ''
if ($script:Checks -lt 25) {
    Write-Host "FAIL: only $($script:Checks) assertion(s) ran - this file is meant to make more" -ForegroundColor Red
    $script:Failures++
}
if ($script:Failures -gt 0) {
    Write-Host "RESULT: FAILED - $($script:Failures) failure(s) in $($script:Ran) case(s)" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: OK - $($script:Ran) case(s), $($script:Checks) assertion(s)" -ForegroundColor Green
exit 0
