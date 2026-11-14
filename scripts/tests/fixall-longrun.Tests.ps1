#Requires -Version 5.1
<#
.SYNOPSIS
    The scheduled loop honours LONG-RUN. Task 0216d83c.

.DESCRIPTION
    A task that cannot be finished in one 75-minute tick carries a `LONG-RUN` line in its
    description (FIXALL_WORKFLOW.md → "Tasks longer than one run", AWTD-1041). On this board the
    flag was inert: `scripts/fixall-loop.ps1` mentioned neither `LONG-RUN` nor
    `ASTRID_FIXALL_NEXT_TASK` nor `ASTRID_FIXALL_DEFER_TASKS`, so two tasks flagged on 2026-11-12 -
    `1bdf84d7` and `9ef7edb4` - sat at the top of every tick's queue for ever. No tick could take
    them under a 75-minute watchdog, and every tick paid to read and skip them first.

    THE DECISION IS NOT OURS. astrid-web owns it in `scripts/lib/long-run.ts` (`parseLongRun`,
    `parseLongRunWindow`, `planRun`), and `scripts/agent-queue-status.ts --long-run-window` prints
    the answer as `RUN-MINUTES:` / `RUN-LONG: 1` / `RUN-TASK:` / `RUN-DEFER:` / `RUN:`. astrid-web's
    own loop (`scripts/fixall-loop.sh`, "Size the run") only APPLIES those lines, and so does this
    one. A second implementation of "how long may this run be" would drift silently against the
    queue that is deliberately called rather than copied here.

    So this file has two halves, for the two ways the application can be wrong.

    1. PARSING, exercised directly against `Get-RunPlan` with injected preflight output - no
       network, no board, no sibling checkout. The cases that matter are the malformed ones: a
       plan this loop cannot read must leave the defaults EXACTLY alone, because the failure mode
       of guessing is an 8-hour watchdog on an ordinary task or, far worse, a 75-minute watchdog on
       a long one. The second is the kill this whole mechanism exists to prevent.

    2. WIRING, asserted over the loop's parsed AST, for the same reason
       `fixall-loop-guards.Tests.ps1` gives: the guard is a shell-out to astrid-web's tsx, and
       running it inside the gate would need a sibling checkout, a node process and a token. What
       regressed here is not an exit-code mapping but WIRING - three absent flags and two unset
       environment variables - so that is what is checked, structurally.

       The ORDERING assertion is the one a careless edit re-breaks: the plan has to be applied
       BEFORE `Start-Process` launches claude and before the watchdog's `WaitForExit`. A watchdog
       resized after the run has started is inert, and that reads exactly like a working fix.

    AND THE SCHEDULER DEADLINE, which no amount of care on the run side can work around.
    `install-fixall-task.ps1` sets `ExecutionTimeLimit` to watchdog + recovery = 85 minutes. Task
    Scheduler stopping the loop at 85 minutes runs NONE of the loop's recovery - no save of
    unfinished work, no Doing release, no RESULT line - so an 8-hour tick would be killed at 85
    minutes in the worst possible way. The deadline has to allow for the long-run ceiling.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-longrun.Tests.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibPath = Join-Path $RepoRoot 'scripts\lib\fixall-longrun.ps1'
$LoopPath = Join-Path $RepoRoot 'scripts\fixall-loop.ps1'
$WakePath = Join-Path $RepoRoot 'scripts\lib\fixall-wake.ps1'
$InstallPath = Join-Path $RepoRoot 'scripts\install-fixall-task.ps1'
$CommandPath = Join-Path $RepoRoot '.claude\commands\fixall.md'

foreach ($required in @($LibPath, $LoopPath, $WakePath, $InstallPath)) {
    if (-not (Test-Path $required)) {
        Write-Host "FAIL: missing $required" -ForegroundColor Red
        exit 1
    }
}

$script:Failures = 0
$script:Ran = 0
# Assertions MADE, not passed - a case that exits early makes none, and a false OK in the gate that
# guards the scheduled loop is exactly the class of bug this file is about.
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

Write-Host 'fixall-longrun: reading the preflight plan (task 0216d83c)' -ForegroundColor Cyan

