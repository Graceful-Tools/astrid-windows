#Requires -Version 5.1
<#
.SYNOPSIS
    What this checkout's scheduled entry should look like, and whether the registered one does.
    Task 18ba8c2e.

.DESCRIPTION
    THE FAILURE THIS EXISTS FOR, and it is two failures one inside the other.

    The outer one is arithmetic. `install-fixall-task.ps1` once set the scheduler's
    `ExecutionTimeLimit` to a literal 55 minutes while `fixall-loop.ps1` defaulted its own watchdog
    to 75, so Task Scheduler killed every long tick twenty minutes BEFORE the watchdog meant to
    catch it - and a scheduler kill runs none of the loop's recovery: no WIP commit, no `Doing`
    release, no wake acknowledgement, no comment on the task it had taken. Two independently
    written numbers in two files is all it took.

    The inner one is why that was still happening seventeen days after it was fixed. A SCHEDULED
    TASK IS REGISTERED, NOT READ FROM THE REPO. Editing the installer changes nothing on the
    machine until somebody re-runs it, and until this library nothing anywhere compared the two. On
    2026-11-14 the entry on this machine still read `PT55M` with no `-MaxMinutes` at all, while the
    installer in the same checkout would by then compute `max(75, 480) + 10 = 490` - task
    `0216d83c` having meanwhile landed LONG-RUN support, which gave the gap teeth it did not have
    when it was written: a tick astrid-web's preflight legitimately grants eight hours, stopped at
    fifty-five minutes.

    SO THIS FILE DOES TWO THINGS. It is the ONE place the deadline and the loop's argument string
    are computed - the installer builds both from here rather than inline - and it can read the
    registered entry back and say whether it still matches. The loop asks every tick and prints one
    line when it does not.

    A WARNING AND NEVER A STOP. A drifted registration is not a reason to take the loop out: the
    tick runs perfectly well, it is only bounded wrongly, and a guard that stopped on it would turn
    a cosmetic gap into a machine that went quiet. That is the opposite call to the one
    fixall-disk.ps1 makes about a full disk, and deliberately: there, the build genuinely cannot
    succeed.

    AND AN UNREADABLE REGISTRATION SAYS NOTHING AT ALL. A loop started by hand with `-Force` has no
    registration to compare against, and a machine where `Get-ScheduledTask` is unavailable has no
    reading to report. Same call guard 1b makes about an unreadable pause state and guard 1c about
    an unreadable disk reading - except that here there is not even a tick to hold up, so silence
    is the whole response. A warning printed every half hour about a reading that could not be
    taken is how a warning stops being read.

    WHY IT IS A LIBRARY. Same reason as scripts/lib/fixall-disk.ps1 and scripts/lib/fixall-wake.ps1:
    neither caller can run inside the gate - the loop takes a machine-wide lock and starts a Claude
    session, and the installer would re-register the real entry on the machine running the tests -
    so the decision lives here and is tested by value, while the WIRING in both is asserted over the
    parsed AST. See scripts/tests/fixall-schedule.Tests.ps1.
#>

# THE DEFAULTS, in one place. install-fixall-task.ps1 still names them in its own `param` block, so
# that each stays overridable with its own ValidateRange and so `-Status` keeps working without
# this file; a case in fixall-schedule.Tests.ps1 asserts the two sets are the same numbers, which is
# what keeps them from becoming 55 and 75 again.
$script:FixallScheduleDefaults = @{
    # The ONE watchdog. The loop is HANDED this as -MaxMinutes and the deadline below is computed
    # from it, so a deadline derived from one number while the loop obeys another cannot recur.
    Watchdog = 75
    # How long the loop gets, on top of its watchdog, to save unfinished work, release its Doing
    # claims, acknowledge its wake keys and comment on the task before the scheduler stops it.
    Recovery = 10
    # The ceiling a LONG-RUN tick may reach (task 0216d83c). The loop sizes its own watchdog per
    # tick from astrid-web's plan at RUN TIME, so the scheduler cannot know on any given tick
    # whether this is a 75-minute run or an 8-hour one; the deadline has to allow for the longest
    # one that is possible, not the one that is usual.
    LongRun  = 480
    TaskName = 'Astrid fixall (Windows board)'
}

