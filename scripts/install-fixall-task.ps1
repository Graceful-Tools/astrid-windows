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
      * MultipleInstances = IgnoreNew, plus an execution time limit a little above the loop's own
        watchdog. One wedged run must not stack copies, and must not be able to outlive its own
        watchdog either.

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
    [string]$TaskName = 'Astrid fixall (Windows board)'
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
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$LoopScript`"" `
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
    -ExecutionTimeLimit (New-TimeSpan -Minutes 55) `
    -RestartCount 0

$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
    -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal `
    -Description "Works the Astrid Windows To-do board's Ready lane every 30 minutes through claude -p /fixall. See docs/AUTOMATION.md." `
    -Force | Out-Null

$info = Get-ScheduledTaskInfo -TaskName $TaskName
Write-Host "Registered '$TaskName' for $env:USERNAME."
Write-Host "  runs:     powershell -NoProfile -ExecutionPolicy Bypass -File $LoopScript"
Write-Host "  schedule: :10 and :40, every hour"
Write-Host "  next run: $($info.NextRunTime)"
Write-Host "  log:      $LogFile"
Write-Host ''
Write-Host 'Prove it before trusting it:'
Write-Host '  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1 -RunNow'
Write-Host '  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1 -Status'