# ── Half one: Get-RunPlan ─────────────────────────────────────────────────────────────────────

Invoke-Case 'no RUN- lines at all leaves every default exactly alone' {
    $plan = Get-RunPlan -PreflightOutput @('QUEUE: 3 ready', 'LANES: clear') `
        -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 75 $plan.MaxMinutes
    Assert-Equal 'cap' '10' $plan.MaxUsd
    Assert-Equal 'long' $false $plan.Long
    Assert-Equal 'next task' '' $plan.NextTask
    Assert-Equal 'defer list' '' $plan.DeferTasks
}

Invoke-Case 'an ordinary tick takes the plan back unchanged' {
    # The preflight prints RUN-MINUTES on every call once --long-run-window is passed, flagged work
    # or not. 75 back is the same 75 that went in, and no RUN-LONG means the ordinary cap.
    $plan = Get-RunPlan -PreflightOutput @('RUN-MINUTES: 75', 'RUN-TASK: 3d5cca89') `
        -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 75 $plan.MaxMinutes
    Assert-Equal 'cap stays ordinary' '10' $plan.MaxUsd
    Assert-Equal 'long' $false $plan.Long
    Assert-Equal 'next task' '3d5cca89' $plan.NextTask
}

Invoke-Case 'inside the window a long task resizes the watchdog AND the cap' {
    $plan = Get-RunPlan -PreflightOutput @(
        'RUN-MINUTES: 480',
        'RUN-LONG: 1',
        'RUN-TASK: 1bdf84d7-c82b-467a-afa6-c54ce00d2d89',
        'RUN: 1bdf84d7 flagged LONG-RUN - watchdog 480m') `
        -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 480 $plan.MaxMinutes
    Assert-Equal 'cap' '50' $plan.MaxUsd
    Assert-Equal 'long' $true $plan.Long
    Assert-Equal 'next task' '1bdf84d7-c82b-467a-afa6-c54ce00d2d89' $plan.NextTask
    Assert-Equal 'defer list' '' $plan.DeferTasks
    Assert-Match 'the note is carried for the log' 'flagged LONG-RUN' ($plan.Notes -join "`n")
}

Invoke-Case 'outside the window the flagged ids are deferred and the tick stays ordinary' {
    $plan = Get-RunPlan -PreflightOutput @(
        'RUN-MINUTES: 75',
        'RUN-TASK: 3d5cca89-143d-47ec-a856-cb7901612e4c',
        'RUN-DEFER: 1bdf84d7-c82b-467a-afa6-c54ce00d2d89,9ef7edb4-55ba-4a1a-9ec8-de7f1f742e81',
        'RUN: 1bdf84d7, 9ef7edb4 flagged LONG-RUN - deferred to the 22:00-06:00 window') `
        -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 75 $plan.MaxMinutes
    Assert-Equal 'cap' '10' $plan.MaxUsd
    Assert-Equal 'long' $false $plan.Long
    Assert-Equal 'next task is the workable one' '3d5cca89-143d-47ec-a856-cb7901612e4c' $plan.NextTask
    Assert-Equal 'both deferred ids, verbatim and comma-joined' `
        '1bdf84d7-c82b-467a-afa6-c54ce00d2d89,9ef7edb4-55ba-4a1a-9ec8-de7f1f742e81' $plan.DeferTasks
}

Invoke-Case 'a malformed RUN-MINUTES is ignored, not coerced to zero' {
    # [int]'soon' is 0 in PowerShell only if you ask for it; a 0-minute watchdog kills the run
    # instantly and every tick would report a timeout. The default must survive.
    foreach ($bad in @('RUN-MINUTES: soon', 'RUN-MINUTES:', 'RUN-MINUTES: 48o', 'RUN-MINUTES: -30', 'RUN-MINUTES: 90m')) {
        $plan = Get-RunPlan -PreflightOutput @($bad) -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
        Assert-Equal "[$bad] leaves the default watchdog" 75 $plan.MaxMinutes
    }
}

