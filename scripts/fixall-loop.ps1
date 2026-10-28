#Requires -Version 5.1
<#
.SYNOPSIS
    One scheduled, unattended pass of /fixall on the Astrid WINDOWS board.

.DESCRIPTION
    WHY THIS EXISTS. Assigning a task to Claude Agent in Astrid starts nothing: in polling mode
    Astrid calls out to no one, by design (astrid-web docs/AGENT_POLLING_MODE.md, after the
    2026-08-23 retry storm). Something has to poll. This repo had only
    `.github/workflows/fixall.yml`, which is the COPILOT path and which has been failing on every
    tick since it was written because the four repository secrets it needs were never added — so
    every Windows /fixall to date has run because somebody typed it.

    Ported from astrid-web/scripts/fixall-loop.sh, which is itself a port of the iOS one. The
    differences are all consequences of the platform and of the queue living next door:

      * PowerShell, not zsh, and Windows Task Scheduler, not launchd. No node shim: that exists on
        the Mac for a TCC reason (a launchd-started zsh cannot read ~/Documents) which has no
        Windows equivalent.
      * The task tooling is astrid-web's, in the sibling checkout, run through ITS tsx and ITS
        .env.local. Nothing here duplicates "which tasks are ready": a second implementation
        drifts, and the drift is silent, because a queue that is wrong looks exactly like a quiet
        day.
      * The board is resolved BY NAME, never by a hardcoded id. An id is account data and a stale
        one fails by answering "nothing to do" (docs/AUTOMATION.md).

    IT RUNS ON THE CLAUDE CODE CLI SUBSCRIPTION, NEVER THE ANTHROPIC API. `claude -p` is the whole
    runtime; no API key is read anywhere in this path, and -MaxUsd is a CLI safety bound rather
    than metered spend. That is the point of polling mode: the subscription already covers the
    work, and the harness has the repo, the branches and the tests.

    THE LAST LINE IS ALWAYS EXACTLY ONE RESULT: LINE, so a scheduler or a person skimming the log
    can tell what happened without reading it:

      RESULT: OK      - a run happened
      RESULT: SKIPPED - deliberately did nothing, and why
      RESULT: FAILED  - tried and could not

    WHY A SKIP IS THE COMMON CASE. Every half hour is often, and most ticks have nothing to do or
    land while somebody is already working the tree. A skip is the healthy outcome, not an error,
    so it exits 0 and says so in one line.

    IT WAKES FOR REPLIES, not only for Ready tasks. Until task 18ba8c2e guard 3 asked
    `ready-tasks.ts windows --json`, which answers Ready plus lane work and nothing else, so a
    comment on a task this loop had moved to `Doing` - or on one it had finished - woke no tick,
    ever. The header's own reason for that ("agent-queue-status.ts requires a LIST ID, and this
    board's id is deliberately nowhere in this repo") expired with AWTD-1383: the shared preflight
    now resolves `--board windows` by name and refuses a duplicate name rather than taking the
    first match. So guard 3 asks it instead and gets queue + attention + lanes in one request.
    `attention` is half of the engagement contract - the queue is what to work, attention is what
    has been said to this agent and not answered - and a loop that reads only the first is deaf by
    construction. The decisions live in scripts/lib/fixall-wake.ps1, where they are tested.

.PARAMETER Force
    Skip the dirty-tree and branch guard. Testing only.

.PARAMETER IgnoreQueue
    Run even when the queue is empty. For proving the Claude path end to end.

.PARAMETER DryRun
    Run every guard and stop before starting Claude.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/fixall-loop.ps1

.EXAMPLE
    # What Task Scheduler runs. See scripts/install-fixall-task.ps1.
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/fixall-loop.ps1
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$IgnoreQueue,
    [switch]$DryRun,

    # The model for an unattended run, the watchdog that kills a wedged one, and the CLI budget
    # bound that stops one which stays busy on a task it cannot finish. Two bounds, because a run
    # goes wrong in two different ways: the watchdog catches a HANG, the budget catches BUSY.
    [string]$Model = $(if ($env:FIXALL_MODEL) { $env:FIXALL_MODEL } else { 'opus' }),
    [int]$MaxMinutes = $(if ($env:FIXALL_MAX_MINUTES) { [int]$env:FIXALL_MAX_MINUTES } else { 75 }),
    [string]$MaxUsd = $(if ($null -ne $env:FIXALL_MAX_USD) { $env:FIXALL_MAX_USD } else { '10' }),
    [string]$PermissionMode = $(if ($env:FIXALL_PERMISSION_MODE) { $env:FIXALL_PERMISSION_MODE } else { 'acceptEdits' }),

    # How many Ready tasks one tick may take. ONE, because a run that takes the whole queue does not
    # fit either bound above once a predeploy costs ~10 minutes and a task costs real money: on
    # 2026-09-27 three runs in a row were cut off partway through a task they had already started.
    # The next tick takes the next task, so the queue still drains - it drains one task at a time,
    # each of which is finished, gated and reported. Answering RECHECK/REVIEW does not count.
    [int]$MaxTasks = $(if ($env:FIXALL_MAX_TASKS) { [int]$env:FIXALL_MAX_TASKS } else { 1 }),

    # How long a checkout must have been untouched before this tick treats its leftovers as a dead
    # run's and commits them. THIRTY, one whole tick interval: "quiet for longer than a tick" is the
    # rule, and a long model turn or a cargo build is minutes rather than thirty. The asymmetry picks
    # the default - sweeping a live run costs work nobody can get back, waiting costs one skipped
    # tick which says why. See scripts/lib/fixall-sweep.ps1.
    [int]$QuietMinutes = $(if ($env:FIXALL_QUIET_MINUTES) { [int]$env:FIXALL_QUIET_MINUTES } else { 30 }),

    # How long a task may sit in Doing before the start-of-tick board pass calls the claim abandoned.
    # 180, which is astrid-web's default and well past the $MaxMinutes watchdog: a bound inside the
    # watchdog window would release a task out from under a session still working it. The asymmetry
    # is the opposite way round to $QuietMinutes - releasing early costs a live run its claim, while
    # waiting costs a few hours on a task nobody is working.
    [int]$StaleDoingMinutes = $(if ($env:FIXALL_STALE_DOING_MINUTES) { [int]$env:FIXALL_STALE_DOING_MINUTES } else { 180 })
)

