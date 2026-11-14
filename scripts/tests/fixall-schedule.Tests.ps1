#Requires -Version 5.1
<#
.SYNOPSIS
    The registered scheduled task is compared against what this checkout's installer would produce,
    and a tick says so when they disagree. Task 18ba8c2e.

.DESCRIPTION
    WHAT HAPPENED. Scope item 3 of task 18ba8c2e fixed a real outage on 2026-10-28:
    `install-fixall-task.ps1` set the scheduler's `ExecutionTimeLimit` to a literal 55 minutes
    against a 75-minute launcher watchdog, so Task Scheduler killed every long tick twenty minutes
    BEFORE the watchdog meant to catch it - and a scheduler kill runs none of the loop's recovery:
    no WIP commit, no `Doing` release, no wake acknowledgement, no comment on the task it had taken.
    The fix landed in the repo the same day.

    SEVENTEEN DAYS LATER THE MACHINE WAS STILL RUNNING THE OLD ENTRY. A scheduled task is
    registered, not read from the repo, so editing the installer changes nothing until somebody
    re-runs it - and nothing anywhere noticed. On 2026-11-14 the registration still read

        <ExecutionTimeLimit>PT55M</ExecutionTimeLimit>
        <Arguments>-NoProfile -ExecutionPolicy Bypass -File "...\scripts\fixall-loop.ps1"</Arguments>

    with no `-MaxMinutes` at all, while the installer in the repo would by then compute
    max(75, 480) + 10 = 490 minutes. Task `0216d83c` had meanwhile landed LONG-RUN support, so the
    gap had grown teeth it did not have when it was written: a tick that astrid-web's preflight
    legitimately gives eight hours is stopped at fifty-five minutes.

    SO THE BUG IS NOT THE NUMBER, IT IS THAT NOBODY COULD SEE THE NUMBER WAS WRONG. Two fixes, and
    they are different in kind:

    1. ONE PLACE COMPUTES BOTH. `scripts/lib/fixall-schedule.ps1` owns the deadline arithmetic and
       the loop's argument string; the installer builds both from it rather than inline. Two
       independently-written expressions is how 55 and 75 drifted apart in the first place.

    2. EVERY TICK CHECKS THE REGISTRATION IT IS RUNNING UNDER. The loop compares the registered
       entry against what the installer would produce now and says one line when they disagree,
       naming the command that fixes it.

    A WARNING AND NEVER A STOP. A drifted deadline is not a reason to take the loop out: the tick
    still runs fine, it is only bounded wrongly, and a guard that stopped on it would turn a
    cosmetic gap into an outage. And an unreadable registration says NOTHING - a loop started by
    hand with `-Force` has no registration to compare against, and nagging every half hour about a
    reading that could not be taken is how a warning stops being read.

    WHY THIS FILE IS SPLIT IN TWO. The decision is pure and tested by value. The WIRING in
    fixall-loop.ps1 and install-fixall-task.ps1 cannot be executed inside the gate - the loop takes
    a machine-wide lock and starts a Claude session, and the installer would re-register the real
    scheduled task on the machine running the tests - so both are asserted over the parsed AST, the
    same choice fixall-disk.Tests.ps1 and fixall-loop-guards.Tests.ps1 make and explain.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-schedule.Tests.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LoopPath = Join-Path $RepoRoot 'scripts\fixall-loop.ps1'
$LibPath = Join-Path $RepoRoot 'scripts\lib\fixall-schedule.ps1'
$InstallPath = Join-Path $RepoRoot 'scripts\install-fixall-task.ps1'
$PredeployPath = Join-Path $RepoRoot 'scripts\predeploy.ps1'
$AutomationDoc = Join-Path $RepoRoot 'docs\AUTOMATION.md'

