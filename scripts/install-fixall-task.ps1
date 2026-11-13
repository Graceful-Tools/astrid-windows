#Requires -Version 5.1
<#
.SYNOPSIS
    Install, remove or inspect the scheduled /fixall loop for the Astrid Windows board.

.DESCRIPTION
    The Windows half of what launchd does on the Mac (astrid-web scripts/launchd/install.sh). It
    registers scripts/fixall-loop.ps1 with Task Scheduler for the CURRENT USER, filling in this
    checkout's own path, so there is nothing to hand-edit and nothing to rot when the checkout
    moves. Re-run it after moving the checkout.

    NO ELEVATION. The task runs as the logged-on user at Limited run level, which is what the
    Claude CLI needs - it reads the user's own credentials and settings - and is why nothing here
    asks for administrator.

    THREE SETTINGS ARE LOAD-BEARING ON A LAPTOP, each of them a way the loop would otherwise go
    quiet while looking installed and healthy:

      * AllowStartIfOnBatteries and DontStopIfGoingOnBatteries. A task without these does not run
        unplugged, which on this machine is most of the time.
      * StartWhenAvailable. After sleep, a tick that was missed runs once on wake instead of being
        dropped until the next half hour.
      * MultipleInstances = IgnoreNew, plus an execution time limit DERIVED from the loop's own
        watchdog. One wedged run must not stack copies, and must not be able to outlive its own
        watchdog either - but neither may the scheduler kill a run BEFORE that watchdog fires.
        Until task 18ba8c2e it did: the limit was a literal 55 minutes against a 75-minute
        watchdog, so Task Scheduler killed every long tick twenty minutes early, and a scheduler
        kill runs none of the loop's recovery - no WIP commit, no Doing release, no wake
        acknowledgement, no comment on the task it had taken. Two independently-defaulted numbers
        is how those drifted apart, so there is now ONE watchdog: -WatchdogMinutes is forwarded to
        the loop explicitly AND is what the deadline is computed from, with -RecoveryMinutes of
        headroom on top for the loop to finish cleaning up after its own watchdog fires.

    The schedule is :10 and :40, chosen to interleave with the other loops working Astrid boards
    rather than to compete with them for the same quota: :00/:30 astrid-ios (claude), :20/:50
    astrid-web (claude), :07/:37 astrid-web (copilot), :05/:35 this repo's GitHub Actions copilot
    path.

.PARAMETER Uninstall
    Remove the task.

.PARAMETER Status
    Show whether it is registered, when it last ran, what it returned, and the tail of its log.

.PARAMETER RunNow
    Start one pass immediately through the scheduler, which is how to prove the registration
    itself works rather than just the script.

.PARAMETER WatchdogMinutes
    The ONE watchdog. Passed to the loop as -MaxMinutes and used to compute the scheduler's
    execution time limit, so the two cannot drift apart again. Must match what the loop would
    otherwise default to (75).

.PARAMETER RecoveryMinutes
    How long the loop is given, on top of its watchdog, to save unfinished work, release its Doing
    claims, acknowledge its wake keys and comment on the task before Task Scheduler stops it.

.PARAMETER LongRunMinutes
    The ceiling a tick may reach when it takes a task flagged LONG-RUN (task 0216d83c). The loop
    sizes its OWN watchdog per tick, from the plan astrid-web's preflight returns, so by the time
    anything knows this is an 8-hour run the scheduler's deadline is already fixed - which is why
    the deadline has to be computed for the longest tick that is possible rather than the one that
    is usual.

    THE COST, stated plainly: the scheduler's deadline is also the backstop against the LOOP ITSELF
    wedging, and that backstop now sits at the long-run ceiling for every tick, ordinary ones
    included. It is the lesser harm. The loop's own watchdog still kills an ordinary wedged RUN at
    75 minutes and recovers; what gets weaker is only the outer guard against the launcher hanging,
    which has never happened. What this replaces is certain: an 85-minute kill of a tick that was
    legitimately given eight hours, with no WIP commit, no Doing release and no comment. Set 0 to
    opt out and keep the tight deadline - a long task is then simply never started here.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1 -Status
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$Status,
    [switch]$RunNow,
    [string]$TaskName = 'Astrid fixall (Windows board)',

    # Ranged, because both numbers are load-bearing and neither failure is visible: a watchdog of
    # minutes kills every real run, and one of hours lets a wedged tick swallow a whole evening of
    # them (MultipleInstances = IgnoreNew).
    [ValidateRange(10, 240)][int]$WatchdogMinutes = 75,
    [ValidateRange(1, 60)][int]$RecoveryMinutes = 10,

    # The CEILING a LONG-RUN tick may reach, which the deadline below must also allow for (task
    # 0216d83c). The loop sizes its own watchdog from the preflight's plan at RUN TIME, so the
    # scheduler cannot know on any given tick whether this is a 75-minute run or an 8-hour one -
    # and a deadline set for the ordinary case kills the long one at 85 minutes, running NONE of
    # the loop's recovery. 480 is astrid-web's LONG_RUN_MAX_MINUTES; 0 disables long runs entirely,
    # which is what to set if this machine must never give a tick more than its watchdog.
    [ValidateRange(0, 480)][int]$LongRunMinutes = 480
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$LoopScript = Join-Path $RepoRoot 'scripts\fixall-loop.ps1'
$LogFile = Join-Path $env:LOCALAPPDATA 'Astrid\logs\fixall-windows.log'

