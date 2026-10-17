#Requires -Version 5.1
<#
.SYNOPSIS
    The scheduled loop must say it is scheduled, and must yield to a pause. Task 38a93fca.

.DESCRIPTION
    TWO BUGS, both of them silent, both found by an interactive /fixall on 2026-10-16 that stood
    down for the live 17:10 tick and then read that tick's log.

    1. THE LOOP PUBLISHED A PAUSE ABOUT ITSELF. `fixall-session.ts acquire` takes two different
       branches (astrid-web, lines 143-163): with `--scheduled` it just takes the lock, and without
       it also calls `registerHolder()` - a machine-wide "someone is working here" that stands the
       scheduled loops down - and prints

           the scheduled /fixall loops will stand down until this session exits

       The Windows loop omitted the flag, so every tick registered itself as an interactive session
       and logged that sentence about itself. `shouldYield` filters the caller out by `--self-pid`,
       so the loop never actually blocked itself; what it did was lie in the log, and leave a holder
       file behind on any tick that was hard-killed before its `release`. astrid-web's loop
       (`fixall-loop.sh:122`) has always passed `--scheduled`.

    2. THERE WAS NO GUARD 1b, so NOTHING COULD PAUSE THIS LOOP. `grep -n pause fixall-loop.ps1`
       found nothing. The working-tree lock is per CHECKOUT; contention for a machine is per
       MACHINE - on 2026-10-11 three concurrent builds drove the 5-minute load average over 890 and
       turned green tests red on timeouts (AWTD-1192, AWTD-1197). So neither `npm run fixall:pause`
       (for work no session knows about - a manual build, AWTD-1212) nor an interactive acquire in
       `../astrid-web` or `../astrid-core` stopped a Windows tick from starting `npm run predeploy`,
       which cross-builds ARM64 every run, straight through it.

    WHY THIS FILE ASSERTS ON THE SCRIPT RATHER THAN RUNNING IT, which is the opposite of the choice
    fixall-sweep.Tests.ps1 explains at length. Both guards are a shell-out to astrid-web's tsx and a
    branch on the exit code, and running them would mean a sibling checkout, a node process and a
    machine-wide state directory inside the gate - the gate would then fail on a developer who has
    no astrid-web beside this repo, and it would WRITE to the real pause state. What actually
    regressed here is not an exit-code mapping, it is WIRING: a missing flag and an absent call. So
    that is what this checks, and it checks it over the parsed AST rather than the file's text, so
    that a command is one node and the ordering assertions below are structural.

    THE ORDERING IS THE PART THAT IS EASY TO RE-BREAK. Guard 1b has to sit:

      * AFTER the acquire, because standing down is pointless if the tree is already somebody's;
      * BEFORE the start-of-tick sweep, because the sweep makes git commits and posts board
        comments, and a tick that stands down after doing those has stood down too late;
      * INSIDE the main `try`, whose `finally` releases the lock - a skip from outside it would
        leak the working-tree lock and the NEXT tick would refuse this checkout.

    The third is the one a careless port gets wrong, and none of the three shows up as a test
    failure anywhere else.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-loop-guards.Tests.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LoopPath = Join-Path $RepoRoot 'scripts\fixall-loop.ps1'
if (-not (Test-Path $LoopPath)) {
    Write-Host "FAIL: no loop script at $LoopPath" -ForegroundColor Red
    exit 1
}

$script:Failures = 0
$script:Ran = 0
# Assertions MADE, not passed. A case that exits early makes none, and that is the one outcome a
# failure count cannot see - fixall-sweep.Tests.ps1 learned this by printing OK for six vacuous
# cases, and a false OK in the gate that guards the scheduled loop is the bug this file is about.
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

