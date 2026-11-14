#Requires -Version 5.1
<#
.SYNOPSIS
    An in-session /fixall takes the working-tree lock before it reads the queue. Task f7ee5e57.

.DESCRIPTION
    TWO /fixall RUNS WORKED THIS CHECKOUT AT ONCE on 2026-11-13/14 and one got a GREEN GATE ON A
    TREE THAT NO LONGER EXISTED: `npm run predeploy` reported OK with 623 tests on a branch, and
    the same content failed BrandCopyTests and BrandProfileTests immediately on merge, because
    HEAD moved under the gate while it ran. A green result for a tree that never existed is the
    gate lying, and nothing in its output says so.

    THE LOCK THAT PREVENTS THIS ALREADY EXISTS, and it is the shared one - astrid-web's
    `scripts/fixall-session.ts`, keyed on process LIVENESS (never an age limit) and scoped to one
    WORKING TREE. FIXALL_WORKFLOW.md makes it that document's FIRST section: "Before anything
    else, including reading the queue". An interactive acquire additionally stands down every
    scheduled loop on the machine (AWTD-1212).

    So the bug here is not a missing mechanism, and none of the shapes the task proposed - a new
    lock file, refusing a non-`main` HEAD, a worktree per run - is needed. THE BUG IS WIRING, in
    one place: `scripts/fixall-loop.ps1` acquires the lock (with `--scheduled`), and
    `.claude/commands/fixall.md` does not mention it at all. Its "the workflow itself is shared"
    section enumerates what the canonical document covers and omits that document's first and most
    load-bearing section, so an in-session run walks straight into the tick. The scheduled loop
    fires at :10 and :40, so an interactive run of any length WILL overlap one.

    WHY THIS FILE ASSERTS ON WIRING RATHER THAN RUNNING THE LOCK, which is the choice
    fixall-loop-guards.Tests.ps1 argues at length and for the same reasons: an acquire is a
    shell-out to astrid-web's tsx, so running it would need a sibling checkout and a node process
    inside the gate - failing for any developer without astrid-web beside this repo - and it would
    WRITE to the real machine-wide pause state, standing down the scheduled loops from inside a
    test. What regressed is an absent instruction and an unread variable, so that is what is
    checked.

    THE DEADLOCK IS THE PART A CARELESS FIX GETS WRONG. The loop holds the lock with its own pid,
    which the agent cannot see; inside `claude -p` the agent's parent is a DIFFERENT live pid, so a
    naive acquire by a loop-spawned run returns 2 and the run stops, blocked by its own launcher.
    `fixall-loop.ps1` exports ASTRID_FIXALL_LOCK_HELD=1 precisely so that adopting the lock here
    cannot deadlock against it, and until this task nothing read that variable - `grep` found one
    occurrence, the write. So the command file must honour it, and the helper must too.

    TWO WINDOWS-SPECIFIC BUGS make a copy of the Mac instruction silently wrong, both found by
    running it as written:

    1. IT LOCKS THE WRONG REPOSITORY. `fixall-session.ts` takes no --repo; it keys the lock on
       `git rev-parse --absolute-git-dir` of the CWD. Invoked the documented way
       (`cd ../astrid-web; npx tsx scripts/fixall-session.ts ...`) it locked astrid-web and left
       THIS tree unprotected - the one failure mode indistinguishable from success.
    2. PPID IS 1 IN THIS HARNESS'S BASH TOOL, not the harness session. FIXALL_WORKFLOW.md's claim
       is verified on the Mac. Here the lock would record a meaningless pid, and liveness - the
       whole basis of staleness - becomes worthless. The ancestry walk is reliable:
       powershell -> cmd -> claude.exe finds THIS session even with several open.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-session-guard.Tests.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandPath = Join-Path $RepoRoot '.claude\commands\fixall.md'
$HelperPath = Join-Path $RepoRoot 'scripts\fixall-session.ps1'
$PredeployPath = Join-Path $RepoRoot 'scripts\predeploy.ps1'

$script:Failures = 0
$script:Ran = 0
# Assertions MADE, not passed - a case that exits early makes none, and that is the one outcome a
# failure count cannot see (the lesson fixall-sweep.Tests.ps1 learned by printing OK for six
# vacuous cases).
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
    $script:Failures++
}