if (-not (Test-Path $LoopScript)) { throw "no loop script at $LoopScript" }

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

if ($Status) {
    if (-not $existing) {
        Write-Host "Not registered. Install it with:`n  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1"
        exit 0
    }
    $info = Get-ScheduledTaskInfo -TaskName $TaskName
    Write-Host "Task:        $TaskName"
    Write-Host "State:       $($existing.State)"
    Write-Host "Runs:        $(($existing.Actions | ForEach-Object { $_.Execute + ' ' + $_.Arguments }) -join '; ')"
    Write-Host "Last run:    $($info.LastRunTime)  (result $($info.LastTaskResult))"
    Write-Host "Next run:    $($info.NextRunTime)"
    Write-Host "Log:         $LogFile"
    if (Test-Path $LogFile) {
        Write-Host '--- last 15 log lines ---'
        Get-Content -Path $LogFile -Tail 15 | ForEach-Object { Write-Host "  $_" }
    }
    exit 0
}

if ($Uninstall) {
    if (-not $existing) { Write-Host "Not registered; nothing to remove."; exit 0 }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Removed '$TaskName'. The log stays at $LogFile."
    exit 0
}

if ($RunNow) {
    if (-not $existing) { throw "not registered; install it first" }
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Started '$TaskName'. Watch it with:`n  Get-Content '$LogFile' -Tail 20 -Wait"
    exit 0
}

# ── Install ───────────────────────────────────────────────────────────────────────────────────
# The watchdog is FORWARDED, never left to the loop's own default: the deadline below is computed
# from this number, and a deadline derived from one number while the loop obeys another is the
# drift task 18ba8c2e fixed.
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$LoopScript`" -MaxMinutes $WatchdogMinutes" `
    -WorkingDirectory $RepoRoot

# One trigger, repeating every 30 minutes forever, rather than two daily triggers: the same two
# clock minutes every hour with half the configuration to get wrong.
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date -Hour 0 -Minute 10 -Second 0) `
    -RepetitionInterval (New-TimeSpan -Minutes 30)

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes ([Math]::Max($WatchdogMinutes, $LongRunMinutes) + $RecoveryMinutes)) `
    -RestartCount 0

$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
    -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal `
    -Description "Works the Astrid Windows To-do board's Ready lane every 30 minutes through claude -p /fixall. See docs/AUTOMATION.md." `
    -Force | Out-Null

$info = Get-ScheduledTaskInfo -TaskName $TaskName
Write-Host "Registered '$TaskName' for $env:USERNAME."
Write-Host "  runs:     powershell -NoProfile -ExecutionPolicy Bypass -File $LoopScript -MaxMinutes $WatchdogMinutes"
Write-Host "  schedule: :10 and :40, every hour"
Write-Host "  watchdog: ${WatchdogMinutes}m, with the scheduler stopping it at $([Math]::Max($WatchdogMinutes, $LongRunMinutes) + $RecoveryMinutes)m (${RecoveryMinutes}m to recover)"
Write-Host "  long run: up to ${LongRunMinutes}m for a task flagged LONG-RUN, inside its window - the loop sizes each tick"
Write-Host "  next run: $($info.NextRunTime)"
Write-Host "  log:      $LogFile"
Write-Host ''
Write-Host 'Prove it before trusting it:'
Write-Host '  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1 -RunNow'
Write-Host '  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1 -Status'
