#Requires -Version 5.1
<#
.SYNOPSIS
    A tick checks for disk before it spends itself finding out, and the caches have a pruner.
    Task 4d9d42e6.

.DESCRIPTION
    WHAT HAPPENED. The scheduled tick of 2026-11-13 05:10 died three quarters of the way through
    its first build with

        error: failed to build archive at `...\target\debug\deps\libastrid_core-....rlib`:
        There is not enough space on the disk. (os error 112)

    `C:` had 31 MB free of 509 GB used: `astrid-windows/target` was 103 GB and
    `astrid-core/target` 51 GB. `cargo clean` in astrid-core freed the 51 GB and the tick's work
    continued, but nothing has ever pruned either tree - `npm run predeploy` builds host debug,
    host release, both cross targets and the .NET shell in Release, and every pin bump adds a
    fresh `astrid-core-<hash>` tree beside the ones before it. The artefacts of every revision
    this app has ever pinned are still on disk.

    SO THERE ARE TWO BUGS, and they need different fixes.

    1. THE TICK FINDS OUT BY FAILING. An `os error 112` deep inside a build reads exactly like a
       broken change, and it costs a whole tick to produce. A free-space check is three lines and
       runs before anything is built.

    2. NOTHING PRUNES. A guard with no way to clear it is a loop that stops for ever, so the
       guard's own message has to name a command that fixes it - and that command has to be
       cheaper than `cargo clean`, which costs the next gate a cold build of the whole graph.

    WHY THIS FILE IS SPLIT IN THREE. The decision is pure and lives in scripts/lib/fixall-disk.ps1,
    so it is tested by value over its thresholds. The WIRING in fixall-loop.ps1 cannot be executed
    inside the gate - the loop takes a machine-wide lock and starts a Claude session - so it is
    asserted over the parsed AST, the same choice fixall-loop-guards.Tests.ps1 makes and explains.
    The pruner IS run, against a scratch tree, because "report-only unless -Apply" is a claim about
    behaviour and an AST assertion would not catch a `Remove-Item` that ran anyway.

    THE UNREADABLE CASE RUNS THE TICK. Same call guard 1b and guard 2b already make about an
    unreadable pause state and an unreadable load average: a loop that stalls on a reading it could
    not take is a worse outage than one build too many.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-disk.Tests.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LoopPath = Join-Path $RepoRoot 'scripts\fixall-loop.ps1'
$LibPath = Join-Path $RepoRoot 'scripts\lib\fixall-disk.ps1'
$PrunePath = Join-Path $RepoRoot 'scripts\prune-build-caches.ps1'
$PredeployPath = Join-Path $RepoRoot 'scripts\predeploy.ps1'

foreach ($required in @($LoopPath, $LibPath, $PrunePath, $PredeployPath)) {
    if (-not (Test-Path $required)) {
        Write-Host "FAIL: missing $required" -ForegroundColor Red
        exit 1
    }
}

$script:Failures = 0
$script:Ran = 0
# Assertions MADE, not passed - a case that exits early makes none, and that is the one outcome a
# failure count cannot see. See fixall-loop-guards.Tests.ps1.
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

. $LibPath

$GB = 1073741824L

# ── The decision, by value ────────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host 'a tick decides on free space before it builds anything (task 4d9d42e6)' -ForegroundColor Cyan

Invoke-Case 'plenty of room is a silent OK' {
    $v = Get-DiskSpaceVerdict -FreeBytes (200 * $GB) -FloorBytes (15 * $GB) -WarnBytes (40 * $GB)
    Assert-Equal 'the tick runs' 'True' $v.Ok
    Assert-Equal 'and says nothing' 'ok' $v.Level
    Assert-Equal 'with no reason to print' '' $v.Reason
}

Invoke-Case 'under the floor stops the tick BEFORE it builds' {
    # 31 MB, which is what the 05:10 tick actually had.
    $v = Get-DiskSpaceVerdict -FreeBytes 32505856L -FloorBytes (15 * $GB) -WarnBytes (40 * $GB)
    Assert-Equal 'the tick does not start' 'False' $v.Ok
    Assert-Equal 'and it is a failure, not a skip' 'critical' $v.Level
    # The numbers, because the point of the guard is that the log says what was wrong rather than
    # leaving an os error 112 that reads like a broken change.
    Assert-Match 'the reason gives the measured free space' '0\.03 GB' $v.Reason
    Assert-Match 'and the floor it is under' '15 GB' $v.Reason
    # A guard with no way to clear it is a loop that stops for ever.
    Assert-Match 'and names the command that clears it' 'prune-build-caches' $v.Reason
}

