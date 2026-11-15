#Requires -Version 5.1
<#
.SYNOPSIS
    The loop's task and claims records must be DURABLE and scoped to the checkout that wrote them,
    and the records already in %TEMP% must be drained rather than abandoned. Task 18ba8c2e, scope
    item 2.

.DESCRIPTION
    `ASTRID_FIXALL_TASK_FILE` and `ASTRID_FIXALL_CLAIMS_FILE` are the only two things a tick leaves
    behind for a LATER tick to read, and that is the whole of their purpose:

      * a surviving task file is how the next tick knows a run died without writing a `RESULT:`
        line, and which task to say so on — the loop cannot guess, and a comment on the wrong task
        is worse than none;
      * a surviving claims file is the only record of which tasks that run left sitting in `Doing`.
        Nothing else can see them: the queue never takes `Doing` on purpose, and the after-run pass
        that would have released them is exactly what a hard kill stops.

    THREE FACTS ABOUT THEM, AND THE FIRST TWO PULL IN OPPOSITE DIRECTIONS.

    1. %TEMP% IS NOT DURABLE. Storage Sense and every disk-cleanup run sweep it, so the one record
       of an abandoned claim can be deleted by Windows housekeeping between two ticks — and the
       cost of that is invisible and permanent: a task stranded in `Doing` that no later pass can
       see. `f8dfc84e` sat there from 01:25 on 2026-10-18 until a human released it by hand. The
       wake journal item 1 added is under `%LOCALAPPDATA%\Astrid\state\fixall-windows` for this
       reason; these two belong beside it, and the directory is computed in ONE place so they
       cannot drift apart.

    2. A DURABLE DIRECTORY IS SHARED, SO IT HAS TO BE SCOPED. `%LOCALAPPDATA%` is one directory per
       USER for every checkout on the machine, while `%TEMP%` at least turned over. A pid is not
       unique across checkouts — Windows reuses them freely — so a record found there says nothing
       about which worker wrote it. It matters because a release names the branch carrying the
       task's id and runs with `--repo` pointed at THIS checkout: a record drained from another
       worktree would be released against the wrong repository. So the names carry a WORKER SCOPE,
       a short stable digest of the checkout's own path, and a scan ignores any scoped record that
       is not this worker's. Another worker sweeps its own.

    3. THE LEGACY RECORDS ARE DRAINED, NOT ABANDONED. The tick that upgrades is a tick whose
       predecessors' files are all in the old place under the old names. A scan that looked only in
       the new directory would make every one of them invisible on the first tick after the change
       — which is precisely the failure the claims file exists to prevent, delivered by the fix for
       it. So the scans take the durable directory AND a legacy one, match the old unscoped name as
       well, act on it identically, and delete it once it is handled. Legacy records carry no scope
       by construction, so they are drained whatever this worker's scope is: that is what the old
       behaviour already was.

    AND NOTHING ELSE MOVES. The pause/yield stand-down, the working-tree lock and its liveness
    check, the WIP save in both checkouts, the died-run note on the task, and the bounded `Doing`
    release on the board are all asserted here as still wired, because "move two file paths" is
    exactly the kind of change that quietly takes one of them with it.

    WHY THE LOGIC IS IN A LIBRARY, as everywhere else in this suite: the loop cannot be run inside
    the gate — it takes a machine-wide lock and starts a real Claude session — so the decisions live
    in `scripts/lib/fixall-sweep.ps1` behind injected closures and are exercised here against
    scratch directories, while the loop's WIRING is asserted over the parsed AST.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-state.Tests.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibPath = Join-Path $RepoRoot 'scripts\lib\fixall-sweep.ps1'
$LoopPath = Join-Path $RepoRoot 'scripts\fixall-loop.ps1'
$AutomationPath = Join-Path $RepoRoot 'docs\AUTOMATION.md'

foreach ($required in @($LibPath, $LoopPath, $AutomationPath)) {
    if (-not (Test-Path $required)) {
        Write-Host "FAIL: missing $required" -ForegroundColor Red
        exit 1
    }
}

