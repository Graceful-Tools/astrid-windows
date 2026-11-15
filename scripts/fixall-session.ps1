#Requires -Version 5.1
<#
.SYNOPSIS
    Take, release or inspect the /fixall lock on THIS working tree, from an in-session run.
    Task f7ee5e57.

.DESCRIPTION
    THE LOCK ITSELF IS astrid-web's, and deliberately: `scripts/fixall-session.ts` is the one
    implementation, shared by every repo and every harness, keyed on process LIVENESS rather than
    on any age limit. This script is only a correct way to CALL it from here, because calling it
    the way FIXALL_WORKFLOW.md documents goes wrong on Windows in two silent ways.

    1. IT WOULD LOCK THE WRONG REPOSITORY. `fixall-session.ts` takes no --repo. It keys the lock
       on `git rev-parse --absolute-git-dir` of the CURRENT DIRECTORY, which is what makes the
       lock per working tree. So the documented invocation - `cd ../astrid-web` and then run the
       script from there - locks astrid-web and leaves THIS checkout unprotected, while printing a
       cheerful "Acquired". That is the one failure mode indistinguishable from success, and it is
       what happened on the first attempt at this task. Hence Push-Location $RepoRoot: the cwd is
       pinned to this repo for the duration of the call, exactly as fixall-loop.ps1 does it.

    2. $PPID IS 1 IN THIS HARNESS'S BASH TOOL. FIXALL_WORKFLOW.md says to pass $PPID and that in
       an agent's shell it is the harness session - verified, but on the Mac. Here the Bash tool's
       shell reports a parent of 1, so the lock would record a pid that is not the session, and
       liveness - the entire basis on which a stale lock is reclaimed - becomes meaningless. Nor
       can the shell's own pid be used: this harness starts a fresh shell per call, so it is dead
       before the next one. The ancestry walk is what works, and it is robust when several
       sessions are open because it finds THIS one: powershell -> cmd -> claude.exe.

    THE LOOP ALREADY HOLDS THE LOCK FOR ITS OWN CHILD. fixall-loop.ps1 acquires it with the
    PowerShell process's pid, which the agent cannot see, and exports ASTRID_FIXALL_LOCK_HELD=1 so
    that adopting the lock in-session cannot deadlock against the launcher. A naive acquire from
    inside `claude -p` would be refused with 2 - the agent blocked by the very loop that started
    it - and a naive RELEASE would be worse: it would drop the loop's lock mid-tick and invite the
    next tick straight into this checkout. So both are skipped when that variable is set.

    EXIT CODES ARE THE INTERFACE, passed through from the shared script:
      0  the tree is yours (or the loop holds it for you)
      2  another LIVE session holds it - stop, and do not read the queue
      1  something went wrong

    2 rather than 1 for "held" mirrors claim-fixall-task.ts, where 2 is likewise "someone else got
    there first, skip without complaining". Collapsing it would make a busy tree look like a
    broken call.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/fixall-session.ps1 acquire

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/fixall-session.ps1 status
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('acquire', 'release', 'status')]
    [string]$Action = 'acquire',

    # Escape hatch for a harness whose process is not named claude*, and for the tests. When this
    # is 0 the pid is resolved by walking this process's ancestry.
    [int]$HarnessPid = 0,

    [string]$Harness = 'claude-code'
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$WebRepo = Join-Path (Split-Path -Parent $RepoRoot) 'astrid-web'
$Tsx = Join-Path $WebRepo 'node_modules\.bin\tsx.cmd'
$SessionScript = Join-Path $WebRepo 'scripts\fixall-session.ts'

# ── The loop's lock is the loop's to hold and to drop ─────────────────────────────────────────
if ($env:ASTRID_FIXALL_LOCK_HELD -eq '1') {
    if ($Action -eq 'status') {
        Write-Host 'The scheduled loop holds this tree for this run (ASTRID_FIXALL_LOCK_HELD=1).'
    }
    else {
        Write-Host "Skipped $Action - the scheduled loop that started this run already holds the"
        Write-Host '  lock on this tree, with a pid this run cannot see. Nothing to do.'
    }
    exit 0
}

if (-not (Test-Path $Tsx)) {
    Write-Host "FAILED - no tsx at $Tsx" -ForegroundColor Red
    Write-Host '  The lock lives in astrid-web. Check it out beside this repo and run npm ci there.'
    exit 1
}
if (-not (Test-Path $SessionScript)) {
    Write-Host "FAILED - no lock script at $SessionScript" -ForegroundColor Red
    exit 1
}

# ── Which process should the lock name? ───────────────────────────────────────────────────────
# The one that outlives this script and dies with the session, so the lock clears itself and a
# dead holder is reclaimed by liveness. That is the harness, not this PowerShell and not the
# agent's per-call shell.
function Resolve-HarnessPid {
    $current = $PID
    $hops = 0
    while ($current -gt 0 -and $hops -lt 16) {
        $hops++
        $proc = $null
        try { $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$current" -ErrorAction Stop }
        catch { return 0 }
        if (-not $proc) { return 0 }
        if ($proc.Name -match '(?i)^claude') { return [int]$proc.ProcessId }
        $current = [int]$proc.ParentProcessId
    }
    return 0
}

$pidToUse = $HarnessPid
if ($pidToUse -le 0 -and $Action -ne 'status') {
    $pidToUse = Resolve-HarnessPid
    if ($pidToUse -le 0) {
        Write-Host 'FAILED - no harness process found in this process ancestry.' -ForegroundColor Red
        Write-Host '  Pass it explicitly: -HarnessPid <pid of the agent session>.'
        exit 1
    }
}

# ── Call the shared script, with the cwd pinned to THIS repo ──────────────────────────────────
$sessionArgs = @($SessionScript, $Action)
if ($Action -ne 'status') { $sessionArgs += @('--pid', [string]$pidToUse, '--harness', $Harness) }

Push-Location $RepoRoot
try { & $Tsx @sessionArgs }
finally { Pop-Location }

$code = $LASTEXITCODE
if ($null -eq $code) { $code = 0 }

if ($code -eq 2) {
    # The shared script has already printed who holds it and what to do about it.
    exit 2
}
exit $code