function Assert-NotMatch([string]$What, [string]$Pattern, [string]$Actual) {
    $script:Checks++
    if ($Actual -notmatch $Pattern) { return }
    Write-Host "    FAIL $What" -ForegroundColor Red
    Write-Host "      expected NOT to match: [$Pattern]" -ForegroundColor Red
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

if (-not (Test-Path $CommandPath)) {
    Write-Host "FAIL: no /fixall command file at $CommandPath" -ForegroundColor Red
    exit 1
}
$Command = Get-Content -Raw -Path $CommandPath

Write-Host ''
Write-Host 'an in-session /fixall takes the working-tree lock first (task f7ee5e57)' -ForegroundColor Cyan

Invoke-Case 'the command file tells the run to take the lock, and names the shared script' {
    Assert-Match 'it names fixall-session' 'fixall-session' $Command
    Assert-Match 'and tells the run to acquire' '(?i)acquire' $Command
}

Invoke-Case 'the lock comes BEFORE the queue is read - the whole point of the guard' {
    # "Before anything else, including reading the queue" (FIXALL_WORKFLOW.md). A lock taken after
    # the queue has been pulled and a task claimed has already duplicated a peer's work.
    $lockAt = $Command.IndexOf('fixall-session')
    $boardAt = $Command.IndexOf('## Finding the board')
    Assert-True 'the command file has a board/queue section' ($boardAt -ge 0)
    Assert-True 'the lock is mentioned at all' ($lockAt -ge 0)
    Assert-True 'and the lock instruction precedes it' ($lockAt -lt $boardAt)
}

Invoke-Case 'a held tree means stop, and the exit code that says so is named' {
    Assert-Match 'exit code 2 is called out as the held case' '(?i)exit(s)?\s+(code\s+)?.?2' $Command
    Assert-Match 'and it means another live session holds this tree' '(?i)another (live )?session' $Command
    Assert-Match 'and the instruction is to stop' '(?i)\bstop\b' $Command
}

Invoke-Case 'a loop-spawned run does not deadlock against its own launcher' {
    # fixall-loop.ps1 holds the lock with a pid the agent cannot see, and exports this so that
    # adopting the lock here cannot block the very run the loop just started.
    Assert-Match 'the command file honours ASTRID_FIXALL_LOCK_HELD' 'ASTRID_FIXALL_LOCK_HELD' $Command
}

Invoke-Case 'the Mac instruction is not copied verbatim - PPID is 1 in this harness' {
    # Mentioning $PPID to warn against it is correct and wanted; what must never appear is the
    # Mac's actual invocation, which would record a pid that is not the session and make the
    # liveness check - the only thing that reclaims a stale lock - meaningless.
    Assert-NotMatch 'the command file never passes PPID as the pid' '--pid\s+\$PPID' $Command
    Assert-Match 'and it warns against it explicitly' '(?i)(not|never)[^.\n]*\$PPID|\$PPID[^.\n]*(is\s+`?1`?|not the)' $Command
}

Invoke-Case 'the helper exists and parses' {
    Assert-True 'there is a helper at scripts/fixall-session.ps1' (Test-Path $HelperPath)
    if (-not (Test-Path $HelperPath)) { return }
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($HelperPath, [ref]$tokens, [ref]$errors) | Out-Null
    $errorCount = 0
    if ($errors) { $errorCount = $errors.Count }
    Assert-Equal 'the helper parses cleanly' '0' $errorCount
}

Invoke-Case 'the helper cannot lock the wrong repository' {
    if (-not (Test-Path $HelperPath)) {
        Assert-True 'the helper is present' $false
        return
    }
    $helper = Get-Content -Raw -Path $HelperPath
    # The lock is keyed on the CWD's git dir, so the helper must run the acquire with the cwd
    # pinned to THIS repo rather than wherever the agent happened to be standing.
    Assert-Match 'the helper resolves this repo root' '\$RepoRoot' $helper
    Assert-Match 'and pins the cwd to it around the shell-out' 'Push-Location' $helper
    Assert-Match 'it finds astrid-web as a sibling, like the loop does' 'astrid-web' $helper
    Assert-Match 'and calls the shared script' 'fixall-session\.ts' $helper
}

Invoke-Case 'the helper resolves the harness pid by ancestry, never PPID' {
    if (-not (Test-Path $HelperPath)) {
        Assert-True 'the helper is present' $false
        return
    }
    $helper = Get-Content -Raw -Path $HelperPath
    Assert-Match 'it walks the process ancestry' 'ParentProcessId' $helper
    Assert-Match 'looking for the harness process' '(?i)claude' $helper
    Assert-NotMatch 'and never passes PPID as the pid' '--pid.{0,12}\$PPID' $helper
    # The RESOLVED pid is what gets handed over - not this PowerShell's own, which exits in
    # milliseconds and would read as stale to the very next caller. (Asserted positively: these
    # comparisons are case-insensitive, so a negative on $PID also matches $pidToUse.)
    Assert-Match 'the pid handed to the shared script is the resolved one' '--pid.{0,24}pidToUse' $helper
}

Invoke-Case 'the helper honours the loop and passes the stand-down code through' {
    if (-not (Test-Path $HelperPath)) {
        Assert-True 'the helper is present' $false
        return
    }
    $helper = Get-Content -Raw -Path $HelperPath
    Assert-Match 'it reads ASTRID_FIXALL_LOCK_HELD' 'ASTRID_FIXALL_LOCK_HELD' $helper
    # 2 rather than 1 for "held" mirrors claim-fixall-task.ts, where 2 is likewise "someone else
    # got there first, skip without complaining". Collapsing it to 1 would make a held tree
    # indistinguishable from a broken call.
    Assert-Match 'and 2 stays 2 rather than collapsing to a generic failure' 'exit\s+2' $helper
}

Invoke-Case 'the gate actually runs this file' {
    # predeploy.ps1 names its script tests in an explicit allowlist, so a new file that is not
    # added there never runs and this whole guard would be decoration.
    Assert-True 'predeploy.ps1 is present' (Test-Path $PredeployPath)
    if (-not (Test-Path $PredeployPath)) { return }
    $predeploy = Get-Content -Raw -Path $PredeployPath
    Assert-Match 'predeploy runs fixall-session-guard.Tests.ps1' 'fixall-session-guard\.Tests\.ps1' $predeploy
}

Write-Host ''
if ($script:Checks -lt 20) {
    Write-Host "FAIL: only $($script:Checks) assertion(s) ran - this file is meant to make more" -ForegroundColor Red
    $script:Failures++
}
if ($script:Failures -gt 0) {
    Write-Host "RESULT: FAILED - $($script:Failures) failure(s) in $($script:Ran) case(s)" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: OK - $($script:Ran) case(s), $($script:Checks) assertion(s)" -ForegroundColor Green
exit 0
