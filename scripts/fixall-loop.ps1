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
      * The board is resolved BY NAME by `ready-tasks.ts windows`, never by a hardcoded id. An id
        is account data and a stale one fails by answering "nothing to do" (docs/AUTOMATION.md).

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

    WHAT THIS DOES NOT DO, stated because the Mac loop does it. astrid-web's guard 3 calls
    `agent-queue-status.ts`, which also reports `attention` - comments and chat nobody has
    answered - and keeps a seen-file so one unanswered item wakes one run rather than every tick.
    That script requires a LIST ID, and this board's id is deliberately nowhere in this repo, so
    the guard here uses `ready-tasks.ts windows --json` instead: the queue and the lane sweep, both
    resolved by name. The inbox is therefore not a reason this loop wakes. Adopt the richer
    preflight the day it can resolve a board by name.

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
    [int]$MaxMinutes = $(if ($env:FIXALL_MAX_MINUTES) { [int]$env:FIXALL_MAX_MINUTES } else { 50 }),
    [string]$MaxUsd = $(if ($null -ne $env:FIXALL_MAX_USD) { $env:FIXALL_MAX_USD } else { '10' }),
    [string]$PermissionMode = $(if ($env:FIXALL_PERMISSION_MODE) { $env:FIXALL_PERMISSION_MODE } else { 'acceptEdits' })
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

# Everything this script says goes to the console AND to the log, because Task Scheduler captures
# neither. Native command output is appended separately, where it is produced.
function Say([string]$Line) {
    Write-Host $Line
    Add-Content -Path $LogFile -Value $Line -Encoding utf8
}

function Finish([string]$Result) {
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

# ── Guard 1: one session per working tree ─────────────────────────────────────────────────────
# Cheap by design: no Claude session is started just to discover the tree is busy. The lock is
# keyed to the git directory, so this tree and astrid-web's never see each other, and a lock whose
# holder is gone is reclaimed by liveness rather than expiring under a working session. Run from
# THIS repo, which is what makes it this repo's lock.
Push-Location $RepoRoot
try { & $Tsx (Join-Path $WebRepo 'scripts\fixall-session.ts') 'acquire' '--pid' $PID '--harness' 'claude-code' 2>&1 | ForEach-Object { Say "  $_" } }
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

    # ── Guard 2: never clobber work in progress ───────────────────────────────────────────────
    # /fixstuff and an interactive session take no lock, so they are invisible to guard 1. This is
    # the only thing between a half-hourly tick and somebody's uncommitted work.
    if (-not $Force) {
        Push-Location $RepoRoot
        try {
            $dirty = (& git status --porcelain 2>$null) -join "`n"
            $branch = (& git rev-parse --abbrev-ref HEAD 2>$null)
        } finally { Pop-Location }
        if ($dirty) { Finish 'RESULT: SKIPPED - working tree is dirty, leaving it alone' }
        if ($branch -ne 'main') { Finish "RESULT: SKIPPED - HEAD is on $branch, not main" }
    }

    # ── Guard 3: is there actually any work? ──────────────────────────────────────────────────
    # THE expensive question, asked the cheap way. Without this a quiet tick still boots a whole
    # session - CLAUDE.md, fixall.md, the tool schemas - to ask the queue once and be told it is
    # empty. A few HTTP requests answer the same question.
    #
    # `ready-tasks.ts windows` also SWEEPS the lanes first, so a Waiting task whose date has
    # arrived is Ready by the time the answer is counted, and RECHECK/REVIEW items count as work.
    # Both are things this loop must wake for.
    #
    # A non-zero exit means "could not tell" - network, auth - and must NOT read as empty: a queue
    # nobody can see is a reason to run and let the agent report properly, not a reason to go quiet
    # forever.
    if (-not $IgnoreQueue) {
        $queueOut = Invoke-WebTool @('scripts/ready-tasks.ts', 'windows', '--json', '--harness', 'claude-code')
        $queueStatus = $LASTEXITCODE
        $queueJson = ($queueOut | Select-String -Pattern '^\s*\{.*"tasks"' | Select-Object -Last 1).ToString()
        if ($queueStatus -ne 0) {
            Say "  QUEUE: unknown - ready-tasks.ts exited $queueStatus; running anyway so the agent can report"
        }
        elseif (-not $queueJson) {
            Say '  QUEUE: unknown - no JSON verdict; running anyway so the agent can report'
        }
        else {
            $tasks = @()
            try { $tasks = @((ConvertFrom-Json $queueJson).tasks) } catch { $tasks = @() }
            $actions = ($tasks | ForEach-Object { $_.action }) -join ', '
            $queueLine = '  QUEUE: ' + $tasks.Count + ' item(s)'
            if ($actions) { $queueLine += ' - ' + $actions }
            Say $queueLine
            if ($tasks.Count -eq 0) {
                Finish 'RESULT: SKIPPED - nothing to do for claude (no Ready task, no lane work)'
            }
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

    if ($killed) { Finish "RESULT: FAILED - killed after the ${MaxMinutes}m watchdog timeout; nothing was pushed by this run" }

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
    Finish "RESULT: FAILED - claude exited $status; nothing was pushed by this run"
}
finally {
    & $releaseLock
}