<#
.SYNOPSIS
    One of the shared defaults, by name: Watchdog, Recovery, LongRun or TaskName.
#>
function Get-FixallScheduleDefault {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][ValidateSet('Watchdog', 'Recovery', 'LongRun', 'TaskName')][string]$Name)

    return $script:FixallScheduleDefaults[$Name]
}

<#
.SYNOPSIS
    The scheduler's execution time limit, in minutes, for a tick that may legitimately be long.
#>
function Get-FixallScheduleDeadlineMinutes {
    [CmdletBinding()]
    param(
        [int]$WatchdogMinutes = $script:FixallScheduleDefaults.Watchdog,
        [int]$RecoveryMinutes = $script:FixallScheduleDefaults.Recovery,
        [int]$LongRunMinutes = $script:FixallScheduleDefaults.LongRun
    )

    # The MAXIMUM and not the sum: LongRun is a ceiling the watchdog is raised TO on the ticks that
    # earn it, not an extra allowance on top of an ordinary one.
    return [Math]::Max($WatchdogMinutes, $LongRunMinutes) + $RecoveryMinutes
}

<#
.SYNOPSIS
    The argument string Task Scheduler should start the loop with.

.DESCRIPTION
    THE WATCHDOG IS FORWARDED EXPLICITLY, never left to the loop's own default - that is the whole
    point of this function existing rather than a literal in the installer. The path is quoted
    because a checkout under a user profile with a space in the name is ordinary.
