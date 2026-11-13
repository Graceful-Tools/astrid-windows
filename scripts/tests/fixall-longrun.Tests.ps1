#Requires -Version 5.1
<#
.SYNOPSIS
    The Windows loop honours the LONG-RUN flag, so a flagged epic stops being re-triaged every
    tick. Task 0216d83c (and its duplicate f7eae119).

.DESCRIPTION
    THE BUG. `scripts/fixall-loop.ps1` mentioned neither `LONG-RUN` nor
    `ASTRID_FIXALL_DEFER_TASKS`, so the flag that FIXALL_WORKFLOW.md -> "Tasks longer than one run"
    (AWTD-1041) defines was inert on this board. On 2026-11-12 Jon flagged the two epics at the top
    of the Windows queue - `1bdf84d7` and `9ef7edb4` - precisely because three separate runs had
    each read them, concluded neither fits a 75-minute watchdog however it is split, commented, and
    left them in Ready for the next run to repeat. Flagging is the documented answer to that churn.
    Here it changed nothing: both came back at the top of every tick and every tick paid to
    re-triage them.

    THE DECISION IS NOT OURS. astrid-web owns it - `scripts/lib/long-run.ts` decides and
    `scripts/agent-queue-status.ts` prints the plan as `RUN-MINUTES:` / `RUN-LONG: 1` /
    `RUN-TASK:` / `RUN-DEFER:` / `RUN:` when it is given `--long-run-window`. `fixall-loop.sh` only
    APPLIES it, and so does this loop. A second implementation of "how long may this run be" drifts,
    and the drift is silent - which is the whole reason the queue is called here rather than copied
    (docs/AUTOMATION.md).

    SO WHAT IS TESTED HERE IS APPLICATION, in three layers:

    1. THE READ, `Get-RunPlan` in scripts/lib/fixall-longrun.ps1: turning the preflight's `RUN-*`
       lines into a watchdog, a cap, a next-task id and a defer list. A library for the reason
       scripts/lib/fixall-wake.ps1 is one - the loop takes a machine-wide lock and starts a Claude
       session, so it cannot run inside the gate.

    2. THE WIRING in the loop, asserted over the parsed AST, as fixall-wake.Tests.ps1 does: the
       flags on guard 3's preflight, and the four things the plan sets. A missing flag is exactly
       the kind of regression that leaves the flag inert again while every test still passes.

    3. THE SCHEDULER, in scripts/install-fixall-task.ps1. This is the half no run side can work
       around: `ExecutionTimeLimit` was `watchdog + recovery` = 85 minutes, so Task Scheduler would
       have killed an 8-hour tick at 85 minutes - and a scheduler kill runs NONE of the loop's
       recovery (no WIP commit, no Doing release, no wake acknowledgement). The deadline has to
       clear the long-run ceiling, not the ordinary watchdog.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-longrun.Tests.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibPath = Join-Path $RepoRoot 'scripts\lib\fixall-longrun.ps1'
$LoopPath = Join-Path $RepoRoot 'scripts\fixall-loop.ps1'
$InstallPath = Join-Path $RepoRoot 'scripts\install-fixall-task.ps1'
$CommandPath = Join-Path $RepoRoot '.claude\commands\fixall.md'

foreach ($required in @($LibPath, $LoopPath, $InstallPath, $CommandPath)) {
    if (-not (Test-Path $required)) {
        Write-Host "FAIL: missing $required" -ForegroundColor Red
        exit 1
    }
}

. $LibPath

$script:Failures = 0
$script:Ran = 0
# Assertions MADE, not passed: a case that returns early makes none, and a vacuous OK in the gate
# that guards the scheduled loop is the exact shape of bug this file is about.
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
function Assert-False([string]$What, $Actual) { Assert-Equal $What 'False' ([bool]$Actual) }

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

Write-Host ''
Write-Host 'the Windows loop honours LONG-RUN (task 0216d83c)' -ForegroundColor Cyan

# ── Reading the plan ─────────────────────────────────────────────────────────────────────────
# The preflight's own lines, verbatim, as `agent-queue-status.ts` prints them.