Invoke-Case 'between the floor and the warn mark the tick runs, and says so' {
    $v = Get-DiskSpaceVerdict -FreeBytes (30 * $GB) -FloorBytes (15 * $GB) -WarnBytes (40 * $GB)
    Assert-Equal 'the tick still runs' 'True' $v.Ok
    Assert-Equal 'but it is low' 'low' $v.Level
    Assert-Match 'and the warning names the pruner too' 'prune-build-caches' $v.Reason
}

Invoke-Case 'exactly on the floor is not under it' {
    # The boundary, written down: the 05:10 failure was a build that needed more than was there, so
    # "at the floor" is the lowest value the tick is still allowed to start on.
    $v = Get-DiskSpaceVerdict -FreeBytes (15 * $GB) -FloorBytes (15 * $GB) -WarnBytes (40 * $GB)
    Assert-Equal 'the tick runs' 'True' $v.Ok
    Assert-Equal 'as a low one' 'low' $v.Level
}

Invoke-Case 'a reading nobody could take RUNS the tick' {
    # Same call guard 1b makes about an unreadable pause state and guard 2b about an unreadable
    # load average: stalling on a reading that failed is a worse outage than one build too many.
    # PARENTHESISED. `-FreeBytes -1L` is not negative one: PowerShell reads the `-1L` that follows
    # a parameter name as a bare argument TOKEN and hands the string "-1L" to the [long] cast,
    # which fails with "Input string was not in a correct format". The parentheses force it to be
    # parsed as an expression.
    $v = Get-DiskSpaceVerdict -FreeBytes (-1L) -FloorBytes (15 * $GB) -WarnBytes (40 * $GB)
    Assert-Equal 'the tick runs' 'True' $v.Ok
    Assert-Equal 'and the level says why it is not a verdict' 'unknown' $v.Level
    Assert-Match 'the reason says the reading failed' 'could not' $v.Reason
}

# ── The wiring, over the parsed loop ──────────────────────────────────────────────────────────

$tokens = $null
$errors = $null
$Ast = [System.Management.Automation.Language.Parser]::ParseFile($LoopPath, [ref]$tokens, [ref]$errors)
if ($errors -and $errors.Count -gt 0) {
    Write-Host "FAIL: scripts/fixall-loop.ps1 does not parse ($($errors.Count) error(s)):" -ForegroundColor Red
    $errors | Select-Object -First 5 | ForEach-Object { Write-Host "    $($_.Message)" -ForegroundColor Red }
    exit 1
}
$Commands = @($Ast.FindAll(
        { param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))

function Find-LoopCommand([string[]]$AllOf) {
    foreach ($cmd in $Commands) {
        $text = $cmd.Extent.Text
        $ok = $true
        foreach ($needle in $AllOf) { if ($text -notmatch $needle) { $ok = $false; break } }
        if ($ok) { return $cmd }
    }
    return $null
}

$DotSource = Find-LoopCommand @('fixall-disk\.ps1')
$Verdict = Find-LoopCommand @('Get-DiskSpaceVerdict')
$Yield = Find-LoopCommand @('fixall-pause\.ts', "'yield'")
$Sweep = Find-LoopCommand @('Invoke-StartOfTickSweep')

Invoke-Case 'the loop takes the reading and asks the library' {
    Assert-True 'it dot-sources scripts/lib/fixall-disk.ps1' ($null -ne $DotSource)
    Assert-True 'and calls Get-DiskSpaceVerdict' ($null -ne $Verdict)
}

Invoke-Case 'guard 1c runs after the stand-down and before the sweep' {
    if (-not $Verdict -or -not $Yield -or -not $Sweep) {
        Assert-True 'the guard and its neighbours were all found' $false
        return
    }
    # AFTER 1b: a machine someone else is working is not this tick's disk problem to report.
    Assert-True 'the disk check comes after guard 1b' ($Verdict.Extent.StartOffset -gt $Yield.Extent.StartOffset)
    # BEFORE the sweep, which makes git commits in two checkouts and posts board comments. A commit
    # onto a full disk is how a checkout gets a corrupt object, and reporting after it has already
    # written is reporting too late.
    Assert-True 'and before the start-of-tick sweep' ($Verdict.Extent.StartOffset -lt $Sweep.Extent.StartOffset)
}

Invoke-Case 'a stop on disk still releases the working-tree lock' {
    $tries = @($Ast.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.TryStatementAst] }, $true))
    $main = $null
    if ($Sweep) {
        $main = $tries | Where-Object {
            $_.Body.Extent.StartOffset -lt $Sweep.Extent.StartOffset -and
            $_.Body.Extent.EndOffset -gt $Sweep.Extent.EndOffset
        } | Select-Object -First 1
    }
    Assert-True 'the sweep runs inside a try/finally' ($null -ne $main)
    if (-not $main -or -not $Verdict) { return }
    Assert-Match 'whose finally releases the lock' 'releaseLock' $main.Finally.Extent.Text
    # Outside the try, a stop here would leak the lock and the NEXT tick would refuse this checkout
    # as held by a process that is gone - the same trap guard 1b documents.
    Assert-True 'and guard 1c is inside that same try' (
        $Verdict.Extent.StartOffset -gt $main.Body.Extent.StartOffset -and
        $Verdict.Extent.EndOffset -lt $main.Body.Extent.EndOffset)
}