#>
function Get-FixallScheduleArgument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$LoopScript,
        [int]$WatchdogMinutes = $script:FixallScheduleDefaults.Watchdog
    )

    return "-NoProfile -ExecutionPolicy Bypass -File `"$LoopScript`" -MaxMinutes $WatchdogMinutes"
}

<#
.SYNOPSIS
    The registered entry, flattened to the two things worth comparing.

.OUTPUTS
    @{ Found = [bool]; Argument = [string]; DeadlineMinutes = [int] }

    DeadlineMinutes is -1 when the entry sets NO limit, which is a real and different state from
    "could not read it": an unbounded task means a wedged launcher is stopped by nothing outside
    itself. Found is $false for both "not registered" and "could not ask", because the caller's
    answer to each is the same - say nothing.
#>
function Read-FixallScheduleRegistration {
    [CmdletBinding()]
    param([string]$TaskName = $script:FixallScheduleDefaults.TaskName)

    $absent = [pscustomobject]@{ Found = $false; Argument = ''; DeadlineMinutes = -1 }

    try {
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        if (-not $task) { return $absent }

        # The first Exec action: the installer registers exactly one, and a hand-edited entry with
        # several is drift this cannot describe usefully anyway.
        $argument = @($task.Actions | ForEach-Object { $_.Arguments } | Where-Object { $_ }) |
            Select-Object -First 1
        if ($null -eq $argument) { $argument = '' }

        # ExecutionTimeLimit comes back as an ISO 8601 duration string (PT55M, PT8H10M), empty when
        # the entry sets no limit. [System.Xml.XmlConvert] parses it; TimeSpan::Parse does not.
        $limit = $task.Settings.ExecutionTimeLimit
        $minutes = -1
        if ($limit) {
            $minutes = [int][Math]::Round(([System.Xml.XmlConvert]::ToTimeSpan($limit)).TotalMinutes)
        }

        return [pscustomobject]@{ Found = $true; Argument = [string]$argument; DeadlineMinutes = $minutes }
    }
    catch { return $absent }
}

# Two spellings of one path are the same path. Task Scheduler's spelling and an interactive shell's
# differ in case and in how they got there, and a warning that fires every half hour on a difference
# of case is a warning nobody reads - the same reasoning as the worker scope in fixall-sweep.ps1.
function Get-ComparablePath([string]$Path) {
    if (-not $Path) { return '' }
    try { return ([System.IO.Path]::GetFullPath($Path)).TrimEnd('\', '/').ToLowerInvariant() }
    catch { return $Path.TrimEnd('\', '/').ToLowerInvariant() }
}

<#
.SYNOPSIS
    Does the registered entry still match what this checkout's installer would produce?

.OUTPUTS
    @{ InSync = [bool]; Level = 'ok'|'stale'|'unknown'; Reason = [string] }

    InSync is for the tests; nothing acts on it but the warning. Level separates "they disagree"
    from "there was nothing to compare". Reason is ONE line, pasted verbatim into the log beside the
    other preamble warnings, and it always names the command that fixes it - a warning with no way
    out is one that gets ignored on its second appearance.
#>
function Get-ScheduleDriftVerdict {
    [CmdletBinding()]
    param(
        $Registration,
        [Parameter(Mandatory = $true)][string]$LoopScript,
        [int]$WatchdogMinutes = $script:FixallScheduleDefaults.Watchdog,
        [int]$DeadlineMinutes = (Get-FixallScheduleDeadlineMinutes)
    )

    if ($null -eq $Registration -or -not $Registration.Found) {
        return [pscustomobject]@{ InSync = $true; Level = 'unknown'; Reason = '' }
    }

    $argument = [string]$Registration.Argument
    $problems = @()

    # ── The deadline, which is the one that has actually bitten ──────────────────────────────
    $registeredDeadline = [int]$Registration.DeadlineMinutes
    if ($registeredDeadline -lt 0) {
        $problems += "it has no execution time limit at all, so a wedged launcher is never stopped (this checkout computes ${DeadlineMinutes}m)"
    }
    elseif ($registeredDeadline -ne $DeadlineMinutes) {
        $problems += "Task Scheduler stops it at ${registeredDeadline}m, not the ${DeadlineMinutes}m this checkout computes"
    }

    # ── The watchdog the loop is actually handed ─────────────────────────────────────────────
    if ($argument -match '-MaxMinutes\s+(\d+)') {
        $registeredWatchdog = [int]$Matches[1]
        if ($registeredWatchdog -ne $WatchdogMinutes) {
            $problems += "it starts the loop with -MaxMinutes ${registeredWatchdog}, but the deadline was computed from ${WatchdogMinutes}"
        }
    }
    else {
        $problems += "it starts the loop with no -MaxMinutes, so the watchdog falls back to the loop's own default instead of the ${WatchdogMinutes} the deadline was computed from"
    }

    # ── And which checkout it starts ─────────────────────────────────────────────────────────
    # install-fixall-task.ps1's header already says "re-run it after moving the checkout"; nothing
    # has ever checked that either, and a moved checkout leaves the scheduler starting a script that
    # may no longer exist.
    if ($argument -match '-File\s+"([^"]+)"') { $registeredScript = $Matches[1] }
    elseif ($argument -match '-File\s+(\S+)') { $registeredScript = $Matches[1] }
    else { $registeredScript = '' }

    if ((Get-ComparablePath $registeredScript) -ne (Get-ComparablePath $LoopScript)) {
        $problems += "it starts ${registeredScript}, not this checkout's ${LoopScript}"
    }

    if ($problems.Count -eq 0) {
        return [pscustomobject]@{ InSync = $true; Level = 'ok'; Reason = '' }
    }

    $fix = 'powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-fixall-task.ps1'
    return [pscustomobject]@{
        InSync = $false
        Level  = 'stale'
        Reason = "the registered scheduled task does not match this checkout - $($problems -join '; '); re-register it with ``$fix``"
    }
}