# Never stop on a native command's stderr: this script decides what is fatal, and most of what it
# runs reports by exit code.
$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$WebRepo = Join-Path (Split-Path -Parent $RepoRoot) 'astrid-web'
# The shared core is its own repository since 2026-09-26 and is pinned by revision, so a task whose
# fix is a rule, a service, the Outbox or sync is worked THERE and the pin bumped here. Without the
# checkout in the session's allowed directories such a task is simply unreachable, and the run can
# only do `app/` work - which is most of this board's backlog, but not all of it.
$CoreRepo = Join-Path (Split-Path -Parent $RepoRoot) 'astrid-core'
$LogDir = Join-Path $env:LOCALAPPDATA 'Astrid\logs'
$LogFile = Join-Path $LogDir 'fixall-windows.log'
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }

# Save-UnfinishedWork: what this script does with a checkout its child died in the middle of. Its
# own file so the tests can run it against a scratch repo - see scripts/tests/fixall-loop-cleanup.Tests.ps1.
. (Join-Path $PSScriptRoot 'lib\fixall-cleanup.ps1')
# The same thing for a run this script never got to watch exit - a kill takes the save below down
# with it - and for astrid-core, which nothing used to look at. Task 07c3b420; the library's header
# has the incident. It dot-sources the cleanup above, so the order of these two lines is cosmetic.
. (Join-Path $PSScriptRoot 'lib\fixall-sweep.ps1')
# Why this tick woke, and how it acknowledges that afterwards without muting anything it never
# looked at. Task 18ba8c2e; the library's header has the two-phase contract in full.
. (Join-Path $PSScriptRoot 'lib\fixall-wake.ps1')

# WHERE THE WAKE JOURNAL LIVES. Under %LOCALAPPDATA%, beside the log, and NOT in %TEMP%: a journal
# is read by a LATER tick - that is the whole point of it - and %TEMP% is swept by Windows and by
# every disk-cleanup run. Per board, because the keys are per board.
$WakeStateDir = Join-Path $env:LOCALAPPDATA 'Astrid\state\fixall-windows'
$WakeJournal = Join-Path $WakeStateDir "wake-$PID.json"
$WakeArtifact = Join-Path $WakeStateDir "preflight-$PID.json"
if (-not (Test-Path $WakeStateDir)) { New-Item -ItemType Directory -Path $WakeStateDir -Force | Out-Null }

# Everything this script says goes to the console AND to the log, because Task Scheduler captures
# neither. Native command output is appended separately, where it is produced.
function Say([string]$Line) {
    Write-Host $Line
    Add-Content -Path $LogFile -Value $Line -Encoding utf8
}

# What the start-of-tick sweep did, appended to whichever RESULT line this tick ends up printing.
# A tick that cleans up after a killed run has to SAY so on its one line, or the recovery is as
# invisible as the four days of `SKIPPED - nothing to do` it exists to end.
$script:SweepNote = ''

function Finish([string]$Result) {
    if ($script:SweepNote) { $Result += " | $($script:SweepNote)" }
    Say $Result
    if ($Result.StartsWith('RESULT: OK') -or $Result.StartsWith('RESULT: SKIPPED')) { exit 0 }
    exit 1
}

Say "-------- fixall loop (windows) $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') --------"

# ── The tools this needs, and where they are ──────────────────────────────────────────────────
$Tsx = Join-Path $WebRepo 'node_modules\.bin\tsx.cmd'
if (-not (Test-Path $Tsx)) {
    Finish "RESULT: FAILED - no tsx at $Tsx (is astrid-web checked out beside this repo, and npm ci run?)"
}

$ClaudeCmd = $env:CLAUDE_BIN
if (-not $ClaudeCmd) {
    # The CLI installs as claude.cmd beside node; Get-Command finds claude.ps1 first, which
    # Start-Process cannot execute.
    $found = Get-Command 'claude.cmd' -ErrorAction SilentlyContinue
    if ($found) { $ClaudeCmd = $found.Source }
}
if (-not $ClaudeCmd -or -not (Test-Path $ClaudeCmd)) {
    Finish 'RESULT: FAILED - no claude CLI found (set CLAUDE_BIN to claude.cmd)'
}

# Run astrid-web's tooling from ITS checkout: that is where its .env.local, its node_modules and
# its OAuth pair are. Returns the captured output; the caller reads $LASTEXITCODE.
function Invoke-WebTool([string[]]$ScriptArgs) {
    Push-Location $WebRepo
    try { & $Tsx @ScriptArgs 2>&1 } finally { Pop-Location }
}

