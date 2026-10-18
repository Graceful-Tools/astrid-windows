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