Invoke-Case 'a preflight that was never asked for a plan leaves every default alone' {
    # The iOS loop's calls are unchanged for this reason: --long-run-window is opt-in, and a call
    # without it prints no RUN- lines at all.
    $plan = Get-RunPlan -Output @('QUEUE: 2 ready', 'KEYS: []') -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'the ordinary watchdog stands' 75 $plan.MaxMinutes
    Assert-Equal 'and the ordinary cap' '10' $plan.MaxUsd
    Assert-False 'this is not a long run' $plan.Long
    Assert-Equal 'nothing is pinned as the next task' '' $plan.NextTask
    Assert-Equal 'and nothing is deferred' '' $plan.DeferTasks
    Assert-Equal 'so there is nothing to say about it' '' $plan.Note
}

Invoke-Case 'inside the window the watchdog and the cap are both raised' {
    $plan = Get-RunPlan -Output @(
        'RUN-MINUTES: 480',
        'RUN-LONG: 1',
        'RUN-TASK: 1bdf84d7-c82b-467a-afa6-c54ce00d2d89',
        'RUN: AWTD-9 flagged LONG-RUN - watchdog 480m'
    ) -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'the watchdog is the task s own length' 480 $plan.MaxMinutes
    Assert-True 'this run was sized for a long task' $plan.Long
    # The budget is the OTHER bound, and the one that actually stops a run which stays busy. An
    # 8-hour run under a $10 cap is a 75-minute run wearing a longer watchdog.
    Assert-Equal 'and the cap is raised with it' '50' $plan.MaxUsd
    Assert-Equal 'the flagged task is taken first' '1bdf84d7-c82b-467a-afa6-c54ce00d2d89' $plan.NextTask
    Assert-Equal 'nothing is deferred inside the window' '' $plan.DeferTasks
    Assert-Match 'and the log says why this tick is long' 'LONG-RUN' $plan.Note
}

Invoke-Case 'outside the window the flagged ids are deferred and the tick stays ordinary' {
    $plan = Get-RunPlan -Output @(
        'RUN-MINUTES: 75',
        'RUN-TASK: 3d5cca89',
        'RUN-DEFER: 1bdf84d7,9ef7edb4',
        'RUN: 1bdf84d7, 9ef7edb4 flagged LONG-RUN - deferred to the 22:00-06:00 window'
    ) -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'the ordinary watchdog' 75 $plan.MaxMinutes
    Assert-False 'and not a long run' $plan.Long
    # THE bug. Without this the two epics come back at the top of every tick for ever.
    Assert-Equal 'both flagged ids are handed to the run as deferred' '1bdf84d7,9ef7edb4' $plan.DeferTasks
    Assert-Equal 'the ordinary cap stands' '10' $plan.MaxUsd
    Assert-Equal 'and the workable task is what the run takes first' '3d5cca89' $plan.NextTask
}

Invoke-Case 'a cap the operator turned off is NOT turned back on by a long run' {
    # FIXALL_MAX_USD='' is how a cap is disabled, and `[ -n "$MAX_USD" ]` is the web loop's own
    # guard on exactly this.
    $plan = Get-RunPlan -Output @('RUN-MINUTES: 480', 'RUN-LONG: 1') -DefaultMinutes 75 -DefaultMaxUsd '' -LongMaxUsd '50'
    Assert-Equal 'the watchdog is still raised' 480 $plan.MaxMinutes
    Assert-True 'and it is still a long run' $plan.Long
    Assert-Equal 'but no cap is introduced' '' $plan.MaxUsd
}

Invoke-Case 'a plan that cannot be read leaves the defaults alone rather than guessing' {
    # A malformed RUN-MINUTES is a shared script this loop could not parse - a version skew, or a
    # line that changed shape. Running at 75 minutes is the recoverable mistake.
    $plan = Get-RunPlan -Output @('RUN-MINUTES: whenever', 'RUN-LONG: yes') -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'the default watchdog' 75 $plan.MaxMinutes
    Assert-False 'and no long run was claimed' $plan.Long
    Assert-Equal 'so the ordinary cap stands' '10' $plan.MaxUsd
}

Invoke-Case 'a plan can only LENGTHEN a tick, never shorten one' {
    # planRun already returns max(default, longest), so no honest preflight produces this. A
    # watchdog shortened on another process's say-so would kill ordinary tasks mid-gate, and this
    # loop reads that value across a process boundary.
    $plan = Get-RunPlan -Output @('RUN-MINUTES: 5') -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'the floor is this loop s own watchdog' 75 $plan.MaxMinutes
}