# Phase two of waking: record the keys the preflight handed back, by how the run ended. The LIST ID
# comes from the journal, not from a second `--board windows` resolution - a resolution that fails
# after the run would otherwise strand keys that were already read, and an id resolved twice is an
# id that can differ twice.
#
# The keys travel as a FILE because PowerShell 5.1 loses the quotes on a JSON array handed to a
# native command, which is the whole reason `--seen-keys-file` exists (AWTD-1383). The arguments are
# built into a variable rather than written inline so that exactly ONE command in this script is the
# board preflight - scripts/tests/fixall-wake.Tests.ps1 finds guard 3 by looking for the script's
# name and would otherwise find this instead.
function Invoke-WakeKeysAck([string]$KeysFile, [bool]$Failed, [string]$ListId) {
    if (-not $ListId) {
        Say '  WAKE: the journal carries no board id, so the keys cannot be acknowledged'
        return $false
    }
    $ackArgs = @('scripts/agent-queue-status.ts', '--agent', 'claude', '--list', $ListId,
        '--mark-seen', '--seen-keys-file', $KeysFile)
    if ($Failed) { $ackArgs += '--failed' }
    Invoke-WebTool $ackArgs | ForEach-Object { Say "  $_" }
    return ($LASTEXITCODE -eq 0)
}

# Handed to both acknowledgement passes, so a replayed journal and this tick's own are recorded by
# the same code path.
$WakeAck = {
    param($KeysFile, $Failed, $ListId)
    return (Invoke-WakeKeysAck -KeysFile $KeysFile -Failed ([bool]$Failed) -ListId ([string]$ListId))
}

# The half-sentence about leftovers that both the RESULT line and the task comment end with.
function Format-SavedNote($Saved) {
    if ($Saved.SavedBranch -and $Saved.PushedBranch) { return "its unfinished work is saved on $($Saved.SavedBranch) (WIP, unverified)" }
    if ($Saved.SavedBranch) { return "its unfinished work is committed on $($Saved.SavedBranch) but could not be pushed (WIP, unverified)" }
    if ($Saved.PushedBranch) { return "it left $($Saved.PushedBranch), now pushed for review" }
    return 'nothing was pushed by this run'
}

# Say on the board what the log would otherwise be the only record of. The task id comes from the
# run itself (ASTRID_FIXALL_TASK_FILE) rather than from the pre-run queue, because a comment on the
# wrong task is worse than no comment. No task file means the run died before taking anything -
# there is nothing to tell, and the RESULT line has already said it.
function Send-DiedRunNote([string]$TaskFile, [string]$Reason, $Saved) {
    if (-not (Test-Path $TaskFile)) { return }
    $taskId = (Get-Content $TaskFile -Raw -ErrorAction SilentlyContinue).Trim()
    if (-not $taskId) { return }
    $leftovers = Format-SavedNote $Saved
    $note = "**Scheduled /fixall (windows) did not finish** - $Reason, and $leftovers. " +
    'This task is still in Doing and nothing on that branch has passed a gate: a tip reading ' +
    '`wip: ... UNFINISHED, UNVERIFIED` is a resume point, not something to ship. ' +
    'Log: %LOCALAPPDATA%\Astrid\logs\fixall-windows.log'
    Invoke-WebTool @('scripts/add-task-comment.ts', $taskId, $note) | ForEach-Object { Say "  $_" }
    if ($LASTEXITCODE -ne 0) { Say '  (could not comment on the task it had taken)' }
}

# Put a run's abandoned Doing claims back. ONE call site for the tool, THREE callers: the after-run
# pass over this tick's own claims, the start-of-tick pass over a dead tick's (task 1b9fed0a), and
# the start-of-tick pass over the BOARD (task 1cc6ac2f), which is the only one of the three that can
# reach a claim no claims file records at all - one made by an interactive session, or by a session
# that died before a claims file was ever written.
#
# The board is named, never carried as an id: `--board windows` resolves it through
# GET /api/v1/lists (AWTD-1267) and exits 1 rather than reporting an empty board when the name is
# not found. A hardcoded id is account data, and a stale one fails by answering "nothing to do" -
# which reads exactly like a quiet day (docs/AUTOMATION.md).
#
# EVERY RULE ABOUT WHAT A RELEASE DOES IS THE SCRIPT'S, not this loop's — it keeps the agent
# assignment so the next run resumes from the branch rather than starting over, comments with the
# branch carrying the task's id, hands the task back to its human creator in Waiting on a SECOND
# release rather than retrying it a third time, and never touches a Doing task assigned to a person
# (astrid-web lib/doing-release.ts). Writing any of that here would be a second implementation of
# "release", and the drift would be silent.
#
# --repo is THIS repo, because that is where the task branches are: the release names the branch
# that carries the work, and run from astrid-web it would otherwise read astrid-web's branches.
function Invoke-DoingRelease {
    [CmdletBinding(DefaultParameterSetName = 'Claims')]
    param(
        [Parameter(Mandatory = $true, ParameterSetName = 'Claims')][string]$ClaimsFile,
        [Parameter(Mandatory = $true, ParameterSetName = 'Board')][string]$Board,
        [Parameter(ParameterSetName = 'Board')][int]$StaleMinutes = 180
    )
    # Which claims to consider is the ONLY thing that differs between the passes. --agent, --repo and
    # the output handling are shared on purpose: a second shell-out is where they drift apart.
    $selector = if ($PSCmdlet.ParameterSetName -eq 'Board') {
        @('--board', $Board, '--stale-minutes', [string]$StaleMinutes)
    }
    else {
        @('--claims-file', $ClaimsFile)
    }
    Invoke-WebTool (@('scripts/release-stuck-doing.ts', '--agent', 'claude') + $selector +
        @('--repo', $RepoRoot)) | ForEach-Object { Say "  $_" }
    return ($LASTEXITCODE -eq 0)
}

