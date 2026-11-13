#Requires -Version 5.1
<#
.SYNOPSIS
    How long this tick may be, and which task it may take. Task 0216d83c.

.DESCRIPTION
    A task that will not fit one 75-minute tick carries a `LONG-RUN` line in its description
    (FIXALL_WORKFLOW.md -> "Tasks longer than one run", AWTD-1041). Three Windows runs in a row read
    two such tasks, concluded neither could be split, commented, and left them in Ready; flagging
    them on 2026-11-12 was supposed to end that, and on this board it changed nothing, because the
    loop had never heard of the flag.

    THE DECISION IS NOT MADE HERE. astrid-web owns it in `scripts/lib/long-run.ts` - how long the
    flag asks for, whether the overnight window is open, whether a flagged task is taken first or
    deferred for a tick - and `scripts/agent-queue-status.ts --long-run-window` prints the answer
    alongside the queue verdict the preflight was already being asked for:

        RUN-MINUTES: <watchdog for this tick>
        RUN-LONG: 1                            # only when the tick was sized for a long task
        RUN-TASK: <id /fixall takes first>
        RUN-DEFER: <ids held back for the window, comma-joined>
        RUN: <one human line for the log>

    astrid-web's own loop (`scripts/fixall-loop.sh`, "Size the run") does nothing with those but
    read them into four shell variables. This file is the same five lines of reading, which is the
    whole point: the queue is CALLED rather than copied here, so a second implementation of "how
    long may this run be" would drift against it silently and nothing would fail.

    WHAT IT IS CAREFUL ABOUT is a plan it cannot read. Guessing has two failure modes and they are
    not symmetric. An 8-hour watchdog on an ordinary task wastes a night. A 75-minute watchdog on a
    task that was flagged *because* it needs eight hours is the hard kill this mechanism exists to
    prevent - the run dies mid-task, Task Scheduler runs none of the recovery, and the next tick
    finds the same task back at the top of the queue. So anything malformed leaves the caller's
    defaults exactly as they were, and the plan only ever RAISES them.

    Separated from fixall-loop.ps1 for the same reason fixall-wake.ps1 is: the loop cannot be run
    inside the gate, so the reading is tested behind injected input and the wiring is asserted over
    the parsed AST. See scripts/tests/fixall-longrun.Tests.ps1.
#>

<#
.SYNOPSIS
    Read the preflight's RUN- lines into a watchdog, a budget cap and the two task ids /fixall needs.

.PARAMETER PreflightOutput
    The lines `scripts/agent-queue-status.ts --long-run-window ...` printed. Whatever the shell-out
    produced: under PS 5.1 a native call can hand back ErrorRecords and nulls mixed in with strings,
    so everything is cast before it is matched. Empty or null is a legitimate input - `-IgnoreQueue`
    skips the preflight entirely - and means "no plan", not "a plan of nothing".

.PARAMETER DefaultMinutes
    The watchdog already in force. The plan can only raise it: an operator who passed
    -MaxMinutes 600 asked for 600, and a 480-minute plan must not cut them back.

.PARAMETER DefaultMaxUsd
    The budget cap already in force, as a string because empty means "pass no --max-budget-usd at
    all". A long run RAISES a cap that exists; it never introduces one where the operator removed it.

.PARAMETER LongMaxUsd
    The cap a long run gets instead. 50 in both loops - eight hours of work costs more than one.
#>
function Get-RunPlan {
    [CmdletBinding()]
    param(
        [AllowNull()][AllowEmptyCollection()][object[]]$PreflightOutput,
        [Parameter(Mandatory = $true)][int]$DefaultMinutes,
        [AllowEmptyString()][string]$DefaultMaxUsd = '',
        [AllowEmptyString()][string]$LongMaxUsd = '50'
    )

    $lines = @(@($PreflightOutput) | Where-Object { $null -ne $_ } | ForEach-Object { ([string]$_).TrimEnd() })

    # `head -1` in the web loop. Two of any of these would mean the preflight had changed shape, and
    # taking the first is both what that loop does and the conservative half of the difference.
    function Get-FirstMatch([string[]]$Haystack, [string]$Pattern) {
        foreach ($line in $Haystack) {
            $m = [regex]::Match($line, $Pattern)
            if ($m.Success) { return $m.Groups[1].Value.Trim() }
        }
        return ''
    }

    # Digits only, anchored both ends. "90m", "soon" and "-30" are all a preflight this loop cannot
    # read, and the default is the right answer to every one of them.
    $minutes = $DefaultMinutes
    $printed = Get-FirstMatch $lines '^\s*RUN-MINUTES:\s*([0-9]+)\s*$'
    if ($printed) { $minutes = [Math]::Max($DefaultMinutes, [int]$printed) }

    $long = @($lines | Where-Object { $_ -match '^\s*RUN-LONG:\s*1\s*$' }).Count -gt 0

    # An uncapped run stays uncapped - see DefaultMaxUsd.
    $maxUsd = $DefaultMaxUsd
    if ($long -and $DefaultMaxUsd) { $maxUsd = $LongMaxUsd }

    # `RUN-TASK:`/`RUN-DEFER:` carry ids; a bare `RUN:` is prose for the log. The colon is part of
    # each pattern so the prose line can never be read as a value.
    return [pscustomobject]@{
        MaxMinutes = $minutes
        MaxUsd     = $maxUsd
        Long       = $long
        NextTask   = Get-FirstMatch $lines '^\s*RUN-TASK:\s*(\S.*)$'
        DeferTasks = Get-FirstMatch $lines '^\s*RUN-DEFER:\s*(\S.*)$'
        Notes      = @($lines | Where-Object { $_ -match '^\s*RUN:\s*\S' } |
                ForEach-Object { ([regex]::Match($_, '^\s*RUN:\s*(.*)$')).Groups[1].Value.Trim() })
    }
}