Invoke-Case 'the RUN: note is not confused with the RUN- keys that share its prefix' {
    $plan = Get-RunPlan -Output @('RUN: nothing was flagged', 'RUN-MINUTES: 90') -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'the note is the note' 'nothing was flagged' $plan.Note
    Assert-Equal 'and the watchdog is the watchdog' 90 $plan.MaxMinutes
}

Invoke-Case 'the lines survive the shapes PowerShell actually hands back' {
    # Invoke-WebTool returns whatever the native command wrote, including ErrorRecords and padding;
    # a reader that insists on bare trimmed strings silently finds nothing.
    $plan = Get-RunPlan -Output @('  RUN-MINUTES: 480  ', '', $null, '  RUN-DEFER: aaa,bbb ') `
        -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'the watchdog was still read' 480 $plan.MaxMinutes
    Assert-Equal 'and the defer list is trimmed' 'aaa,bbb' $plan.DeferTasks
}

# ── The wiring in the loop ───────────────────────────────────────────────────────────────────

$tokens = $null
$errors = $null
$Ast = [System.Management.Automation.Language.Parser]::ParseFile($LoopPath, [ref]$tokens, [ref]$errors)
if ($errors -and $errors.Count -gt 0) {
    Write-Host "FAIL: scripts/fixall-loop.ps1 does not parse ($($errors.Count) error(s)):" -ForegroundColor Red
    $errors | Select-Object -First 5 | ForEach-Object { Write-Host "    $($_.Message)" -ForegroundColor Red }
    exit 1
}
$Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
$Assignments = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))
$LoopText = (Get-Content $LoopPath -Raw)

function Find-LoopCommand([string[]]$AllOf) {
    foreach ($cmd in $Commands) {
        $text = $cmd.Extent.Text
        $ok = $true
        foreach ($needle in $AllOf) { if ($text -notmatch $needle) { $ok = $false; break } }
        if ($ok) { return $cmd }
    }
    return $null
}

function Find-Assignment([string]$Pattern) {
    return @($Assignments | Where-Object { $_.Left.Extent.Text -match $Pattern } | Select-Object -First 1)
}

$Preflight = Find-LoopCommand @('agent-queue-status\.ts', '--long-run-window')
$PlanCall = Find-LoopCommand @('Get-RunPlan')
$Start = Find-LoopCommand @('Start-Process')

Invoke-Case 'guard 3 asks the shared preflight for the plan as well as the verdict' {
    Assert-True 'the preflight asks for a long-run plan' ($null -ne $Preflight)
    if (-not $Preflight) { return }
    $text = $Preflight.Extent.Text
    # The three inputs planRun needs. Without --default-minutes it would size against its own 75,
    # and without --max-tasks it would plan for a queue slice this loop does not take.
    Assert-Match 'with this loop s own watchdog as the default' '--default-minutes' $text
    Assert-Match 'and this loop s own task cap' '--max-tasks' $text
}

Invoke-Case 'the window is configurable and defaults to astrid-web s own' {
    # 22-6 is DEFAULT_LONG_RUN_WINDOW in astrid-web/scripts/lib/long-run.ts. Named here so the two
    # cannot drift apart silently; the parse itself stays over there.
    Assert-Match 'FIXALL_LONG_RUN_WINDOW is read' 'FIXALL_LONG_RUN_WINDOW' $LoopText
    Assert-Match 'and defaults to the overnight window' "'22-6'" $LoopText
    Assert-Match 'the long-run budget is configurable too' 'FIXALL_LONG_MAX_USD' $LoopText
}

Invoke-Case 'the plan sets the watchdog, the cap and the two task ids' {
    Assert-True 'the loop reads the plan' ($null -ne $PlanCall)
    Assert-True 'and Claude is still started' ($null -ne $Start)
    if (-not $PlanCall -or -not $Start -or -not $Preflight) { return }
    # Order is the point: the plan comes FROM the preflight and has to be applied before the run it
    # sizes. A Get-RunPlan after Start-Process sizes nothing.
    Assert-True 'the plan is read after the preflight' ($PlanCall.Extent.StartOffset -gt $Preflight.Extent.StartOffset)
    Assert-True 'and applied before the run it sizes' ($PlanCall.Extent.StartOffset -lt $Start.Extent.StartOffset)

    $watchdog = Find-Assignment '^\$MaxMinutes$'
    Assert-True 'the watchdog is reassigned from the plan' (
        @($watchdog).Count -eq 1 -and $watchdog[0].Right.Extent.Text -match 'plan')
    $cap = Find-Assignment '^\$MaxUsd$'
    Assert-True 'and so is the cap' (
        @($cap).Count -eq 1 -and $cap[0].Right.Extent.Text -match 'plan')

    # THE two variables the run side reads. ASTRID_FIXALL_NEXT_TASK makes a long task go first in
    # its window - the window is the only time it can run - and ASTRID_FIXALL_DEFER_TASKS is what
    # stops a flagged epic being re-triaged by every ordinary tick.
    $next = Find-Assignment 'ASTRID_FIXALL_NEXT_TASK'
    Assert-True 'the run is told which task to take first' (@($next).Count -eq 1)
    $defer = Find-Assignment 'ASTRID_FIXALL_DEFER_TASKS'
    Assert-True 'and which ids it may not take' (@($defer).Count -eq 1)
    if (@($defer).Count -eq 1) {
        Assert-True 'from the plan, not from a second board read' ($defer[0].Right.Extent.Text -match 'plan')
    }
}

Invoke-Case 'the run side is documented, or the deferred ids are ignored' {
    # The loop can export both variables all it likes; if .claude/commands/fixall.md never mentions
    # them the session takes the flagged task anyway and the 75-minute watchdog kills it. This file
    # IS the run side's contract - fixall.md's own "This file must exist on the branch you are
    # working" note is about exactly that.
    $command = (Get-Content $CommandPath -Raw)
    Assert-Match 'the run takes ASTRID_FIXALL_NEXT_TASK first' 'ASTRID_FIXALL_NEXT_TASK' $command
    Assert-Match 'and never takes a deferred id' 'ASTRID_FIXALL_DEFER_TASKS' $command
    Assert-Match 'the flag itself is named' 'LONG-RUN' $command
}

# ── The scheduler must not kill a long tick at 85 minutes ────────────────────────────────────

$installTokens = $null
$installErrors = $null
$InstallAst = [System.Management.Automation.Language.Parser]::ParseFile($InstallPath, [ref]$installTokens, [ref]$installErrors)
if ($installErrors -and $installErrors.Count -gt 0) {
    Write-Host 'FAIL: scripts/install-fixall-task.ps1 does not parse' -ForegroundColor Red
    exit 1
}

Invoke-Case 'the scheduler deadline clears the long-run ceiling, not just the watchdog' {
    # The half of this task that no run side can work around. ExecutionTimeLimit was
    # watchdog + recovery = 85 minutes; an 8-hour tick would have been killed by Task Scheduler at
    # 85, and a scheduler kill runs none of the loop's recovery - no WIP commit, no Doing release,
    # no wake acknowledgement. So the deadline is sized for the LONGEST tick that can start.
    $ceiling = @($InstallAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.ParameterAst] }, $true)) |
        Where-Object { $_.Name.VariablePath.UserPath -eq 'LongRunMaxMinutes' } | Select-Object -First 1
    Assert-True 'the ceiling is a parameter of its own' ($null -ne $ceiling)
    if ($ceiling) {
        Assert-Match 'validated, like the watchdog beside it' 'ValidateRange' $ceiling.Extent.Text
        # LONG_RUN_MAX_MINUTES in astrid-web/scripts/lib/long-run.ts. The two numbers are the same
        # number, so the one here says where the other lives.
        Assert-Match 'and defaulting to astrid-web s 8-hour maximum' '480' $ceiling.Extent.Text
    }
    $limit = @($InstallAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)) |
        Where-Object { $_.Extent.Text -match 'New-ScheduledTaskSettingsSet' } | Select-Object -First 1
    Assert-True 'the settings set is still built' ($null -ne $limit)
    if (-not $limit) { return }
    # Still derived, never a literal - that is what fixall-wake.Tests.ps1 won back - but derived
    # from the longest tick rather than the ordinary one.
    Assert-Match 'the deadline is computed from the ceiling' 'ExecutionTimeLimit[\s\S]{0,120}LongRunMaxMinutes' $limit.Extent.Text
    Assert-Match 'and still from the watchdog' 'ExecutionTimeLimit[\s\S]{0,120}Watchdog' $limit.Extent.Text
    Assert-Match 'with the recovery headroom still added' 'ExecutionTimeLimit[\s\S]{0,120}RecoveryMinutes' $limit.Extent.Text
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