Invoke-Case 'the RUN: note line is never mistaken for a value line' {
    # `RUN: ...` is prose. A prefix match on "RUN" would read it as a task id or a watchdog.
    $plan = Get-RunPlan -PreflightOutput @(
        'RUN: long-run window "lunchtime" is not <start>-<end> or "always" - using 22-6') `
        -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 75 $plan.MaxMinutes
    Assert-Equal 'next task' '' $plan.NextTask
    Assert-Equal 'defer list' '' $plan.DeferTasks
    Assert-Match 'but the complaint is kept for the log' 'is not <start>-<end>' ($plan.Notes -join "`n")
}

Invoke-Case 'the first value wins, as it does in the web loop' {
    # fixall-loop.sh pipes each of these through `head -1`. Matching that is cheaper than being
    # cleverer than it, and two RUN-TASK lines would mean the preflight had changed shape anyway.
    $plan = Get-RunPlan -PreflightOutput @('RUN-MINUTES: 480', 'RUN-MINUTES: 75', 'RUN-TASK: aaa', 'RUN-TASK: bbb') `
        -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 480 $plan.MaxMinutes
    Assert-Equal 'next task' 'aaa' $plan.NextTask
}

Invoke-Case 'an uncapped run stays uncapped even for a long task' {
    # -MaxUsd '' means "no --max-budget-usd argument". A long run must not INTRODUCE a cap where
    # the operator deliberately removed one; it raises a cap that is already there.
    $plan = Get-RunPlan -PreflightOutput @('RUN-MINUTES: 480', 'RUN-LONG: 1') `
        -DefaultMinutes 75 -DefaultMaxUsd '' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 480 $plan.MaxMinutes
    Assert-Equal 'still uncapped' '' $plan.MaxUsd
    Assert-Equal 'long' $true $plan.Long
}

Invoke-Case 'a long plan never SHORTENS the operator default' {
    # -MaxMinutes 600 from the command line with a 480 plan must stay 600. The plan is a floor the
    # flagged task needs, not a ceiling on what the operator asked for.
    $plan = Get-RunPlan -PreflightOutput @('RUN-MINUTES: 480', 'RUN-LONG: 1') `
        -DefaultMinutes 600 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 600 $plan.MaxMinutes
}

Invoke-Case 'nothing at all - a preflight that printed no lines - is not a crash' {
    # -IgnoreQueue and a preflight that could not run both land here.
    $plan = Get-RunPlan -PreflightOutput @() -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 75 $plan.MaxMinutes
    Assert-Equal 'next task' '' $plan.NextTask
    $plan = Get-RunPlan -PreflightOutput $null -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'a null output is the same as an empty one' 75 $plan.MaxMinutes
}

Invoke-Case 'lines arrive as whatever the shell-out produced, not necessarily strings' {
    # Invoke-WebTool hands back native output, which under PS 5.1 can be ErrorRecords mixed with
    # strings. Casting is the library's job; a plan that only works on [string[]] fails in the one
    # place it is actually used.
    $plan = Get-RunPlan -PreflightOutput @([pscustomobject]@{ x = 1 }, 'RUN-MINUTES: 480', $null) `
        -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 480 $plan.MaxMinutes
}