Invoke-Case 'the stop is a FAILED, and a low reading is not' {
    $region = ((@($Ast.FindAll(
                    { param($n) $n -is [System.Management.Automation.Language.IfStatementAst] }, $true)) |
            Where-Object { $_.Extent.Text -match '\$diskVerdict' } |
            ForEach-Object { $_.Extent.Text }) -join "`n")
    Assert-True 'the verdict is branched on' ($region.Length -gt 0)
    # FAILED and not SKIPPED: a full disk is something to fix, not a quiet day. A SKIPPED tick is
    # indistinguishable from an empty board, which is how this machine would go silent for a week.
    Assert-Match 'a critical reading ends the tick as FAILED' 'RESULT: FAILED' $region
    Assert-Match 'carrying the reason the library wrote' '\$diskVerdict\.Reason' $region
}

# ── The pruner, actually run ──────────────────────────────────────────────────────────────────

function New-ScratchTree {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("astrid-prune-4d9d42e6-" + [guid]::NewGuid().ToString('N'))
    $deps = Join-Path $root 'target\debug\deps'
    New-Item -ItemType Directory -Path $deps -Force | Out-Null
    $stale = Join-Path $deps 'libastrid_core-0ldrev.rlib'
    $fresh = Join-Path $deps 'libastrid_core-newrev.rlib'
    Set-Content -LiteralPath $stale -Value 'stale' -Encoding ascii
    Set-Content -LiteralPath $fresh -Value 'fresh' -Encoding ascii
    # The artefacts of a revision this app pinned months ago, which is what 103 GB is made of.
    (Get-Item $stale).LastWriteTime = (Get-Date).AddDays(-90)
    return [pscustomobject]@{ Root = $root; Stale = $stale; Fresh = $fresh }
}

Invoke-Case 'the pruner reports and deletes NOTHING by default' {
    $t = New-ScratchTree
    try {
        $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $PrunePath -Root $t.Root -Days 14 2>&1 | Out-String
        # CLAUDE.md requires asking before deleting files. A pruner whose default run deletes is one
        # a scheduled loop could be pointed at by accident.
        Assert-True 'the stale artefact is still there' (Test-Path $t.Stale)
        Assert-True 'and so is the fresh one' (Test-Path $t.Fresh)
        Assert-Match 'it says it changed nothing' '(?i)report only|-Apply' $out
        # Option 3 on the task: bin/ and obj/ hold their own copies per configuration and target.
        Assert-Match 'and it measures the tree it was given' '(?i)target' $out
    }
    finally { Remove-Item $t.Root -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case '-Apply removes what is stale and keeps what the current build needs' {
    $t = New-ScratchTree
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $PrunePath -Root $t.Root -Days 14 -Apply 2>&1 | Out-Null
        # Age, not name: cargo rebuilds anything it finds missing, so the only unsafe prune is one
        # that takes what the CURRENT build is still touching.
        Assert-Equal 'the 90-day-old revision is gone' 'False' (Test-Path $t.Stale)
        Assert-Equal 'the one this build is using is kept' 'True' (Test-Path $t.Fresh)
    }
    finally { Remove-Item $t.Root -Recurse -Force -ErrorAction SilentlyContinue }
}

# ── And the gate runs this file ───────────────────────────────────────────────────────────────

Invoke-Case 'predeploy runs this test' {
    # The script tests run FIRST in predeploy and seconds long, because what they guard is the
    # scheduled loop that runs everything below them. A test file nothing runs guards nothing.
    $predeploy = Get-Content $PredeployPath -Raw
    Assert-Match 'predeploy.ps1 lists fixall-disk.Tests.ps1' 'fixall-disk\.Tests\.ps1' $predeploy
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