foreach ($required in @($LoopPath, $LibPath, $InstallPath, $PredeployPath, $AutomationDoc)) {
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

function Assert-NoMatch([string]$What, [string]$Pattern, [string]$Actual) {
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

. $LibPath

# A loop script path that does not need to exist: the comparison is about what the registration
# SAYS, not about what is on disk, and a test that depended on the real checkout's path would pass
# for the wrong reason on the one machine it matters on.
$Loop = Join-Path $env:SystemDrive 'checkout\astrid-windows\scripts\fixall-loop.ps1'

function New-Registration([string]$Argument, [int]$DeadlineMinutes) {
    [pscustomobject]@{ Found = $true; Argument = $Argument; DeadlineMinutes = $DeadlineMinutes }
}

# ── The arithmetic, in one place ──────────────────────────────────────────────────────────────

Write-Host ''
Write-Host 'the deadline and the loop argument are computed once (task 18ba8c2e)' -ForegroundColor Cyan

Invoke-Case 'the deadline covers the longest tick that is possible, plus recovery' {
    # The loop sizes its OWN watchdog per tick from astrid-web's plan, so by the time anything knows
    # this is an eight-hour run the scheduler's deadline is already fixed. It has to be computed for
    # the LONG-RUN ceiling, not the usual case - task 0216d83c.
    Assert-Equal 'max(75, 480) + 10' 490 (Get-FixallScheduleDeadlineMinutes -WatchdogMinutes 75 -RecoveryMinutes 10 -LongRunMinutes 480)
}

Invoke-Case 'long runs off falls back to the watchdog plus recovery' {
    Assert-Equal 'max(75, 0) + 10' 85 (Get-FixallScheduleDeadlineMinutes -WatchdogMinutes 75 -RecoveryMinutes 10 -LongRunMinutes 0)
}

Invoke-Case 'a watchdog above the long-run ceiling still wins' {
    Assert-Equal 'max(200, 120) + 5' 205 (Get-FixallScheduleDeadlineMinutes -WatchdogMinutes 200 -RecoveryMinutes 5 -LongRunMinutes 120)
}

Invoke-Case 'the argument forwards the watchdog the deadline was computed from' {
    # NEVER left to the loop's own default: a deadline derived from one number while the loop obeys
    # another is exactly the drift this task fixed.
    $arg = Get-FixallScheduleArgument -LoopScript $Loop -WatchdogMinutes 75
    Assert-Match 'it runs the loop script, quoted against spaces in the path' '-File "[^"]+fixall-loop\.ps1"' $arg
    Assert-Match 'and forwards the watchdog explicitly' '-MaxMinutes 75(\s|$)' $arg
    Assert-Match 'with no profile' '-NoProfile' $arg
}

# ── The drift verdict, by value ───────────────────────────────────────────────────────────────

Write-Host ''
Write-Host 'a tick compares the registration it is running under (task 18ba8c2e)' -ForegroundColor Cyan

Invoke-Case 'a matching registration is a silent OK' {
    $reg = New-Registration (Get-FixallScheduleArgument -LoopScript $Loop -WatchdogMinutes 75) 490
    $v = Get-ScheduleDriftVerdict -Registration $reg -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490
    Assert-Equal 'it is in sync' 'True' $v.InSync
    Assert-Equal 'and says nothing' 'ok' $v.Level
    Assert-Equal 'with no reason to print' '' $v.Reason
}

Invoke-Case 'the live 2026-11-14 drift: PT55M and no -MaxMinutes' {
    # The regression this file exists for, by value. The entry registered on this machine on
    # 2026-09-25 and never re-registered, against what the installer computes on 2026-11-14.
    $reg = New-Registration "-NoProfile -ExecutionPolicy Bypass -File `"$Loop`"" 55
    $v = Get-ScheduleDriftVerdict -Registration $reg -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490
    Assert-Equal 'the registration is stale' 'False' $v.InSync
    Assert-Equal 'and it is stale, not unreadable' 'stale' $v.Level
    Assert-Match 'the reason names what is registered' '55' $v.Reason
    Assert-Match 'and what the installer would produce' '490' $v.Reason
    Assert-Match 'and that the watchdog is not forwarded at all' '-MaxMinutes' $v.Reason
    Assert-Match 'and names the way out' 'install-fixall-task\.ps1' $v.Reason
    Assert-Equal 'on one line, because it goes into the log beside the other warnings' 1 ($v.Reason -split "`n").Count
}

Invoke-Case 'a wrong deadline alone is drift' {
    $reg = New-Registration (Get-FixallScheduleArgument -LoopScript $Loop -WatchdogMinutes 75) 85
    $v = Get-ScheduleDriftVerdict -Registration $reg -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490
    Assert-Equal 'it is stale' 'False' $v.InSync
    Assert-Match 'the reason names the registered deadline' '85' $v.Reason
    Assert-NoMatch 'and does not complain about the watchdog, which is right' 'no -MaxMinutes' $v.Reason
}

Invoke-Case 'a registration with no execution time limit at all is drift' {
    # -1 is the reader's word for "the entry sets no limit". It is not the same as unreadable: an
    # unbounded task means a wedged launcher is never stopped by anything outside itself.
    $reg = New-Registration (Get-FixallScheduleArgument -LoopScript $Loop -WatchdogMinutes 75) -1
    $v = Get-ScheduleDriftVerdict -Registration $reg -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490
    Assert-Equal 'it is stale' 'False' $v.InSync
    Assert-Match 'and the reason says there is no limit' 'no execution time limit' $v.Reason
}

Invoke-Case 'a watchdog that disagrees with the one the deadline came from is drift' {
    $reg = New-Registration (Get-FixallScheduleArgument -LoopScript $Loop -WatchdogMinutes 55) 490
    $v = Get-ScheduleDriftVerdict -Registration $reg -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490
    Assert-Equal 'it is stale' 'False' $v.InSync
    Assert-Match 'the reason names the registered watchdog' '55' $v.Reason
    Assert-Match 'and the expected one' '75' $v.Reason
}

Invoke-Case 'a registration pointing at another checkout is drift' {
    # install-fixall-task.ps1's header says "re-run it after moving the checkout" and nothing has
    # ever checked that either: a moved checkout leaves the scheduler running a loop script that may
    # not exist, under a working directory that does not either.
    $other = Join-Path $env:SystemDrive 'elsewhere\astrid-windows\scripts\fixall-loop.ps1'
    $reg = New-Registration (Get-FixallScheduleArgument -LoopScript $other -WatchdogMinutes 75) 490
    $v = Get-ScheduleDriftVerdict -Registration $reg -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490
    Assert-Equal 'it is stale' 'False' $v.InSync
    Assert-Match 'and the reason names the script it actually starts' 'elsewhere' $v.Reason
}

Invoke-Case 'the same checkout spelled differently is not drift' {
    # Task Scheduler's spelling of a path and an interactive shell's differ in case and in trailing
    # separators. A warning that fires every half hour on a difference of case is a warning nobody
    # reads - the same reasoning as the worker scope in fixall-sweep.ps1.
    $shouty = $Loop.ToUpperInvariant().Replace('\SCRIPTS\', '\scripts\.\..\scripts\')
    $reg = New-Registration (Get-FixallScheduleArgument -LoopScript $shouty -WatchdogMinutes 75) 490
    $v = Get-ScheduleDriftVerdict -Registration $reg -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490
    Assert-Equal 'it is in sync' 'True' $v.InSync
    Assert-Equal 'with nothing to say' '' $v.Reason
}

Invoke-Case 'all three wrong at once is still one line' {
    $other = Join-Path $env:SystemDrive 'elsewhere\astrid-windows\scripts\fixall-loop.ps1'
    $reg = New-Registration "-NoProfile -ExecutionPolicy Bypass -File `"$other`" -MaxMinutes 55" 55
    $v = Get-ScheduleDriftVerdict -Registration $reg -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490
    Assert-Equal 'it is stale' 'False' $v.InSync
    Assert-Match 'the deadline is named' '55' $v.Reason
    Assert-Match 'the checkout is named' 'elsewhere' $v.Reason
    Assert-Match 'the way out is named' 'install-fixall-task\.ps1' $v.Reason
    Assert-Equal 'and it is one line' 1 ($v.Reason -split "`n").Count
}

Invoke-Case 'no registration says nothing and runs the tick' {
    # A loop started by hand with -Force has no registration to compare against. Same call guard 1b
    # makes about an unreadable pause state and guard 1c about an unreadable disk reading: a tick
    # that could not take a reading runs anyway, and says nothing it cannot stand behind.
    $v = Get-ScheduleDriftVerdict -Registration $null -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490
    Assert-Equal 'the tick is not held up' 'True' $v.InSync
    Assert-Equal 'the level says the reading was not taken' 'unknown' $v.Level
    Assert-Equal 'and there is nothing to print' '' $v.Reason
}

Invoke-Case 'an unreadable registration says nothing either' {
    $reg = [pscustomobject]@{ Found = $false; Argument = ''; DeadlineMinutes = -1 }
    $v = Get-ScheduleDriftVerdict -Registration $reg -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490
    Assert-Equal 'the tick is not held up' 'True' $v.InSync
    Assert-Equal 'the level says the reading was not taken' 'unknown' $v.Level
    Assert-Equal 'and there is nothing to print' '' $v.Reason
}

Invoke-Case 'reading a task that is not registered is not an error' {
    # The reader is the one part that touches the machine, so it is exercised against a name that
    # cannot exist rather than against the real entry - registering or reading the real one would
    # make the gate depend on how this machine happens to be set up.
    $reg = Read-FixallScheduleRegistration -TaskName 'Astrid fixall (no such task 18ba8c2e)'
    Assert-Equal 'it reports not found rather than throwing' 'False' $reg.Found
    Assert-Equal 'and the verdict built from it is silent' '' (Get-ScheduleDriftVerdict -Registration $reg -LoopScript $Loop -WatchdogMinutes 75 -DeadlineMinutes 490).Reason
}

# ── The wiring, over the parsed loop and installer ────────────────────────────────────────────

Write-Host ''
Write-Host 'the loop and the installer both use it (task 18ba8c2e)' -ForegroundColor Cyan

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

$LoopAst = Get-ScriptAst $LoopPath
$InstallAst = Get-ScriptAst $InstallPath

function Find-CommandIn($Ast, [string[]]$AllOf) {
    $commands = @($Ast.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
    foreach ($cmd in $commands) {
        $text = $cmd.Extent.Text
        $ok = $true
        foreach ($needle in $AllOf) { if ($text -notmatch $needle) { $ok = $false; break } }
        if ($ok) { return $cmd }
    }
    return $null
}

$LoopSource = Get-Content $LoopPath -Raw
$InstallSource = Get-Content $InstallPath -Raw

Invoke-Case 'the loop asks the library about its own registration' {
    Assert-True 'it dot-sources scripts/lib/fixall-schedule.ps1' ($null -ne (Find-CommandIn $LoopAst @('fixall-schedule\.ps1')))
    Assert-True 'it reads the registration' ($null -ne (Find-CommandIn $LoopAst @('Read-FixallScheduleRegistration')))
    Assert-True 'and asks for the verdict' ($null -ne (Find-CommandIn $LoopAst @('Get-ScheduleDriftVerdict')))
}

Invoke-Case 'the loop warns and never stops on drift' {
    # The whole point: the tick still runs. A guard that stopped here would turn "bounded wrongly"
    # into "this machine went quiet", which is the trade fixall-disk.ps1's header argues about the
    # other way round for a full disk - there, the build genuinely cannot succeed.
    $ifs = @($LoopAst.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.IfStatementAst] }, $true))
    $guard = $ifs | Where-Object { $_.Extent.Text -match 'Drift|InSync' } | Select-Object -First 1
    Assert-True 'the drift verdict is acted on in an if' ($null -ne $guard)
    if (-not $guard) { return }
    Assert-Match 'it says something' 'Say ' $guard.Extent.Text
    Assert-NoMatch 'and it never calls Finish' 'Finish ' $guard.Extent.Text
    Assert-NoMatch 'and never exits' '(^|\s)exit\s' $guard.Extent.Text
}

Invoke-Case 'the installer builds both numbers from the library, not inline' {
    Assert-True 'it dot-sources the library' ($null -ne (Find-CommandIn $InstallAst @('fixall-schedule\.ps1')))
    Assert-True 'it computes the deadline there' ($null -ne (Find-CommandIn $InstallAst @('Get-FixallScheduleDeadlineMinutes')))
    Assert-True 'and the argument there' ($null -ne (Find-CommandIn $InstallAst @('Get-FixallScheduleArgument')))
    # The inline expression is what drifted from the loop's watchdog in the first place. It must not
    # survive beside the shared one, or a future edit lands on whichever copy was read first.
    Assert-NoMatch 'the inline Math::Max is gone' '\[Math\]::Max\(\$WatchdogMinutes' $InstallSource
    Assert-NoMatch 'and so is the inline argument string' '-ExecutionPolicy Bypass -File `"\$LoopScript`"' $InstallSource
}

Invoke-Case 'the installer defaults and the library constants are the same numbers' {
    # Two files still name the defaults - the installer so that -WatchdogMinutes can be overridden
    # with ValidateRange, the library so the loop can compare without taking the installer's param
    # block apart. This is the assertion that keeps them from becoming 55 and 75 again.
    Assert-Match 'the installer defaults the watchdog to the library constant' "WatchdogMinutes = $(Get-FixallScheduleDefault Watchdog)\b" $InstallSource
    Assert-Match 'the recovery headroom likewise' "RecoveryMinutes = $(Get-FixallScheduleDefault Recovery)\b" $InstallSource
    Assert-Match 'and the long-run ceiling likewise' "LongRunMinutes = $(Get-FixallScheduleDefault LongRun)\b" $InstallSource
    Assert-Match 'and they share the task name' ([regex]::Escape((Get-FixallScheduleDefault TaskName))) $InstallSource
}

Invoke-Case 'predeploy runs this test' {
    # The script tests run FIRST in predeploy and are seconds long, because what they guard is the
    # scheduled loop that runs everything below them. A test file nothing runs guards nothing.
    Assert-Match 'predeploy.ps1 lists fixall-schedule.Tests.ps1' 'fixall-schedule\.Tests\.ps1' (Get-Content $PredeployPath -Raw)
}

Invoke-Case 'AUTOMATION.md says the registration is checked' {
    Assert-Match 'the doc names the drift check' '(?i)drift' (Get-Content $AutomationDoc -Raw)
}

Invoke-Case 'the loop checks before it spends the tick' {
    # Early, with the other preamble warnings, so the line is in the log even for a tick that goes
    # on to skip - a drifted deadline matters most on the ticks that are about to be long.
    $verdict = Find-CommandIn $LoopAst @('Get-ScheduleDriftVerdict')
    $claude = Find-CommandIn $LoopAst @('Start-Process', 'claude')
    Assert-True 'the drift check was found' ($null -ne $verdict)
    Assert-True 'the session launch was found' ($null -ne $claude)
    if (-not $verdict -or -not $claude) { return }
    Assert-True 'and the check comes first' ($verdict.Extent.StartOffset -lt $claude.Extent.StartOffset)
}

Write-Host ''
if ($script:Checks -lt 40) {
    Write-Host "FAIL: only $($script:Checks) assertion(s) ran - this file is meant to make more" -ForegroundColor Red
    $script:Failures++
}
if ($script:Failures -gt 0) {
    Write-Host "RESULT: FAILED - $($script:Failures) failure(s) in $($script:Ran) case(s)" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: OK - $($script:Ran) case(s), $($script:Checks) assertion(s)" -ForegroundColor Green
exit 0