# ── The checkouts a tick is responsible for ───────────────────────────────────────────────────
# Both of them, which is the correction task 07c3b420 makes: the old guard and the old save read
# this repo only, so a run killed while working a core task left 449 uncommitted lines in
# astrid-core and every tick for four days reported a quiet board.
#
# RequireMain and ReturnToMain are false for the core ON PURPOSE. It is a plain clone shared with
# the other repos' loops, not a git worktree: pushing a branch touches neither its HEAD nor its
# working tree, but `git checkout main` moves the HEAD of whatever else is in there (2026-09-27).
# So the core's work is pushed and left where it stands - and a core on a branch therefore cannot be
# a reason to skip, or the loop would wedge on the state the sweep itself just created.
$Checkouts = @(
    [pscustomobject]@{ Name = 'working tree'; Path = $RepoRoot; RequireMain = $true; ReturnToMain = $true }
    [pscustomobject]@{ Name = 'astrid-core'; Path = $CoreRepo; RequireMain = $false; ReturnToMain = $false }
)

# Does anything still live hold this checkout? The sweep asks before committing anything, because
# the one thing worse than leftovers nobody finds is a tick committing a RUNNING session's work.
# Reuses the same per-working-tree lock as guard 1; a STALE holder is the died run itself and must
# not protect its own remains.
function Get-SessionHolder([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    Push-Location $Path
    try { $out = (& $Tsx (Join-Path $WebRepo 'scripts\fixall-session.ts') 'status' 2>&1 | Out-String) }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { return $null }
    # "<harness> (pid <n>, live|STALE (holder is gone)) has held <tree> since <when>"
    if ($out -notmatch '(?m)^(\S+)\s+\(pid\s+(\d+),\s+(live|STALE)') { return $null }
    return [pscustomobject]@{ Harness = $Matches[1]; HolderPid = [int]$Matches[2]; Live = ($Matches[3] -eq 'live') }
}

# ── Guard 1: one session per working tree ─────────────────────────────────────────────────────
# Cheap by design: no Claude session is started just to discover the tree is busy. The lock is
# keyed to the git directory, so this tree and astrid-web's never see each other, and a lock whose
# holder is gone is reclaimed by liveness rather than expiring under a working session. Run from
# THIS repo, which is what makes it this repo's lock.
#
# --scheduled IS NOT OPTIONAL, and omitting it is invisible (task 38a93fca). Without it
# `fixall-session.ts` (astrid-web, lines 143-163) takes the branch written for a HUMAN session: it
# also calls `registerHolder()`, publishing a machine-wide "someone is working here" that stands the
# scheduled loops down, and prints
#
#     the scheduled /fixall loops will stand down until this session exits
#
# which this loop then logged about ITSELF on every tick - read, by anyone debugging a quiet board,
# as an interactive session holding the tree. `shouldYield` filters the caller out by pid so the
# loop never actually blocked itself; what it did was lie in the log, and leave a holder file behind
# on any tick hard-killed before its `release`. astrid-web's loop has always passed it
# (fixall-loop.sh:122).
Push-Location $RepoRoot
try { & $Tsx (Join-Path $WebRepo 'scripts\fixall-session.ts') 'acquire' '--pid' $PID '--harness' 'claude-code' '--scheduled' 2>&1 | ForEach-Object { Say "  $_" } }
finally { Pop-Location }
$lockStatus = $LASTEXITCODE
if ($lockStatus -eq 2) { Finish 'RESULT: SKIPPED - another live session holds this checkout' }
if ($lockStatus -ne 0) { Finish "RESULT: FAILED - could not take the working-tree lock (exit $lockStatus)" }

# The lock is released however this script ends, including a watchdog kill of Claude.
$releaseLock = {
    Push-Location $RepoRoot
    try { & $Tsx (Join-Path $WebRepo 'scripts\fixall-session.ts') 'release' '--pid' $PID *> $null } finally { Pop-Location }
}
try {
    # THE LOCK IS HELD BY THIS SCRIPT, whose pid the agent cannot see. Inside `claude -p` the
    # parent is the claude process - a different LIVE pid - so an acquire by the agent would return
    # 2 and it would stop before reading the queue, blocked by its own launcher. Unlike astrid-web,
    # this repo's .claude/commands/fixall.md does not take the lock at all, so today this variable
    # only documents the arrangement; it is exported so that adopting the lock there cannot
    # deadlock against this.
    $env:ASTRID_FIXALL_LOCK_HELD = '1'

    # ONE READY TASK PER TICK (FIXALL_WORKFLOW.md → "One task per scheduled run"). The bound the
    # agent can act on is a COUNT OF TASKS, not its remaining dollars: it cannot see its own spend
    # until it is nearly gone, and on 2026-09-27 it three times started a task it then could not
    # finish. Stopping cleanly after one - pushed, gated, reported - is what the cap buys.
    $env:ASTRID_FIXALL_MAX_TASKS = [string]$MaxTasks

    # WHICH TASK IT TOOK, so a run that dies can be reported on the right one. A died run cannot
    # write its own completion comment, and the loop cannot guess: the queue it read before the run
    # is a list of candidates, and commenting on the wrong task is worse than saying nothing. So
    # /fixall writes the id here as it takes a task, and this script reads it back afterwards.
    #
    # NOT list chat, which is what astrid-web's loop posts to. That needs a board id, and this
    # board's id is deliberately nowhere in this repo (docs/AUTOMATION.md) - it is resolved by name,
    # and `ready-tasks.ts --json` does not carry it.
    $taskFile = Join-Path $env:TEMP "astrid-fixall-windows-task-$PID.txt"
    Remove-Item $taskFile -Force -ErrorAction SilentlyContinue
    $env:ASTRID_FIXALL_TASK_FILE = $taskFile

    # WHICH TASKS IT CLAIMED, so the ones it leaves in Doing can be released after it — exactly,
    # with no heuristic and no board read. `claim-fixall-task.ts` appends every Ready claim here and
    # does nothing at all when the variable is absent, which it was until task 1b9fed0a: so this
    # loop never recorded a claim, never released one, and a task whose run died left the queue for
    # good. Named for this loop's pid, like the task file above, which is what lets a LATER tick tell
    # a dead run's claims from a live one's.
    $claimsFile = Join-Path $env:TEMP "astrid-fixall-windows-claims-$PID.txt"
    Remove-Item $claimsFile -Force -ErrorAction SilentlyContinue
    $env:ASTRID_FIXALL_CLAIMS_FILE = $claimsFile
    # Whether the after-run release below got through. The `finally` deletes the claims file only
    # then: a file whose release FAILED is the only record of which tasks are stranded, and the next
    # tick's start-of-tick pass is what retries it.
    $claimsReleased = $false

    # ── Guard 1b: stand down while someone is working ──────────────────────────────────────────
    # Guard 1 is per WORKING TREE; contention for a machine is per MACHINE. On 2026-10-11 a session
    # worked astrid-web while the iOS loop ran a simulator suite and the web loop ran predeploy: the
    # 5-minute load average peaked over 890, which is how a run ends up red on test timeouts in
    # files that pass alone (AWTD-1192, AWTD-1197). A tick here starts `npm run predeploy`, which
    # cross-builds ARM64 every run, so it is one of the heavy things worth standing down.
    #
    # So an interactive `fixall-session.ts acquire` in ANY checkout on this machine stands this loop
    # down, and `npm run fixall:pause` covers the work no session knows about - a manual build, a
    # profiling run (AWTD-1212). Until task 38a93fca neither did: this script never called
    # fixall-pause.ts at all, and `npm run fixall:pause` simply did not apply to Windows.
    #
    # HERE, and not later: the sweep below makes git commits in two checkouts and posts comments on
    # tasks, and a tick that stands down after doing those has stood down too late. And inside the
    # try, so the `finally` releases the working-tree lock - a skip from outside it would leak the
    # lock and the next tick would refuse this checkout as held by a process that is gone.
    #
    # An unreadable pause state RUNS THE TICK and says so, which is fixall-pause.ts's own call (its
    # header) and the same one guard 2b makes about an unreadable load average: a loop that stalls on
    # a bad state file is a worse outage than one run too many.
    $yieldOut = Invoke-WebTool @('scripts/fixall-pause.ts', 'yield', '--self-pid', $PID)
    $yieldStatus = $LASTEXITCODE
    # One line, whatever the tool said: the header promises the last line of a tick is exactly one
    # RESULT: line, and this text is pasted straight into it.
    $yieldReason = ((($yieldOut | Out-String) -replace '\s+', ' ')).Trim()
    if ($yieldStatus -eq 2) {
        if (-not $yieldReason) { $yieldReason = 'something is working this machine' }
        Finish "RESULT: SKIPPED - $yieldReason"
    }
    if ($yieldStatus -ne 0) {
        Say "  WARNING: could not read the pause state, running anyway: $yieldReason"
    }

    # ── The sweep: clear up after a run this script never watched exit ────────────────────────
    # BEFORE guard 2, because the leftovers are exactly what guard 2 trips on; a sweep after it would
    # never run. AFTER guard 1, because holding this repo's lock is what makes its leftovers ours to
    # move. The save at the bottom of this script is the better path when it runs - it knows why the
    # run ended - but a CTRL_C_EVENT takes this process down before it, and on 2026-09-29 that is
    # what left a core task's work stranded for four days (task 07c3b420).
    if (-not $Force) {
        $sweep = Invoke-StartOfTickSweep -Checkouts $Checkouts -QuietMinutes $QuietMinutes `
            -TaskFileDirectory $env:TEMP -SelfPid $PID `
            -SessionProbe { param($Path) Get-SessionHolder $Path } `
            -OnTaskNote {
            param($TaskId, $Note)
            Invoke-WebTool @('scripts/add-task-comment.ts', $TaskId, $Note) | ForEach-Object { Say "  $_" }
            if ($LASTEXITCODE -ne 0) { Say "  (could not report the stranded run on task $TaskId)" }
        } `
            -Log { param($Line) Say $Line }
        $script:SweepNote = $sweep.Summary
    }

    # ── Doing must not be a dead end ──────────────────────────────────────────────────────────
    # The board half of the sweep above, and the other half of task 1b9fed0a. A claims file whose
    # owning loop is GONE is a tick that was killed before its own after-run pass could run — the one
    # case that pass structurally cannot cover, since a hard kill is what stops it. Releasing exactly
    # those ids needs no board id, which is why it is this pass and not astrid-web's.
    #
    # BEFORE GUARD 3, so a task released this tick counts as work for THIS tick rather than waiting
    # half an hour for the next one. Before guard 2 as well, deliberately: this is a board operation,
    # not a tree one, and a dirty checkout is no reason to leave a task stranded in Doing.
    #
    # NOT under -not $Force either, for the same reason — -Force is about the tree guards.
    $claimRelease = Invoke-StrandedClaimRelease -ClaimsFileDirectory $env:TEMP -SelfPid $PID `
        -Release { param($Path) Invoke-DoingRelease -ClaimsFile $Path } `
        -Log { param($Line) Say $Line }
    if ($claimRelease.Summary) {
        if ($script:SweepNote) { $script:SweepNote += ' | ' }
        $script:SweepNote += $claimRelease.Summary
    }

    # ── And the claims no file records at all ─────────────────────────────────────────────────
    # The pass above is exact but can only see what a loop wrote down. Two kinds of claim are in no
    # claims file: one made by an INTERACTIVE session, which has no loop and no file, and one made by
    # a run that died between claiming and writing. `f8dfc84e` was the second kind - it sat in Doing
    # from 01:25 on 2026-10-18 and a human had to release it, because every claims-file pass on every
    # tick after it was structurally blind to it.
    #
    # So this pass asks the BOARD instead, and takes a staleness bound rather than a file: anything
    # in Doing and untouched for $StaleDoingMinutes. Delegated to astrid-web for the same reason as
    # the others, and placed here for the same two reasons - before guard 3 so a release counts as
    # work for THIS tick, and before guard 2 because a dirty checkout is no reason to leave somebody
    # else's task stranded.
    #
    # NEVER FATAL, and not guarded by -IgnoreQueue. A release that cannot reach the board leaves the
    # task exactly where it already was, so there is nothing to abort for; ending the tick here would
    # turn a board outage into a day of no work at all.
    $boardRelease = Invoke-DoingRelease -Board 'windows' -StaleMinutes $StaleDoingMinutes
    if (-not $boardRelease) { Say '  RELEASE: could not sweep Doing on the board; leaving it as it was' }

    # ── Guard 2: never clobber work in progress ───────────────────────────────────────────────
    # /fixstuff and an interactive session take no lock, so they are invisible to guard 1, and the
    # sweep above deliberately leaves alone anything touched in the last $QuietMinutes. This is the
    # only thing between a half-hourly tick and somebody's uncommitted work.
    #
    # It now covers BOTH checkouts and NAMES THE ONE THAT STOPPED IT: `astrid-core is dirty` is a
    # different instruction to a human than `working tree is dirty`, and before task 07c3b420 the
    # second was the only thing it could ever say, because the core was never looked at.
    if (-not $Force) {
        $verdict = Get-CheckoutGuardVerdict -Checkouts $Checkouts
        if (-not $verdict.Ok) { Finish "RESULT: SKIPPED - $($verdict.Reason)" }
    }

    # ── What a killed tick could not acknowledge ──────────────────────────────────────────────
    # BEFORE this tick's own preflight, and that order is the point: the after-run pass below cannot
    # cover a tick that was hard-killed, because a hard kill is what stops it. A journal whose owning
    # process is gone is exactly that tick, and a watchdog-killed tick IS a failed run - so its keys
    # take a strike rather than being muted, and rather than waking a session every half hour forever.
    #
    # A journal whose owner is still alive is somebody's live tick and is left strictly alone.
    # NOT under -not $IgnoreQueue or -not $Force: both of those are about this tick's own work, and a
    # dead tick's unacknowledged keys are neither.
    $wakeReplay = Invoke-AbandonedWakeAck -JournalDirectory $WakeStateDir -SelfPid $PID `
        -IsAlive { param($OwnerPid) return ($null -ne (Get-Process -Id $OwnerPid -ErrorAction SilentlyContinue)) } `
        -Ack $WakeAck -Log { param($Line) Say $Line }
    if ($wakeReplay.Summary) {
        if ($script:SweepNote) { $script:SweepNote += ' | ' }
        $script:SweepNote += $wakeReplay.Summary
    }

    # ── Guard 3: is there actually any work, or anything unanswered? ──────────────────────────
    # THE expensive question, asked the cheap way. Without this a quiet tick still boots a whole
    # session - CLAUDE.md, fixall.md, the tool schemas - to ask the queue once and be told it is
    # empty. A few HTTP requests answer the same question.
    #
    # Three things count as "yes", and before task 18ba8c2e this guard could see only the first:
    # the QUEUE (Ready, due, this mailbox, plus unassigned Ready work, which on this board is the
    # hand-off); ATTENTION, the comments and list chat nobody has answered; and the LANES, swept
    # first so a Waiting task whose date has arrived is Ready by the time the answer is counted and
    # RECHECK/REVIEW items come out as work.
    #
    # --require-polling, because polling mode is what makes this loop the dispatcher: an agent in
    # api/webhook/off mode is an ERROR here, not an idle queue, or this loop would race a hosted
    # dispatcher. --no-write-seen, because phase one of waking must write nothing - a tick that is
    # killed must not have muted what woke it.
    $wake = $null
    if (-not $IgnoreQueue) {
        $wake = Get-WakeVerdict -ArtifactPath $WakeArtifact -Preflight {
            param($ArtifactPath)
            $out = Invoke-WebTool @('scripts/agent-queue-status.ts', '--agent', 'claude',
                '--board', 'windows', '--include-unassigned', '--require-polling',
                '--no-write-seen', '--output', $ArtifactPath)
            return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $out }
        }
        Say "  QUEUE: $($wake.Reason)"
        # A queue nobody can see is a reason to RUN and let the agent report, never a reason to go
        # quiet - that confusion is how a loop spends weeks looking like a quiet board.
        if (-not $wake.Known) { Say '  running anyway so the agent can report' }
        if (-not $wake.Work) {
            Finish 'RESULT: SKIPPED - nothing to do for claude (no Ready task, nothing unanswered, no lane work)'
        }
        # The keys that actually woke this tick, captured VERBATIM, written down before anything
        # that could kill the run. Never recomputed afterwards: the run will have answered things by
        # then, and the difference is exactly the items that would be muted unseen.
        if (Save-WakeJournal -JournalPath $WakeJournal -OwnerPid $PID -ListId $wake.ListId -WakeKeys $wake.WakeKeys) {
            Say "  WAKE: $(@($wake.WakeKeys).Count) key(s) journalled, to be acknowledged by this run's outcome"
        }
    }

    # ── Can this machine CALL the board, and PUBLISH what it finishes? ────────────────────────
    # WARNS, never skips. A missing mcp__astrid__* grant leaves the run working - it falls back to
    # the OAuth scripts - but deaf to `attention`, while still logging RESULT: OK. That silence is
    # the bug; this is the noise. Degraded beats absent, so it is not a guard.
    Push-Location $RepoRoot
    try { $permissionOut = & $Tsx (Join-Path $WebRepo 'scripts\check-board-permissions.ts') 2>&1 } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) {
        Say '  WARNING: permissions on this machine will degrade this run:'
        $permissionOut | ForEach-Object { Say "     $_" }
        # The checker is astrid-web's and looks for ITS layout, a gitignored settings.local.json.
        # This repo grants those nine tools in a COMMITTED .claude/settings.json instead, so the
        # "missing" list overstates it. What is true either way is the consequence it names: with no
        # astrid MCP server registered on this machine, the run reads the board through astrid-web's
        # OAuth scripts and cannot see `attention`.
        Say '     (this repo grants those tools in the committed .claude/settings.json; what the'
        Say '      run really lacks is a registered astrid MCP server, hence the scripts fallback)'
    }

    if ($DryRun) { Finish 'RESULT: SKIPPED - dry run, every guard passed and Claude was not started' }

    # ── The run ──────────────────────────────────────────────────────────────────────────────
    # --add-dir is what makes the board reachable. Where no astrid MCP server is registered - the
    # case on this machine - the documented fallback is astrid-web's OAuth scripts, and a session
    # confined to this repo cannot run them: it refuses the sibling checkout outright, whatever the
    # permission grants say. Proved by the first real run on 2026-09-26, which reported "this
    # session's allowed working directory is astrid-windows only" and worked nothing.
    #
    # astrid-core is here for the same reason, one layer in. CLAUDE.md and .claude/commands/fixall.md
    # both say a rule, a service, the Outbox or sync is fixed in a checkout of astrid-core beside
    # this one and reaches this app when the pin is bumped - and a session that cannot open that
    # checkout cannot do it, or even READ the core to tell whether the fix belongs there. It cannot
    # reach the sources through the cargo git checkout either: ~/.cargo is outside the allowed
    # directories too. Found on 2026-09-26, when all four Ready tasks turned out to bottom out in
    # core rules and the run could not read one line of them.
    $claudeArgs = @('-p', '/fixall', '--model', $Model, '--permission-mode', $PermissionMode,
        '--add-dir', $WebRepo)
    # Only when it is actually there. A missing core checkout is a degraded run, not a failed one:
    # a shell-only task is still workable, and --add-dir on a path that does not exist would take
    # the whole tick down with it. Loud, like the permissions warning above, because the failure it
    # replaces is the silent kind - a run that reports OK having worked nothing.
    if (Test-Path $CoreRepo) {
        $claudeArgs += @('--add-dir', $CoreRepo)
    } else {
        Say "  WARNING: no astrid-core checkout at $CoreRepo - any task whose fix is in the core"
        Say '     cannot be worked or even diagnosed this run. git clone it beside this repo.'
    }
    if ($MaxUsd) { $claudeArgs += @('--max-budget-usd', $MaxUsd) }
    $runLine = '-> /fixall (' + $Model + ', watchdog ' + $MaxMinutes + 'm'
    if ($MaxUsd) { $runLine += ', cap $' + $MaxUsd }
    Say ($runLine + ')')

    # Redirected to a file rather than inherited: Task Scheduler captures no console, and appending
    # the file afterwards keeps the RESULT line last, which the header promises.
    $runOut = Join-Path $env:TEMP "astrid-fixall-windows-$PID.out"
    $runErr = Join-Path $env:TEMP "astrid-fixall-windows-$PID.err"
    $proc = Start-Process -FilePath $ClaudeCmd -ArgumentList $claudeArgs `
        -WorkingDirectory $RepoRoot -NoNewWindow -PassThru `
        -RedirectStandardOutput $runOut -RedirectStandardError $runErr
    # Touch the handle while the process is alive: without it .ExitCode reads as $null after exit,
    # and every run would report FAILED.
    $null = $proc.Handle

    $killed = $false
    if (-not $proc.WaitForExit($MaxMinutes * 60 * 1000)) {
        # launchd's lesson, and Task Scheduler's too: one wedged run swallows every later tick,
        # because a second copy of the task is never started while the first is alive.
        $killed = $true
        try { $proc.Kill($true) } catch { try { $proc.Kill() } catch { } }
        $proc.WaitForExit(30 * 1000) | Out-Null
    }
    $status = $proc.ExitCode

    $runText = @()
    foreach ($file in @($runOut, $runErr)) {
        if (Test-Path $file) {
            $lines = @(Get-Content -Path $file -Encoding utf8)
            $runText += $lines
            $lines | ForEach-Object { Add-Content -Path $LogFile -Value $_ -Encoding utf8 }
            Remove-Item $file -Force -ErrorAction SilentlyContinue
        }
    }

    # ── Leave the checkout where the next tick can use it ────────────────────────────────────
    # Before any RESULT line, because this is the difference between one bad tick and a loop that is
    # wedged until somebody notices. Guard 2 above refuses a dirty tree and a non-main HEAD - rightly,
    # since it cannot tell a died run's leftovers from somebody's work in progress. THIS script can:
    # it held the lock, it started the child, and the child is gone.
    $saved = Save-UnfinishedWork -RepoRoot $RepoRoot -ClaudeExitCode $status -Log { param($Line) Say $Line }

    # And the core, for the same reason one layer out. The start-of-tick sweep would reach it a tick
    # or two later, but the run that just exited is the one that was writing in there and this script
    # watched it go - so there is no reason to leave a core task's work invisible until then. The
    # quiet window does not apply here (the files were touched seconds ago, by our own child); the
    # LIVE-HOLDER check still does, because the core clone is shared with the other repos' loops.
    $coreStatus = Get-CheckoutStatus -Path $CoreRepo -Name 'astrid-core'
    if ($coreStatus.Exists -and $coreStatus.HasWork) {
        $coreHolder = Get-SessionHolder $CoreRepo
        if ($coreHolder -and $coreHolder.Live) {
            Say "  astrid-core has leftovers but $($coreHolder.Harness) (pid $($coreHolder.HolderPid)) is live in it - leaving them"
        }
        else {
            # ReturnToMain false: pushing touches neither HEAD nor the working tree of a shared clone,
            # `git checkout main` moves both for everything else in there.
            $savedCore = Save-UnfinishedWork -RepoRoot $CoreRepo -ClaudeExitCode $status -ReturnToMain $false `
                -Log { param($Line) Say $Line }
            $coreNote = Format-SavedNote $savedCore
            if ($coreNote -ne 'nothing was pushed by this run') {
                # Appended, not assigned: a tick can both sweep a previous run's leftovers at the
                # start and leave some of its own in the core at the end, and the RESULT line is the
                # only place either is visible.
                if ($script:SweepNote) { $script:SweepNote += ' | ' }
                $script:SweepNote += "in astrid-core, $coreNote"
            }
        }
    }

    # ── Anything this run claimed and did not finish goes back to Ready ──────────────────────
    # A finished task is completed, a blocked one is in Waiting: anything still in `Doing` was
    # abandoned mid-task. AFTER the save above, which is what pushes the branch — the release
    # comments with the branch that carries the task's id, so that branch has to exist first.
    #
    # AND BEFORE THE WATCHDOG BRANCH BELOW, which ends the tick: a killed run is the likeliest way a
    # claim is abandoned, so a release placed after that Finish would never run in the case it exists
    # for. Both failure paths below reach this line.
    $claimsReleased = Invoke-DoingRelease -ClaimsFile $claimsFile

    # ── And what woke this tick is acknowledged by how it ended ──────────────────────────────
    # Phase two. BEFORE every Finish below, because a run that died is the case this exists for: a
    # failed run gives each key a strike instead of muting it, so an item this run could not get to
    # wakes the next tick and the one after, and is muted only after MAX_FAILED_ATTEMPTS.
    #
    # Exactly the journal's keys, which are the preflight's. A delivery that fails KEEPS the journal
    # and says so - the next tick replays it - because a journal dropped on a failed delivery is an
    # item muted that nobody ever saw.
    $wakeAcked = Invoke-WakeAck -JournalPath $WakeJournal -Failed ($killed -or $status -ne 0) -Ack $WakeAck
    if ($wakeAcked.Summary) { Say "  $($wakeAcked.Summary)" }

    if ($killed) {
        $reason = "killed after the ${MaxMinutes}m watchdog timeout"
        Send-DiedRunNote -TaskFile $taskFile -Reason $reason -Saved $saved
        Finish "RESULT: FAILED - $reason; $(Format-SavedNote $saved)"
    }

    # A run whose grants were ignored EXITS ZERO. It reads the board, decides it can change nothing,
    # writes a tidy explanation nobody is watching, and the loop logs RESULT: OK - which is the
    # quiet degradation every guard above exists to prevent, arriving at the last possible moment.
    # The CLI names the condition precisely, so the loop reads its own child's words for it rather
    # than guessing. Both spellings of this repo's path are trusted in ~/.claude.json; this catches
    # the day that is undone, or a fresh checkout nobody has trusted yet.
    $untrusted = @($runText | Where-Object { $_ -match 'has not been trusted' -or $_ -match 'Ignoring \d+ permissions\.allow' })
    if ($untrusted.Count -gt 0) {
        Say '  the CLI said:'
        $untrusted | Select-Object -First 2 | ForEach-Object { Say "     $_" }
        Finish 'RESULT: FAILED - this workspace is not trusted, so .claude/settings.json was ignored and the run could change nothing; run claude interactively here once and accept the trust dialog, or set projects[<this repo>].hasTrustDialogAccepted = true in ~/.claude.json'
    }

    if ($status -eq 0) { Finish 'RESULT: OK - run finished (see the tasks for what changed)' }

    # A run that died cannot write its own completion comment, and this is precisely the outcome
    # worth hearing about, so the wrapper says it on the task itself.
    #
    # WHY THE OUTPUT IS READ FIRST. This line used to append "the budget cap is the usual cause" to
    # every exit 1, and on 2026-10-07 it said that twice while the line above read "Failed to
    # authenticate: OAuth session expired" — a run that cannot sign in exits in seconds and spends
    # nothing, so the verdict sent the reader after the wrong thing. Same discipline as the
    # untrusted check above: read the child's words, and guess only where there is nothing to read
    # (task 73aa3492).
    $verdict = Resolve-RunFailure -Output $runText -Status $status -MaxUsd $MaxUsd
    if ($verdict.Kind -ne 'unknown') {
        Say '  the CLI said:'
        @($runText | Where-Object { $_ -match 'Failed to authenticate|OAuth session expired|Exceeded USD budget' }) |
            Select-Object -First 2 | ForEach-Object { Say "     $_" }
    }
    Send-DiedRunNote -TaskFile $taskFile -Reason $verdict.Reason -Saved $saved
    Finish "RESULT: FAILED - $($verdict.Reason); $(Format-SavedNote $saved)"
}
finally {
    if ($taskFile) { Remove-Item $taskFile -Force -ErrorAction SilentlyContinue }
    # The preflight artifact is this tick's alone and has been read; the JOURNAL is deliberately not
    # touched here - if its acknowledgement did not get through, it is the next tick's retry.
    if ($WakeArtifact) { Remove-Item $WakeArtifact -Force -ErrorAction SilentlyContinue }
    # ONLY when the release got through. A task file is deleted either way — it has been reported on
    # by then — but a claims file whose release failed is the only record of which tasks are sitting
    # in Doing, and the next tick's start-of-tick pass is what retries it. Deleting it here would
    # strand them exactly as before task 1b9fed0a.
    if ($claimsFile -and $claimsReleased) { Remove-Item $claimsFile -Force -ErrorAction SilentlyContinue }
    & $releaseLock
}
