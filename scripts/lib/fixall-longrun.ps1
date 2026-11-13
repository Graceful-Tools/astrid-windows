#Requires -Version 5.1
<#
.SYNOPSIS
    How long this tick may be, and which tasks it may take. Task 0216d83c.

.DESCRIPTION
    THE FLAG IS NOT DECIDED HERE. A task asks for a long run with a line of its own in its
    description - `LONG-RUN`, or `LONG-RUN: 3h` - and everything about what that means lives in
    astrid-web: `scripts/lib/long-run.ts` parses the line, picks the overnight window and plans the
    run, and `scripts/agent-queue-status.ts` prints the plan when it is given `--long-run-window`.
    `scripts/fixall-loop.sh` only APPLIES it, and so does this loop. A second implementation of
    "how long may this run be" drifts, and the drift is silent - the same reason the queue itself
    is called rather than copied here (docs/AUTOMATION.md).

    SO THIS FILE IS A READER, nothing more. The preflight prints five lines; this turns them into
    the four things a tick needs:

      RUN-MINUTES: 480    the watchdog for this tick
      RUN-LONG: 1         this tick was sized for a long task, so it gets the long budget too
      RUN-TASK: <id>      the task the run takes first
      RUN-DEFER: <ids>    the flagged ids this tick may NOT take
      RUN: <note>         one line for the log

    WHY THE DEFER LIST MATTERS MOST. Without it a flagged epic is deferred nowhere: it comes back
    at the top of every tick, and every tick pays to read and re-triage it. That is what happened
    to `1bdf84d7` and `9ef7edb4` from 2026-11-12 onwards, and flagging them - the documented answer
    to exactly that churn - changed nothing on this board because the loop had never heard of the
    flag.

    TWO PLACES THIS DELIBERATELY DIFFERS FROM A LITERAL PORT OF THE SHELL, both because a value is
    being read across a process boundary and a bad one is worse here than a missing one:

      * A plan may only LENGTHEN a tick. `planRun` already returns max(default, longest), so no
        honest preflight produces a shorter watchdog - but one that did would kill ordinary tasks
        mid-gate, and this loop has no way to tell a version skew from a decision.
      * A malformed value leaves the default alone rather than being guessed at.

    A cap the operator turned off is NOT turned back on by a long run, which is the shell's own
    rule (`[ -n "$MAX_USD" ]`).

    WHY A LIBRARY. The same reason scripts/lib/fixall-wake.ps1 is one: the loop takes a
    machine-wide lock and starts a Claude session, so it cannot run inside the gate. The decision
    is tested here against literal preflight output and the WIRING is asserted over the parsed AST.
    See scripts/tests/fixall-longrun.Tests.ps1.
#>

# astrid-web's DEFAULT_LONG_RUN_WINDOW and FIXALL_LONG_MAX_USD default, named here so the two
# cannot drift apart unnoticed. The PARSE of the window stays over there - this is only what we
# send when nobody has said otherwise.
$script:FixallDefaultLongRunWindow = '22-6'
$script:FixallDefaultLongMaxUsd = '50'

# One line out of whatever the native command handed back. Invoke-WebTool returns ErrorRecords and
# blank lines alongside strings, and the padding is not always stripped, so a reader that insists
# on bare trimmed strings finds nothing and reports a quiet default.
function Get-FixallPlanLine([string[]]$Output, [string]$Pattern) {
    foreach ($line in @($Output)) {
        if ($null -eq $line) { continue }
        $text = ([string]$line).Trim()
        if ($text -match $Pattern) { return $Matches[1].Trim() }
    }
    return ''
}

<#
.SYNOPSIS
    Read the preflight's RUN- lines into this tick's watchdog, cap and task ids.

.PARAMETER Output
    Exactly what `agent-queue-status.ts` wrote. A call that was never given `--long-run-window`
    prints none of these lines, which is why every default is carried in rather than assumed.

.PARAMETER DefaultMinutes
    This loop's own watchdog, and the FLOOR for whatever the plan asks for.

.PARAMETER DefaultMaxUsd
    This loop's own budget bound. Empty means the operator turned the cap off, and a long run must
    not turn it back on.

.PARAMETER LongMaxUsd
    The bound for a tick that was sized for a long task. The watchdog catches a run that HANGS; the
    budget catches one that stays BUSY, and an 8-hour watchdog under a 75-minute budget is a
    75-minute run wearing a longer watchdog.
#>
function Get-RunPlan {
    [CmdletBinding()]
    param(
        [string[]]$Output = @(),
        [Parameter(Mandatory = $true)][int]$DefaultMinutes,
        [string]$DefaultMaxUsd = '',
        [string]$LongMaxUsd = ''
    )

    $minutes = $DefaultMinutes
    $raw = Get-FixallPlanLine $Output '^RUN-MINUTES:\s*(\d+)\s*$'
    if ($raw) {
        $asked = [int]$raw
        # Lengthen only. See the header: a shortened watchdog kills ordinary tasks mid-gate, and
        # nothing on this side can tell a version skew from a decision.
        if ($asked -gt $minutes) { $minutes = $asked }
    }

    # `RUN-LONG: 1` exactly, as the shell matches it. Anything else is a line we do not understand,
    # and raising a budget on a guess is the expensive direction.
    $long = (Get-FixallPlanLine $Output '^RUN-LONG:\s*(1)\s*$') -eq '1'

    $maxUsd = $DefaultMaxUsd
    if ($long -and $DefaultMaxUsd) { $maxUsd = $LongMaxUsd }

    # `RUN:` is anchored so the keys that share its prefix cannot be read as the note.
    return [pscustomobject]@{
        MaxMinutes = $minutes
        MaxUsd     = $maxUsd
        Long       = $long
        NextTask   = (Get-FixallPlanLine $Output '^RUN-TASK:\s*(\S.*)$')
        DeferTasks = (Get-FixallPlanLine $Output '^RUN-DEFER:\s*(\S.*)$')
        Note       = (Get-FixallPlanLine $Output '^RUN:\s*(\S.*)$')
    }
}