. $LibPath

$script:Failures = 0
$script:Ran = 0
# Assertions MADE, not passed: a case that returns early makes none, and a vacuous OK in the gate
# that guards the scheduled loop is the exact shape of bug this suite is about.
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

function Assert-NotMatch([string]$What, [string]$Pattern, [string]$Actual) {
    $script:Checks++
    if ($Actual -notmatch $Pattern) { return }
    Write-Host "    FAIL $What" -ForegroundColor Red
    Write-Host "      expected NOT to match: [$Pattern]" -ForegroundColor Red
    Write-Host "      actual:                [$Actual]" -ForegroundColor Red
    $script:Failures++
}

function Assert-True([string]$What, $Actual) { Assert-Equal $What 'True' ([bool]$Actual) }
function Assert-False([string]$What, $Actual) { Assert-Equal $What 'False' ([bool]$Actual) }

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

# A scratch directory per case: these cases write and DELETE state records, and a case that read a
# previous case's leftovers would pass for the wrong reason.
function New-Scratch {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ('astrid-state-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

# A record in the NEW shape: under the durable directory, carrying the worker scope and the pid.
function New-DurableRecord([string]$Directory, [string]$Kind, [string]$Scope, [int]$OwnerPid, [string[]]$Lines) {
    $path = Join-Path $Directory "$Kind-$Scope-$OwnerPid.txt"
    Set-Content -Path $path -Value $Lines -Encoding utf8
    return $path
}

# A record in the OLD shape, which is what every tick before this change left behind.
function New-LegacyRecord([string]$Directory, [string]$Kind, [int]$OwnerPid, [string[]]$Lines) {
    $path = Join-Path $Directory "astrid-fixall-windows-$Kind-$OwnerPid.txt"
    Set-Content -Path $path -Value $Lines -Encoding utf8
    return $path
}

$Dead = { param($OwnerPid) $false }

Write-Host ''
Write-Host 'the loop s task and claims state is durable and checkout-scoped (task 18ba8c2e)' -ForegroundColor Cyan

# ── Where the state lives ─────────────────────────────────────────────────────────────────────

Invoke-Case 'the state directory is under LOCALAPPDATA, beside the log and the wake journal' {
    $scratch = New-Scratch
    try {
        $dir = Get-FixallStateDir -Root $scratch
        Assert-Equal 'it is Astrid\state\fixall-windows under the root' (Join-Path $scratch 'Astrid\state\fixall-windows') $dir
        Assert-True 'and it is created, because a tick writes into it before anything reads it' (Test-Path $dir)
        # Asking twice must not fail on the directory already being there.
        $again = Get-FixallStateDir -Root $scratch
        Assert-Equal 'asking twice answers the same path' $dir $again
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'and its default root is LOCALAPPDATA rather than TEMP' {
    # The point of the move. %TEMP% is swept by Windows housekeeping, and these two records are read
    # by a LATER tick by definition.
    $dir = Get-FixallStateDir -Root $env:LOCALAPPDATA
    Assert-Equal 'the default is the same as naming LOCALAPPDATA' $dir (Get-FixallStateDir)
    Assert-Match 'it is under LOCALAPPDATA' ([regex]::Escape($env:LOCALAPPDATA)) $dir
    Assert-NotMatch 'and not under TEMP' '\\Temp\\' $dir
}

Invoke-Case 'a state file is named for its kind, its worker and its owning pid' {
    $scope = Get-FixallWorkerScope 'C:\checkouts\astrid-windows'
    # A real drive, because PowerShell's Join-Path resolves one and refuses a letter the machine
    # does not have. The directory itself need not exist - naming a record does not create it.
    $dir = Join-Path $env:SystemDrive 'astrid-state-naming'
    $task = Get-FixallStateFile -Directory $dir -Kind 'task' -Scope $scope -OwnerPid 4242
    $claims = Get-FixallStateFile -Directory $dir -Kind 'claims' -Scope $scope -OwnerPid 4242
    Assert-Equal 'the task record' (Join-Path $dir "task-$scope-4242.txt") $task
    Assert-Equal 'the claims record' (Join-Path $dir "claims-$scope-4242.txt") $claims
    Assert-True 'and the two are different files' ($task -ne $claims)
}

# ── The worker scope ──────────────────────────────────────────────────────────────────────────

Invoke-Case 'the worker scope is a stable short digest of the checkout path' {
    $a = Get-FixallWorkerScope 'C:\Users\jonpa\mycode\astrid-dev\astrid-windows'
    $again = Get-FixallWorkerScope 'C:\Users\jonpa\mycode\astrid-dev\astrid-windows'
    Assert-Equal 'the same path answers the same scope' $a $again
    # Short and filename-safe, because it goes in a filename and is read by eye in a log line.
    Assert-Match 'it is eight lowercase hex characters' '^[0-9a-f]{8}$' $a
}

Invoke-Case 'two checkouts of this repo get different scopes' {
    # THE REASON THE SCOPE EXISTS. One %LOCALAPPDATA% per user, several worktrees: a pid alone says
    # nothing about which one wrote the record, and a release runs against THIS repo's branches.
    $a = Get-FixallWorkerScope 'C:\Users\jonpa\mycode\astrid-dev\astrid-windows'
    $b = Get-FixallWorkerScope 'C:\Users\jonpa\mycode\astrid-dev\astrid-windows-2'
    Assert-True 'the scopes differ' ($a -ne $b)
    Assert-Match 'and the second is still filename-safe' '^[0-9a-f]{8}$' $b
}

Invoke-Case 'the same checkout spelled differently is the same worker' {
    # Windows paths are case-insensitive and the loop is handed its root by Task Scheduler, by an
    # interactive shell and by this suite - three spellings of one directory. A scope that changed
    # with the spelling would make a tick blind to its own predecessor's records.
    $plain = Get-FixallWorkerScope 'C:\Users\jonpa\mycode\astrid-dev\astrid-windows'
    Assert-Equal 'a trailing separator does not change it' $plain (Get-FixallWorkerScope 'C:\Users\jonpa\mycode\astrid-dev\astrid-windows\')
    Assert-Equal 'nor does case' $plain (Get-FixallWorkerScope 'c:\users\jonpa\mycode\astrid-dev\astrid-windows')
    Assert-Equal 'nor a forward-slash spelling' $plain (Get-FixallWorkerScope 'C:/Users/jonpa/mycode/astrid-dev/astrid-windows')
}

# ── Task records: durable, scoped, and the legacy ones drained ────────────────────────────────

Invoke-Case 'a dead run s durable task record is found, with the task it had taken' {
    $scratch = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        New-DurableRecord $scratch 'task' $scope 424242 @('aaaaaaaa-0000-0000-0000-000000000001') | Out-Null
        $found = @(Get-StrandedTaskFile -Directory $scratch -Scope $scope -SelfPid 1 -IsAlive $Dead)
        Assert-Equal 'exactly one record' 1 $found.Count
        if ($found.Count -ne 1) { return }
        Assert-Equal 'the task id' 'aaaaaaaa-0000-0000-0000-000000000001' $found[0].TaskId
        Assert-Equal 'and the pid that owned it' 424242 $found[0].HolderPid
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'another worker s task record is left strictly alone' {
    $scratch = New-Scratch
    try {
        $mine = Get-FixallWorkerScope 'C:\checkouts\mine'
        $theirs = Get-FixallWorkerScope 'C:\checkouts\theirs'
        $foreign = New-DurableRecord $scratch 'task' $theirs 424242 @('bbbbbbbb-0000-0000-0000-000000000002')
        $found = @(Get-StrandedTaskFile -Directory $scratch -Scope $mine -SelfPid 1 -IsAlive $Dead)
        Assert-Equal 'it is not this worker s to report' 0 $found.Count
        # NOT DELETED EITHER. The worker that wrote it is the one that sweeps it; deleting another
        # checkout's record would strand its task exactly as %TEMP% housekeeping would.
        Assert-True 'and it is still on disk for its own worker' (Test-Path $foreign)
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a live run s task record is left alone, in either location' {
    $scratch = New-Scratch
    $legacy = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        $durable = New-DurableRecord $scratch 'task' $scope 525252 @('cccccccc-0000-0000-0000-000000000003')
        $old = New-LegacyRecord $legacy 'task' 626262 @('dddddddd-0000-0000-0000-000000000004')
        $found = @(Get-StrandedTaskFile -Directory $scratch -LegacyDirectory $legacy -Scope $scope -SelfPid 1 `
                -IsAlive { param($OwnerPid) $true })
        Assert-Equal 'nothing is reported' 0 $found.Count
        Assert-True 'the durable record survives' (Test-Path $durable)
        Assert-True 'and so does the legacy one' (Test-Path $old)
    }
    finally {
        Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $legacy -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-Case 'this tick s own task record is never treated as stranded' {
    $scratch = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        New-DurableRecord $scratch 'task' $scope 777 @('eeeeeeee-0000-0000-0000-000000000005') | Out-Null
        $found = @(Get-StrandedTaskFile -Directory $scratch -Scope $scope -SelfPid 777 -IsAlive $Dead)
        Assert-Equal 'the pid in the name is this tick s' 0 $found.Count
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a legacy TEMP task record is still found and still carries its task' {
    # Requirement 3: the tick that upgrades inherits a %TEMP% full of its predecessors' records.
    $scratch = New-Scratch
    $legacy = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        New-LegacyRecord $legacy 'task' 828282 @('ffffffff-0000-0000-0000-000000000006') | Out-Null
        $found = @(Get-StrandedTaskFile -Directory $scratch -LegacyDirectory $legacy -Scope $scope -SelfPid 1 -IsAlive $Dead)
        Assert-Equal 'the old record is drained' 1 $found.Count
        if ($found.Count -ne 1) { return }
        Assert-Equal 'with its task id' 'ffffffff-0000-0000-0000-000000000006' $found[0].TaskId
        Assert-Equal 'and its pid' 828282 $found[0].HolderPid
    }
    finally {
        Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $legacy -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-Case 'a legacy record is drained whatever this worker s scope is' {
    # An old name carries no scope, so there is nothing to match it against. Skipping it would be
    # the stranding this change exists to prevent; the old behaviour was unscoped already.
    $legacy = New-Scratch
    $durable = New-Scratch
    try {
        New-LegacyRecord $legacy 'task' 929292 @('11111111-0000-0000-0000-000000000007') | Out-Null
        foreach ($scope in @((Get-FixallWorkerScope 'C:\one'), (Get-FixallWorkerScope 'D:\another'))) {
            $found = @(Get-StrandedTaskFile -Directory $durable -LegacyDirectory $legacy -Scope $scope -SelfPid 1 -IsAlive $Dead)
            Assert-Equal "scope $scope drains it" 1 $found.Count
        }
    }
    finally {
        Remove-Item $legacy -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $durable -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-Case 'an empty task record is cleared rather than re-read every tick' {
    $scratch = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        $empty = New-DurableRecord $scratch 'task' $scope 434343 @('')
        $found = @(Get-StrandedTaskFile -Directory $scratch -Scope $scope -SelfPid 1 -IsAlive $Dead)
        Assert-Equal 'a run that was killed before it took anything has nothing to report' 0 $found.Count
        Assert-False 'and its record is removed' (Test-Path $empty)
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a missing directory is not an error' {
    # The first tick on a machine, and a %TEMP% that never had one of these in it.
    $found = @(Get-StrandedTaskFile -Directory (Join-Path ([System.IO.Path]::GetTempPath()) 'no-such-state-dir') `
            -LegacyDirectory (Join-Path ([System.IO.Path]::GetTempPath()) 'no-such-temp-dir') `
            -Scope (Get-FixallWorkerScope 'C:\one') -SelfPid 1 -IsAlive $Dead)
    Assert-Equal 'nothing is found, and nothing throws' 0 $found.Count
}

# ── Claims records: the same rules, because they have the same job ────────────────────────────

Invoke-Case 'a dead run s durable claims record is found, with every id in it' {
    $scratch = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        New-DurableRecord $scratch 'claims' $scope 515151 @(
            '22222222-0000-0000-0000-000000000008',
            '33333333-0000-0000-0000-000000000009') | Out-Null
        $found = @(Get-StrandedClaimsFile -Directory $scratch -Scope $scope -SelfPid 1 -IsAlive $Dead)
        Assert-Equal 'one record' 1 $found.Count
        if ($found.Count -ne 1) { return }
        Assert-Equal 'both claims' 2 @($found[0].TaskIds).Count
        Assert-Equal 'the owning pid' 515151 $found[0].HolderPid
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'another worker s claims record is neither released nor deleted' {
    $scratch = New-Scratch
    try {
        $mine = Get-FixallWorkerScope 'C:\checkouts\mine'
        $theirs = Get-FixallWorkerScope 'C:\checkouts\theirs'
        $foreign = New-DurableRecord $scratch 'claims' $theirs 616161 @('44444444-0000-0000-0000-000000000010')
        $released = New-Object System.Collections.ArrayList
        $result = Invoke-StrandedClaimRelease -ClaimsFileDirectory $scratch -Scope $mine -SelfPid 1 -IsAlive $Dead `
            -Release { param($Path) $null = $released.Add($Path); return $true } -Log { param($m) }
        Assert-Equal 'nothing was released' 0 $released.Count
        Assert-Equal 'nothing is in the summary' '' $result.Summary
        Assert-True 'and the record is still there for its own worker' (Test-Path $foreign)
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a legacy TEMP claims record is released and then removed' {
    $scratch = New-Scratch
    $legacy = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        $old = New-LegacyRecord $legacy 'claims' 717171 @('55555555-0000-0000-0000-000000000011')
        $released = New-Object System.Collections.ArrayList
        $result = Invoke-StrandedClaimRelease -ClaimsFileDirectory $scratch -LegacyDirectory $legacy `
            -Scope $scope -SelfPid 1 -IsAlive $Dead `
            -Release { param($Path) $null = $released.Add($Path); return $true } -Log { param($m) }
        Assert-Equal 'the old record was released' 1 $released.Count
        Assert-Equal 'by its own path' $old $released[0]
        Assert-Equal 'and the ids are reported' 1 @($result.TaskIds).Count
        Assert-False 'the drained record is gone' (Test-Path $old)
    }
    finally {
        Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $legacy -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-Case 'a release that fails keeps its record, wherever the record lives' {
    # Unchanged rule, asserted again in both locations: a claims file is the ONLY record of which
    # tasks are stranded, so one whose release failed must survive for the next tick to retry.
    $scratch = New-Scratch
    $legacy = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        $durable = New-DurableRecord $scratch 'claims' $scope 818181 @('66666666-0000-0000-0000-000000000012')
        $old = New-LegacyRecord $legacy 'claims' 919191 @('77777777-0000-0000-0000-000000000013')
        $result = Invoke-StrandedClaimRelease -ClaimsFileDirectory $scratch -LegacyDirectory $legacy `
            -Scope $scope -SelfPid 1 -IsAlive $Dead `
            -Release { param($Path) return $false } -Log { param($m) }
        Assert-Equal 'both failures are reported' 2 @($result.Failed).Count
        Assert-True 'the durable record survives' (Test-Path $durable)
        Assert-True 'and so does the legacy one' (Test-Path $old)
        Assert-Match 'and the RESULT line says some could not be reached' 'could not be reached' $result.Summary
    }
    finally {
        Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $legacy -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-Case 'one pass covers both locations' {
    # The upgrade tick's own case: its predecessor's record is in %TEMP% and its successor's will be
    # in the durable directory. A pass that did one or the other would lose a task either way.
    $scratch = New-Scratch
    $legacy = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        New-DurableRecord $scratch 'claims' $scope 101010 @('88888888-0000-0000-0000-000000000014') | Out-Null
        New-LegacyRecord $legacy 'claims' 202020 @('99999999-0000-0000-0000-000000000015') | Out-Null
        $result = Invoke-StrandedClaimRelease -ClaimsFileDirectory $scratch -LegacyDirectory $legacy `
            -Scope $scope -SelfPid 1 -IsAlive $Dead `
            -Release { param($Path) return $true } -Log { param($m) }
        Assert-Equal 'both records were acted on' 2 @($result.Files).Count
        Assert-Equal 'and both ids counted' 2 @($result.TaskIds).Count
        Assert-Match 'the summary says two dead runs' 'released 2 abandoned Doing claim' $result.Summary
    }
    finally {
        Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $legacy -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ── The sweep reports a stranded run from either location ─────────────────────────────────────

Invoke-Case 'the start-of-tick sweep reports a durable stranded task and clears it' {
    $scratch = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        $record = New-DurableRecord $scratch 'task' $scope 303030 @('aaaa1111-0000-0000-0000-000000000016')
        # UNIQUELY NAMED, for the reason the library's own header gives about $Log: a scriptblock's
        # free variables resolve in the scope it is INVOKED from, and Invoke-StartOfTickSweep has a
        # list called $noted - a sink that wrote to `$noted` would append to the sweep's own.
        $sweepReports = New-Object System.Collections.ArrayList
        $result = Invoke-StartOfTickSweep -Checkouts @() -TaskFileDirectory $scratch -Scope $scope `
            -SelfPid 1 -IsAlive $Dead -OnTaskNote { param($TaskId, $Note) $null = $sweepReports.Add("$TaskId|$Note") }.GetNewClosure() `
            -Log { param($m) }
        Assert-Equal 'the task was reported on' 1 $sweepReports.Count
        if ($sweepReports.Count -eq 1) {
            Assert-Match 'by id' 'aaaa1111-0000-0000-0000-000000000016' $sweepReports[0]
            Assert-Match 'and the note names the pid that abandoned it' 'pid 303030' $sweepReports[0]
            Assert-Match 'and says a wip tip is a resume point, not something to ship' 'UNFINISHED, UNVERIFIED' $sweepReports[0]
        }
        Assert-Equal 'the sweep carries the id' 1 @($result.Notes).Count
        Assert-False 'and the record is cleared, so no later tick re-reports it' (Test-Path $record)
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'and a legacy one too, in the same pass' {
    $scratch = New-Scratch
    $legacy = New-Scratch
    try {
        $scope = Get-FixallWorkerScope $scratch
        $old = New-LegacyRecord $legacy 'task' 404040 @('bbbb2222-0000-0000-0000-000000000017')
        $sweepReports = New-Object System.Collections.ArrayList
        $result = Invoke-StartOfTickSweep -Checkouts @() -TaskFileDirectory $scratch -LegacyDirectory $legacy `
            -Scope $scope -SelfPid 1 -IsAlive $Dead `
            -OnTaskNote { param($TaskId, $Note) $null = $sweepReports.Add($TaskId) }.GetNewClosure() -Log { param($m) }
        Assert-Equal 'the old record was reported on' 1 $sweepReports.Count
        if ($sweepReports.Count -eq 1) { Assert-Equal 'by id' 'bbbb2222-0000-0000-0000-000000000017' $sweepReports[0] }
        Assert-Equal 'and the sweep carries it' 1 @($result.Notes).Count
        Assert-False 'and it is drained from TEMP' (Test-Path $old)
    }
    finally {
        Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $legacy -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ── The loop's wiring, over the parsed AST ────────────────────────────────────────────────────

$loopTokens = $null
$loopErrors = $null
$Ast = [System.Management.Automation.Language.Parser]::ParseFile($LoopPath, [ref]$loopTokens, [ref]$loopErrors)
if ($loopErrors -and $loopErrors.Count -gt 0) {
    Write-Host 'FAIL: scripts/fixall-loop.ps1 does not parse' -ForegroundColor Red
    foreach ($e in $loopErrors) { Write-Host "  $($e.Message)" -ForegroundColor Red }
    exit 1
}

$Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
$Assignments = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))
$LoopText = Get-Content $LoopPath -Raw

function Find-LoopCommand([string]$Pattern) {
    return (@($Commands | Where-Object { $_.Extent.Text -match $Pattern }) | Select-Object -First 1)
}
function Find-Assignment([string]$LeftPattern) {
    return (@($Assignments | Where-Object { $_.Left.Extent.Text -match $LeftPattern }) | Select-Object -First 1)
}

$TaskFileAssign = Find-Assignment '\$taskFile$'
$ClaimsFileAssign = Find-Assignment '\$claimsFile$'
$ScopeAssign = Find-Assignment 'WorkerScope'
$StateDirAssign = Find-Assignment 'StateDir'
$SweepCall = Find-LoopCommand 'Invoke-StartOfTickSweep'
$ClaimReleaseCall = Find-LoopCommand 'Invoke-StrandedClaimRelease'

Invoke-Case 'the loop hands the run durable paths, not TEMP ones' {
    Assert-True 'the task file has a path of its own' ($null -ne $TaskFileAssign)
    Assert-True 'and so does the claims file' ($null -ne $ClaimsFileAssign)
    if (-not $TaskFileAssign -or -not $ClaimsFileAssign) { return }
    foreach ($pair in @(@{ N = 'task'; A = $TaskFileAssign }, @{ N = 'claims'; A = $ClaimsFileAssign })) {
        $text = $pair.A.Right.Extent.Text
        # Built by the library, so the name, the scope and the pid are in ONE place and the scan
        # cannot be looking for a shape nothing writes.
        Assert-Match "the $($pair.N) record comes from the library" 'Get-FixallStateFile' $text
        Assert-Match "the $($pair.N) record is this worker s" 'Scope' $text
        # Named for this loop's pid: that is what lets a LATER tick tell a dead run's record from a
        # live one's. One file shared by every tick would make a live run's claims look stranded.
        Assert-Match "the $($pair.N) record is named for this loop" '\$PID' $text
        Assert-NotMatch "and the $($pair.N) record is not in TEMP" 'env:TEMP' $text
    }
}

Invoke-Case 'both records and the wake journal share one state directory' {
    Assert-True 'the directory is computed once' ($null -ne $StateDirAssign)
    Assert-True 'and the worker scope once' ($null -ne $ScopeAssign)
    if (-not $StateDirAssign -or -not $ScopeAssign) { return }
    Assert-Match 'from the library, not spelled out again' 'Get-FixallStateDir' $StateDirAssign.Right.Extent.Text
    # The scope is the CHECKOUT's, which is the only thing that distinguishes two worktrees.
    Assert-Match 'the scope is the checkout s own path' 'RepoRoot' $ScopeAssign.Right.Extent.Text
    Assert-Match 'through the library' 'Get-FixallWorkerScope' $ScopeAssign.Right.Extent.Text
    # The wake journal is already durable (item 1). It must come from the same directory, or a
    # change to one of them silently leaves the other behind.
    $journal = Find-Assignment 'WakeJournal'
    Assert-True 'the wake journal is still placed' ($null -ne $journal)
    if ($journal) { Assert-Match 'in that same directory' 'StateDir' $journal.Right.Extent.Text }
    # No second spelling of the path anywhere in the loop.
    Assert-Equal 'and the path appears nowhere else in the loop' 0 `
    @([regex]::Matches($LoopText, "Astrid\\\\?state")).Count
}

Invoke-Case 'the start-of-tick passes drain the legacy TEMP records' {
    Assert-True 'the sweep is still called' ($null -ne $SweepCall)
    Assert-True 'and the claims pass is still called' ($null -ne $ClaimReleaseCall)
    if (-not $SweepCall -or -not $ClaimReleaseCall) { return }
    foreach ($pair in @(@{ N = 'sweep'; C = $SweepCall }, @{ N = 'claims pass'; C = $ClaimReleaseCall })) {
        $text = $pair.C.Extent.Text
        Assert-Match "the $($pair.N) looks in the durable directory" 'StateDir' $text
        # Requirement 3. Without this the upgrade tick makes every record written before it
        # invisible, which is the stranding the records exist to prevent.
        Assert-Match "the $($pair.N) also drains the legacy location" '-LegacyDirectory' $text
        Assert-Match "from TEMP, where the old records are" 'env:TEMP' $text
        Assert-Match "and only takes this worker s own" '-Scope' $text
    }
}

Invoke-Case 'nothing else in the loop moved' {
    # "Move two file paths" is exactly the change that quietly takes one of these with it, so each
    # is named here rather than trusted.
    Assert-True 'the pause/yield stand-down is still there' ($null -ne (Find-LoopCommand 'fixall-pause\.ts'))
    Assert-True 'the working-tree lock is still acquired' ($null -ne (Find-LoopCommand 'fixall-session\.ts'))
    Assert-True 'the dirty-checkout guard still runs' ($null -ne (Find-LoopCommand 'Get-CheckoutGuardVerdict'))
    Assert-True 'unfinished work is still saved' ($null -ne (Find-LoopCommand 'Save-UnfinishedWork'))
    Assert-True 'a died run is still reported on its task' ($null -ne (Find-LoopCommand 'Send-DiedRunNote'))
    Assert-True 'the wake keys are still acknowledged' ($null -ne (Find-LoopCommand 'Invoke-WakeAck'))
    # The board half of "Doing must not be a dead end", with its staleness bound: the only pass that
    # can reach a claim no record names at all.
    $board = Find-LoopCommand 'Invoke-DoingRelease -Board'
    Assert-True 'and the bounded board release survives' ($null -ne $board)
    if ($board) { Assert-Match 'with its staleness bound' 'StaleDoingMinutes' $board.Extent.Text }
}

Invoke-Case 'a claims record is still only deleted once its release got through' {
    # The rule this change must not break: a record whose release FAILED is the only evidence of
    # which tasks are sitting in Doing, so the next tick is the retry.
    $guards = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] }, $true)) |
        Where-Object { $_.Extent.Text -match 'claimsReleased' -and $_.Extent.Text -match 'Remove-Item' }
    Assert-Equal 'the deletion is guarded' 1 @($guards).Count
    if (@($guards).Count -ne 1) { return }
    Assert-Match 'by whether the release succeeded' '\$claimsReleased' @($guards)[0].Extent.Text
}

Invoke-Case 'AUTOMATION.md says where the state lives now' {
    # The loop's own documentation is where a human looks for "which file do I delete to unstick
    # this", and %TEMP% is the wrong answer now.
    # Asserted as booleans rather than by handing the whole document to Assert-Match: a failure
    # there would print every line of AUTOMATION.md into the gate's output.
    $automation = Get-Content $AutomationPath -Raw
    Assert-True 'the durable location is documented' ($automation -match 'Astrid\\state\\fixall-windows')
    Assert-True 'and the records are named' ($automation -match 'ASTRID_FIXALL_CLAIMS_FILE')
    Assert-True 'with the reason they are not in TEMP' ($automation -match 'swept by Windows')
}

Write-Host ''
if ($script:Checks -lt 70) {
    Write-Host "FAIL: only $($script:Checks) assertion(s) ran - this file is meant to make more" -ForegroundColor Red
    $script:Failures++
}
if ($script:Failures -gt 0) {
    Write-Host "RESULT: FAILED - $($script:Failures) failure(s) in $($script:Ran) case(s)" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: OK - $($script:Ran) case(s), $($script:Checks) assertion(s)" -ForegroundColor Green
exit 0