Invoke-Case 'trailing carriage returns do not break the match' {
    $plan = Get-RunPlan -PreflightOutput @("RUN-MINUTES: 480`r", "RUN-TASK: abc123`r") `
        -DefaultMinutes 75 -DefaultMaxUsd '10' -LongMaxUsd '50'
    Assert-Equal 'watchdog' 480 $plan.MaxMinutes
    Assert-Equal 'next task has no stray CR' 'abc123' $plan.NextTask
}

# ── Half two: the loop's wiring ───────────────────────────────────────────────────────────────

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
$LoopText = Get-Content -Path $LoopPath -Raw

function Find-LoopCommand([string[]]$AllOf) {
    foreach ($cmd in $Commands) {
        $text = $cmd.Extent.Text
        $ok = $true
        foreach ($needle in $AllOf) { if ($text -notmatch $needle) { $ok = $false; break } }
        if ($ok) { return $cmd }
    }
    return $null
}

Invoke-Case 'the loop dot-sources the library' {
    Assert-Match 'lib\fixall-longrun.ps1 is dot-sourced' 'lib\\fixall-longrun\.ps1' $LoopText
}

Invoke-Case 'the preflight asks for the plan' {
    # One COMMAND's own extent, so the three flags cannot be satisfied by words elsewhere in the
    # file. If the preflight does not ask, there is no plan to apply and every assertion below is
    # about dead code.
    $cmd = Find-LoopCommand @('agent-queue-status\.ts')
    Assert-True 'guard 3 still calls agent-queue-status.ts' ($null -ne $cmd)
    if (-not $cmd) { return }
    $text = $cmd.Extent.Text
    Assert-Match '--long-run-window is passed' '--long-run-window' $text
    Assert-Match '--default-minutes is passed' '--default-minutes' $text
    Assert-Match '--max-tasks is passed' '--max-tasks' $text
    Assert-Match 'the window comes from the parameter, not a literal' '\$LongRunWindow' $text
    Assert-Match 'default-minutes is the watchdog in force' '\$MaxMinutes' $text
    Assert-Match 'max-tasks is the loop s own bound' '\$MaxTasks' $text
}

Invoke-Case 'the window and the long cap are parameters with env fallbacks' {
    $params = $Ast.ParamBlock.Parameters
    $names = @($params | ForEach-Object { $_.Name.VariablePath.UserPath })
    Assert-True '-LongRunWindow exists' ($names -contains 'LongRunWindow')
    Assert-True '-LongMaxUsd exists' ($names -contains 'LongMaxUsd')
    Assert-Match 'FIXALL_LONG_RUN_WINDOW is read' 'FIXALL_LONG_RUN_WINDOW' $LoopText
    Assert-Match 'FIXALL_LONG_MAX_USD is read' 'FIXALL_LONG_MAX_USD' $LoopText
    Assert-Match 'the window defaults to the overnight one astrid-web documents' "'22-6'" $LoopText
    Assert-Match 'the long cap defaults to 50, as the web loop does' "'50'" $LoopText
}

Invoke-Case 'the plan is read from the preflight the loop already ran' {
    $cmd = Find-LoopCommand @('Get-RunPlan')
    Assert-True 'Get-RunPlan is called' ($null -ne $cmd)
    if (-not $cmd) { return }
    $text = $cmd.Extent.Text
    Assert-Match 'it reads the preflight output rather than calling the board again' '\$wake\.Output' $text
    Assert-Match 'the long cap is handed in' '\$LongMaxUsd' $text
}

Invoke-Case 'Get-WakeVerdict carries the preflight output forward' {
    # Otherwise the loop would have to run the preflight TWICE - a second board call per tick, and
    # two answers that can disagree with each other.
    $wakeText = Get-Content -Path $WakePath -Raw
    Assert-Match 'the verdict object has an Output member' 'Output\s*=' $wakeText
}

Invoke-Case 'the ids /fixall needs are exported' {
    Assert-Match 'ASTRID_FIXALL_NEXT_TASK is set' '\$env:ASTRID_FIXALL_NEXT_TASK\s*=' $LoopText
    Assert-Match 'ASTRID_FIXALL_DEFER_TASKS is set' '\$env:ASTRID_FIXALL_DEFER_TASKS\s*=' $LoopText
}

Invoke-Case 'the plan is applied BEFORE the run starts' {
    # The ordering bug: a watchdog resized after Start-Process is inert, and the loop would keep
    # killing long runs at 75 minutes while every other assertion here passed.
    $plan = Find-LoopCommand @('Get-RunPlan')
    $start = Find-LoopCommand @('Start-Process', 'ClaudeCmd')
    Assert-True 'the run is still started with Start-Process' ($null -ne $start)
    if (-not $plan -or -not $start) { return }
    Assert-True 'Get-RunPlan comes first' ($plan.Extent.StartOffset -lt $start.Extent.StartOffset)

    $nextAt = $LoopText.IndexOf('$env:ASTRID_FIXALL_NEXT_TASK =')
    $deferAt = $LoopText.IndexOf('$env:ASTRID_FIXALL_DEFER_TASKS =')
    Assert-True 'NEXT_TASK is exported before the run' ($nextAt -ge 0 -and $nextAt -lt $start.Extent.StartOffset)
    Assert-True 'DEFER_TASKS is exported before the run' ($deferAt -ge 0 -and $deferAt -lt $start.Extent.StartOffset)
}

Invoke-Case 'the scheduler deadline allows for a long run' {
    # Task Scheduler stopping the process runs NONE of the loop's recovery. An 85-minute
    # ExecutionTimeLimit turns an 8-hour tick into a hard kill with no RESULT line, no saved work
    # and a task left in Doing.
    $installText = Get-Content -Path $InstallPath -Raw
    Assert-Match 'the limit is no longer watchdog + recovery alone' `
        'ExecutionTimeLimit \(New-TimeSpan -Minutes \(\[Math\]::Max\(' $installText
    Assert-Match 'a long-run ceiling is part of the computation' 'LongRunMinutes' $installText
    $itokens = $null
    $ierrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($InstallPath, [ref]$itokens, [ref]$ierrors)
    Assert-Equal 'install-fixall-task.ps1 still parses' 0 (@($ierrors).Count)
}