function Assert-NotMatch([string]$What, [string]$Pattern, [string]$Actual) {
    $script:Checks++
    if ($Actual -notmatch $Pattern) { return }
    Write-Host "    FAIL $What" -ForegroundColor Red
    Write-Host "      expected NOT to match: [$Pattern]" -ForegroundColor Red
    Write-Host "      actual:                [$Actual]" -ForegroundColor Red
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

# ── The loop, parsed ──────────────────────────────────────────────────────────────────────────
# A syntax error here is a loop that cannot run at all, so it fails the gate before any assertion
# about its contents could be meaningful.
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

# One command's own extent, so "the acquire passes --scheduled" cannot be satisfied by the word
# turning up somewhere else in an 80-line region.
function Find-LoopCommand([string[]]$AllOf) {
    foreach ($cmd in $Commands) {
        $text = $cmd.Extent.Text
        $ok = $true
        foreach ($needle in $AllOf) { if ($text -notmatch $needle) { $ok = $false; break } }
        if ($ok) { return $cmd }
    }
    return $null
}

$Acquire = Find-LoopCommand @('fixall-session\.ts', "'acquire'")
$Yield = Find-LoopCommand @('fixall-pause\.ts', "'yield'")
$Sweep = Find-LoopCommand @('Invoke-StartOfTickSweep')

Write-Host ''
Write-Host 'the scheduled loop says it is scheduled, and yields to a pause (task 38a93fca)' -ForegroundColor Cyan

Invoke-Case 'guard 1 acquires the working-tree lock as a SCHEDULED session' {
    Assert-True 'the loop calls fixall-session.ts acquire' ($null -ne $Acquire)
    if (-not $Acquire) { return }
    # Without this the acquire takes the interactive branch: it registers a machine-wide holder and
    # prints "the scheduled /fixall loops will stand down until this session exits" about itself.
    Assert-Match 'and passes --scheduled' "--scheduled" $Acquire.Extent.Text
    Assert-Match 'identifying this harness' "'claude-code'" $Acquire.Extent.Text
}

Invoke-Case 'guard 1b asks fixall-pause.ts whether to stand down' {
    Assert-True 'the loop calls fixall-pause.ts yield' ($null -ne $Yield)
    if (-not $Yield) { return }
    # shouldYield() filters the caller out of the live-holder list by pid. Without --self-pid the
    # loop could stand down for its own registration - the deadlock the flag above exists to avoid.
    Assert-Match 'passing --self-pid so it cannot yield to itself' '--self-pid' $Yield.Extent.Text
    Assert-Match 'and the pid is this process' '\$PID' $Yield.Extent.Text
}

Invoke-Case 'guard 1b runs after the lock and before the sweep' {
    Assert-True 'the sweep is still called' ($null -ne $Sweep)
    if (-not $Acquire -or -not $Yield -or -not $Sweep) {
        Assert-True 'all three commands were found' $false
        return
    }
    Assert-True 'the yield comes after the acquire' ($Yield.Extent.StartOffset -gt $Acquire.Extent.StartOffset)
    # The sweep commits, pushes and comments on tasks. Standing down after it has stood down too
    # late: the tick has already written to two checkouts and to the board.
    Assert-True 'and before the start-of-tick sweep' ($Yield.Extent.StartOffset -lt $Sweep.Extent.StartOffset)
}

Invoke-Case 'a stand-down releases the working-tree lock' {
    # Structural, not textual: the guard has to be inside the try whose finally releases the lock,
    # or a skip leaks it and the NEXT tick refuses this checkout as held by a dead process.
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
    if (-not $main -or -not $Yield) { return }
    Assert-True 'whose finally releases the lock' ($null -ne $main.Finally)
    Assert-Match 'by calling the release closure' 'releaseLock' $main.Finally.Extent.Text
    Assert-True 'and guard 1b is inside that same try' (
        $Yield.Extent.StartOffset -gt $main.Body.Extent.StartOffset -and
        $Yield.Extent.EndOffset -lt $main.Body.Extent.EndOffset)
}

Invoke-Case 'exit 2 skips the tick; anything else runs it anyway' {
    if (-not $Yield -or -not $Sweep) {
        Assert-True 'guard 1b exists to have a verdict at all' $false
        return
    }
    # The guard's own branches, found as AST nodes rather than by slicing the file between two
    # offsets. Extent offsets count the UTF-8 BOM and `Get-Content -Raw` strips it, so a substring
    # taken that way starts three characters late - which is how the first version of this case
    # failed on a region beginning `ULT: SKIPPED`. Asking the AST for the `if`s that mention
    # $yieldStatus needs no offsets at all, and cannot drift onto a neighbouring statement.
    $region = ((@($Ast.FindAll(
                    { param($n) $n -is [System.Management.Automation.Language.IfStatementAst] }, $true)) |
                Where-Object { $_.Extent.Text -match '\$yieldStatus' } |
                ForEach-Object { $_.Extent.Text }) -join "`n")

    Assert-True 'the exit code is branched on' ($region.Length -gt 0)
    Assert-Match 'exit 2 is the stand-down' '-eq\s+2' $region
    Assert-Match 'and it ends the tick as a SKIP, not a failure' 'RESULT: SKIPPED' $region
    # The library's own header is explicit about this: a loop that stalls on an unreadable pause
    # file is a worse outage than one run too many. Same call guard 2b makes about an unreadable
    # load average.
    Assert-NotMatch 'an unreadable pause state is never fatal' 'RESULT: FAILED' $region
    Assert-Match 'it warns and runs the tick' 'running anyway' $region
}

Write-Host ''
if ($script:Checks -lt 15) {
    Write-Host "FAIL: only $($script:Checks) assertion(s) ran - this file is meant to make more" -ForegroundColor Red
    $script:Failures++
}
if ($script:Failures -gt 0) {
    Write-Host "RESULT: FAILED - $($script:Failures) failure(s) in $($script:Ran) case(s)" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: OK - $($script:Ran) case(s), $($script:Checks) assertion(s)" -ForegroundColor Green
exit 0