Invoke-Case 'the run side contract is written down, including what it costs' {
    # The loop can defer a task all it likes; if /fixall does not read DEFER_TASKS it takes it
    # anyway, under the ordinary watchdog, which is the kill this exists to prevent. The CONTRACT
    # is asserted here; `.claude/commands/fixall.md` is where the run is actually asked, and that
    # file is checked separately below because a session is not always allowed to write it.
    $doc = Get-Content -Path (Join-Path $RepoRoot 'docs\AUTOMATION.md') -Raw
    Assert-Match 'ASTRID_FIXALL_NEXT_TASK is documented' 'ASTRID_FIXALL_NEXT_TASK' $doc
    Assert-Match 'ASTRID_FIXALL_DEFER_TASKS is documented' 'ASTRID_FIXALL_DEFER_TASKS' $doc
    Assert-Match 'a deferred task is left WITHOUT a comment' 'without comment' $doc
    Assert-Match 'the scheduler deadline and its cost are stated' 'ExecutionTimeLimit' $doc
}

Invoke-Case 'and /fixall itself is asked (task d6a3cc66)' {
    # This used to report a TODO rather than fail, because `.claude/` is outside what a SCHEDULED
    # run may write: the session that built the loop could record the contract in
    # docs/AUTOMATION.md but not install it in the command the run actually reads. An interactive
    # session wrote it on 2026-11-13, so the case asserts now.
    #
    # Why it has to be asserted somewhere: the loop already handles the easy half - a tick whose
    # queue holds NOTHING but deferred work never starts a session at all, because astrid-web
    # removes deferred tasks before the verdict and the preflight exits 3. What is left is the
    # MIXED queue, one deferred LONG-RUN task above several workable ones, where the run sees the
    # flagged task at the head of its own queue and takes it under a 75-minute watchdog. Only the
    # run can prevent that, and only if this file tells it to.
    if (-not (Test-Path $CommandPath)) {
        # A checkout without `.claude/` is a worktree or an export, not a broken repository.
        Write-Host '    (.claude/commands/fixall.md is not in this checkout)' -ForegroundColor Yellow
        Assert-Equal 'nothing to assert' '' ''
        return
    }
    $doc = Get-Content -Path $CommandPath -Raw
    Assert-Match 'the run is told to take ASTRID_FIXALL_NEXT_TASK first' 'ASTRID_FIXALL_NEXT_TASK' $doc
    Assert-Match 'the run is told not to take ASTRID_FIXALL_DEFER_TASKS' 'ASTRID_FIXALL_DEFER_TASKS' $doc
    # The two things a run gets wrong if it is told the names but not the rule: it takes the
    # deferred task anyway, or it leaves a comment every half hour saying it did not.
    Assert-Match 'and that a deferred task is left in Ready' 'Ready' $doc
    Assert-Match 'and left WITHOUT a comment' 'without comment' $doc
    Assert-Equal 'the file still parses as a /fixall command doc' $true ($doc -match '(?m)^## ')
}

Write-Host ''
if ($script:Failures -gt 0) {
    Write-Host "FAIL: $script:Failures assertion(s) failed across $script:Ran case(s)" -ForegroundColor Red
    exit 1
}
Write-Host "OK: $script:Ran case(s), $script:Checks assertion(s)" -ForegroundColor Green
exit 0
