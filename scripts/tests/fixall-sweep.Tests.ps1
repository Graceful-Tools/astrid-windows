#Requires -Version 5.1
<#
.SYNOPSIS
    A killed run strands work where no guard looks. The next tick sweeps both checkouts. Task 07c3b420.

.DESCRIPTION
    THE BUG. `6392dfdb` made a run that ENDS save its leftovers, in the loop, after the child
    exits. A `CTRL_C_EVENT` that takes the PowerShell process down reaches none of that: on
    2026-09-29 the 06:10 tick was interrupted (`LastTaskResult 3221225786` = `STATUS_CONTROL_C_EXIT`),
    wrote no `RESULT:` line, made no WIP commit, and left `../astrid-core` on
    `fix/google-round-trip-c1651475` with 449 uncommitted lines. Guard 2 reads `git status` in
    `$RepoRoot` ONLY, so the Windows tree looked clean on main and every tick for the next four days
    reported `SKIPPED - nothing to do`. Four days of quiet board while a task's work sat unclaimed in
    the checkout nothing checks — and the checkout nothing checks is the one CLAUDE.md sends core
    tasks to.

    A cleanup that only runs at death is the wrong shape. This one runs at the START of a tick, so it
    survives kill -9, a reboot and a closed console: none of it depends on the dying process doing
    anything.

    WHAT THESE TESTS ARE CAREFUL ABOUT, and it is the whole design. "Dirty means a died run's
    remains" is FALSE, in two different ways, and acting on dirtiness alone would trade this bug for
    a worse one:

      * In `astrid-windows`, guard 2 exists precisely BECAUSE a dirty tree cannot be told apart from
        Jon's uncommitted work or a `/fixstuff` session — neither takes a lock. A sweep triggered by
        dirtiness would commit and push somebody's work in progress out from under them.
      * `astrid-core` is a plain clone shared with the other repos' loops, not a `git worktree`, so
        `git add -A` can swallow a live run's files and `git checkout main` can move its HEAD. That
        happened on 2026-09-27 and is why the core is pushed but never returned to main here.

    So the sweep acts on DIRTY AND QUIET: no live session holder, and nothing touched for
    `-QuietMinutes` (default 30 — one whole tick interval). The asymmetry picks the default. Sweeping
    a live run costs work nobody can get back; waiting costs one skipped tick, which says why.

    WHY IT IS RUN, NOT PATTERN-MATCHED. What matters is where HEAD and the work end up in two
    checkouts at once, which no regex over a script can tell you. Each case builds scratch repos with
    bare origins and reads the answer out of git, the way fixall-loop-cleanup.Tests.ps1 does. Pester
    is deliberately not used: this machine has only the in-box Pester 3.4, and the whole harness
    needed is an assert and a counter.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-sweep.Tests.ps1
#>
[CmdletBinding()]
param()

# 'Continue', not 'Stop': this file shells out to git constantly and `2>&1` makes a native command's
# stderr an ErrorRecord, so 'Stop' would abort on the very cases that prove a git failure is handled.
# The assertion count decides the outcome, not an exception.
$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
foreach ($lib in @('scripts\lib\fixall-cleanup.ps1', 'scripts\lib\fixall-sweep.ps1')) {
    $path = Join-Path $RepoRoot $lib
    if (-not (Test-Path $path)) {
        Write-Host "FAIL: no library at $path" -ForegroundColor Red
        exit 1
    }
    . $path
}

$script:Failures = 0
$script:Ran = 0
# How many assertions have been MADE, not how many passed. A case that exits early makes none, and
# that is the one outcome a counter of failures cannot see - see Invoke-Case.
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

function Assert-True([string]$What, $Actual) { Assert-Equal $What 'True' ([bool]$Actual) }
function Assert-False([string]$What, $Actual) { Assert-Equal $What 'False' ([bool]$Actual) }

# One scratch repo with a bare origin and one commit on main.
function New-ScratchRepo([string]$Parent, [string]$Name) {
    $origin = Join-Path $Parent "$Name.git"
    $repo = Join-Path $Parent $Name
    & git init -q --bare -b main $origin 2>&1 | Out-Null
    & git init -q -b main $repo 2>&1 | Out-Null
    & git -C $repo config user.email 'loop@example.com' 2>&1 | Out-Null
    & git -C $repo config user.name 'loop' 2>&1 | Out-Null
    & git -C $repo config commit.gpgsign false 2>&1 | Out-Null
    Set-Content -Path (Join-Path $repo 'a.txt') -Value 'one' -Encoding utf8 -NoNewline
    & git -C $repo add -A 2>&1 | Out-Null
    & git -C $repo commit -q -m 'init' 2>&1 | Out-Null
    & git -C $repo remote add origin $origin 2>&1 | Out-Null
    & git -C $repo push -q -u origin main 2>&1 | Out-Null
    return $repo
}

# A tick sweeps what a PREVIOUS tick left, so every file it is meant to act on is old by
# definition. Tests that want the sweep to fire must say so explicitly rather than relying on a
# clock the harness does not control.
function Set-Old([string]$Path, [int]$MinutesAgo = 180) {
    $when = (Get-Date).AddMinutes(-$MinutesAgo)
    Get-ChildItem -Path $Path -Recurse -File -Force |
        Where-Object { $_.FullName -notmatch '\\\.git\\' } |
        ForEach-Object { $_.LastWriteTime = $when }
}

function Invoke-Case([string]$Name, [scriptblock]$Body) {
    $script:Ran++
    Write-Host "  - $Name"
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('fixall-sweep-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $before = $script:Checks
    try { & $Body $dir }
    catch {
        Write-Host "    FAIL the case threw: $($_.Exception.Message)" -ForegroundColor Red
        $script:Failures++
    }
    finally {
        # THE CASE THAT ASSERTS NOTHING IS THE ONE A FAILURE COUNT CANNOT SEE. A terminating error
        # inside the code under test - `Save-UnfinishedWork : The script failed due to call depth
        # overflow`, which is how this very file was first run - abandons the body, so none of its
        # Assert-* lines execute, no failure is counted, and the harness prints RESULT: OK. Six cases
        # passed vacuously that way. A silent false OK in the gate that guards the scheduled loop is
        # the same bug as four days of `SKIPPED - nothing to do`, so it is a failure here.
        if ($script:Checks -eq $before) {
            Write-Host '    FAIL the case made no assertions - it exited early' -ForegroundColor Red
            $script:Failures++
        }
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Git-In([string]$Repo, [string[]]$GitArgs) {
    $out = & git -C $Repo @GitArgs 2>&1
    return (($out | Out-String) -replace '\s+$', '')
}

# The two checkouts, described the way the loop describes them. RequireMain and ReturnToMain are
# false for the core on purpose: see the header.
function New-Checkouts([string]$Windows, [string]$Core) {
    return @(
        [pscustomobject]@{ Name = 'working tree'; Path = $Windows; RequireMain = $true; ReturnToMain = $true }
        [pscustomobject]@{ Name = 'astrid-core'; Path = $Core; RequireMain = $false; ReturnToMain = $false }
    )
}

Write-Host ''
Write-Host 'the start-of-tick sweep reaches work a killed run stranded (task 07c3b420)' -ForegroundColor Cyan

Invoke-Case 'commits and pushes a stranded windows branch, and returns it to main' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    & git -C $win checkout -q -b 'fix/8aa5732c-quick-add' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $win 'a.txt') -Value 'half done' -Encoding utf8 -NoNewline
    Set-Old $win

    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -Log { param($m) }

    Assert-Equal 'it swept exactly one checkout' 1 @($result.Swept).Count
    Assert-Equal 'and named which' 'working tree' @($result.Swept)[0].Name
    Assert-Equal 'HEAD is back on main' 'main' (Git-In $win @('rev-parse', '--abbrev-ref', 'HEAD'))
    Assert-Equal 'the tree is clean' '' (Git-In $win @('status', '--porcelain'))
    Assert-Match 'the tip says it is unverified' 'UNFINISHED, UNVERIFIED' (Git-In $win @('log', '-1', '--format=%s', 'origin/fix/8aa5732c-quick-add'))
    Assert-Equal 'the work reached origin' 'half done' (Git-In $win @('show', 'origin/fix/8aa5732c-quick-add:a.txt'))
    Assert-Match 'and the RESULT line will say so' 'working tree' $result.Summary
}

# Gap 2, and the whole reason this task exists: guard 2 and the post-exit save both look only at
# $RepoRoot, so this is the case that hid for four days.
Invoke-Case 'reaches astrid-core, which no guard looked at' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    & git -C $core checkout -q -b 'fix/google-round-trip-c1651475' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $core 'a.txt') -Value '449 lines of it' -Encoding utf8 -NoNewline
    Set-Old $core

    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -Log { param($m) }

    Assert-Equal 'it swept the core' 'astrid-core' @($result.Swept)[0].Name
    Assert-Equal 'the core tree is clean' '' (Git-In $core @('status', '--porcelain'))
    Assert-Equal 'the work reached origin' '449 lines of it' (Git-In $core @('show', 'origin/fix/google-round-trip-c1651475:a.txt'))
    Assert-Match 'the tip says it is unverified' 'UNFINISHED, UNVERIFIED' (Git-In $core @('log', '-1', '--format=%s'))
    # The shared-clone rule: pushing touches neither HEAD nor the working tree, so it is safe.
    # Checking out main is the step that destroyed another run's work on 2026-09-27.
    Assert-Equal 'and the core was NOT moved to main' 'fix/google-round-trip-c1651475' (Git-In $core @('rev-parse', '--abbrev-ref', 'HEAD'))
    Assert-Match 'and the RESULT line will say so' 'astrid-core' $result.Summary
}

Invoke-Case 'sweeps both checkouts in one tick when a run died across both' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    & git -C $win checkout -q -b 'fix/both' 2>&1 | Out-Null
    & git -C $core checkout -q -b 'fix/both-core' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $win 'a.txt') -Value 'shell half' -Encoding utf8 -NoNewline
    Set-Content -Path (Join-Path $core 'a.txt') -Value 'core half' -Encoding utf8 -NoNewline
    Set-Old $win
    Set-Old $core

    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -Log { param($m) }

    Assert-Equal 'both were swept' 2 @($result.Swept).Count
    Assert-Equal 'the shell work reached origin' 'shell half' (Git-In $win @('show', 'origin/fix/both:a.txt'))
    Assert-Equal 'the core work reached origin' 'core half' (Git-In $core @('show', 'origin/fix/both-core:a.txt'))
}

# The 95c7a68f shape, one repo over: committed, then killed before the push or the merge. Nothing to
# commit, and leaving it there is what makes the work invisible.
Invoke-Case 'pushes a committed branch the run never got to push' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    & git -C $core checkout -q -b 'fix/committed-not-pushed' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $core 'a.txt') -Value 'done but invisible' -Encoding utf8 -NoNewline
    & git -C $core add -A 2>&1 | Out-Null
    $env:GIT_COMMITTER_DATE = (Get-Date).AddHours(-3).ToString('yyyy-MM-ddTHH:mm:ss')
    try { & git -C $core commit -q --date $env:GIT_COMMITTER_DATE -m 'the run committed this and died' 2>&1 | Out-Null }
    finally { Remove-Item env:GIT_COMMITTER_DATE -ErrorAction SilentlyContinue }

    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -Log { param($m) }

    Assert-Equal 'it swept the core' 1 @($result.Swept).Count
    Assert-Equal 'no WIP commit was invented over a clean tree' '' @($result.Swept)[0].SavedBranch
    Assert-Equal 'the branch was pushed' 'fix/committed-not-pushed' @($result.Swept)[0].PushedBranch
    Assert-Equal 'the work is reviewable on origin' 'done but invisible' (Git-In $core @('show', 'origin/fix/committed-not-pushed:a.txt'))
}

# Idempotence. A branch already on origin is not work, and re-pushing it every half hour would put a
# line in every RESULT for a tick that found nothing.
Invoke-Case 'does not touch a clean branch that is already on origin' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    & git -C $core checkout -q -b 'fix/already-pushed' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $core 'a.txt') -Value 'landed' -Encoding utf8 -NoNewline
    & git -C $core add -A 2>&1 | Out-Null
    & git -C $core commit -q -m 'landed' 2>&1 | Out-Null
    & git -C $core push -q -u origin 'fix/already-pushed' 2>&1 | Out-Null
    Set-Old $core

    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -Log { param($m) }

    Assert-Equal 'nothing was swept' 0 @($result.Swept).Count
    Assert-Equal 'nothing is reported busy either' 0 @($result.Busy).Count
    Assert-Equal 'the summary is empty' '' $result.Summary
}

Invoke-Case 'leaves a clean pair on main entirely alone' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'

    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -Log { param($m) }

    Assert-Equal 'nothing was swept' 0 @($result.Swept).Count
    Assert-Equal 'no commit was invented' 'init' (Git-In $win @('log', '-1', '--format=%s'))
    Assert-Equal 'the summary is empty' '' $result.Summary
}

Write-Host ''
Write-Host 'and it refuses to sweep work that somebody may still be holding' -ForegroundColor Cyan

# The regression guard 2 would otherwise lose. Jon's uncommitted work, or a /fixstuff session:
# neither takes a lock, so recency is the only signal there is.
Invoke-Case 'leaves a dirty tree that was touched moments ago alone, and says why' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    Set-Content -Path (Join-Path $win 'a.txt') -Value 'Jon is typing in this' -Encoding utf8 -NoNewline

    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -Log { param($m) }

    Assert-Equal 'nothing was swept' 0 @($result.Swept).Count
    Assert-Equal 'it is reported busy instead' 1 @($result.Busy).Count
    Assert-Equal 'and named' 'working tree' @($result.Busy)[0].Name
    Assert-Match 'with a reason a human can act on' 'changed .* minute|recently' @($result.Busy)[0].Reason
    Assert-Equal 'the work is untouched' 'Jon is typing in this' (Get-Content (Join-Path $win 'a.txt') -Raw)
    Assert-Equal 'and it is still uncommitted' ' M a.txt' (Git-In $win @('status', '--porcelain'))
}

# The 2026-09-27 hazard: another repo's loop live in the shared core clone. Its own lock says so.
Invoke-Case 'leaves a checkout alone while a live session holds it' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    & git -C $core checkout -q -b 'fix/5f3453e2-autolink' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $core 'a.txt') -Value 'another run is writing this' -Encoding utf8 -NoNewline
    Set-Old $core

    $probe = { param($Path) if ($Path -eq $core) { return [pscustomobject]@{ Harness = 'claude-code'; HolderPid = 4242; Live = $true } } return $null }
    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -SessionProbe $probe -Log { param($m) }

    Assert-Equal 'nothing was swept' 0 @($result.Swept).Count
    Assert-Equal 'the core is reported busy' 'astrid-core' @($result.Busy)[0].Name
    Assert-Match 'naming the live holder' 'live session|claude-code' @($result.Busy)[0].Reason
    Assert-Equal 'its work is untouched' 'another run is writing this' (Get-Content (Join-Path $core 'a.txt') -Raw)
}

# THE LOCK GUARD 1 JUST TOOK IS THIS TICK'S OWN, AND IT IS LIVE. fixall-loop.ps1 acquires it under
# its own $PID seconds before the sweep runs, so a sweep that read "a live session holds this
# checkout" at face value would refuse to sweep astrid-windows for ever - blocked by its own
# launcher, which is the same shape as the deadlock .claude/commands/fixall.md avoids by not taking
# the lock at all. Holding the lock is precisely what makes these leftovers ours to move.
Invoke-Case 'sweeps past its own live lock, which guard 1 took seconds earlier' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    & git -C $win checkout -q -b 'fix/held-by-us' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $win 'a.txt') -Value 'left by the tick that held the lock' -Encoding utf8 -NoNewline
    Set-Old $win

    $probe = { param($Path) return [pscustomobject]@{ Harness = 'claude-code'; HolderPid = 14984; Live = $true } }
    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -SelfPid 14984 -SessionProbe $probe -Log { param($m) }

    Assert-Equal 'it swept anyway' 'working tree' @($result.Swept)[0].Name
    Assert-Equal 'the work reached origin' 'left by the tick that held the lock' (Git-In $win @('show', 'origin/fix/held-by-us:a.txt'))
}

# A STALE lock is the died run itself - that is what it is for. It must not protect its own remains.
Invoke-Case 'sweeps past a stale lock, which is the died run own holder' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    & git -C $win checkout -q -b 'fix/stale-holder' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $win 'a.txt') -Value 'left by the dead holder' -Encoding utf8 -NoNewline
    Set-Old $win

    $probe = { param($Path) return [pscustomobject]@{ Harness = 'claude-code'; HolderPid = 999999; Live = $false } }
    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -SessionProbe $probe -Log { param($m) }

    Assert-Equal 'it swept anyway' 'working tree' @($result.Swept)[0].Name
    Assert-Equal 'the work reached origin' 'left by the dead holder' (Git-In $win @('show', 'origin/fix/stale-holder:a.txt'))
}

Invoke-Case 'a checkout that is not there is not a failure' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $missing = Join-Path $dir 'no-core-here'

    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $missing) -QuietMinutes 30 `
        -TaskFileDirectory $dir -Log { param($m) }

    Assert-Equal 'nothing was swept' 0 @($result.Swept).Count
    Assert-Equal 'and nothing is busy' 0 @($result.Busy).Count
}

Write-Host ''
Write-Host 'a RESULT-less run is recoverable from the task file it left behind' -ForegroundColor Cyan

# Requirement 4. The loop deletes $ASTRID_FIXALL_TASK_FILE in its `finally`; a CTRL_C_EVENT that
# takes the process down never runs it, so the file surviving IS the signal that a run died without
# reporting. Its name carries the dead pid, which is why a later tick can tell it from its own.
Invoke-Case 'reports the task a dead run had taken, and clears the file' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    $stranded = Join-Path $dir 'astrid-fixall-windows-task-31337.txt'
    Set-Content -Path $stranded -Value "c1651475-0000-0000-0000-000000000000`n" -Encoding utf8
    & git -C $core checkout -q -b 'fix/c1651475' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $core 'a.txt') -Value 'the stranded work' -Encoding utf8 -NoNewline
    Set-Old $core

    $notes = New-Object System.Collections.ArrayList
    $result = Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -SelfPid 4242 -IsAlive { param($ProcessId) $false } `
        -OnTaskNote { param($TaskId, $Note) $null = $notes.Add([pscustomobject]@{ TaskId = $TaskId; Note = $Note }) } `
        -Log { param($m) }

    Assert-Equal 'one task was reported on' 1 $notes.Count
    Assert-Equal 'and it is the one the dead run took' 'c1651475-0000-0000-0000-000000000000' $notes[0].TaskId
    Assert-Match 'the note says the run did not finish' 'did not finish|never reported' $notes[0].Note
    Assert-Match 'and names the branch the work is on' 'fix/c1651475' $notes[0].Note
    Assert-Match 'and warns it passed no gate' 'UNFINISHED, UNVERIFIED' $notes[0].Note
    Assert-False 'the task file is gone, so the next tick does not re-report it' (Test-Path $stranded)
    Assert-Equal 'the id is reported back to the caller' 'c1651475-0000-0000-0000-000000000000' @($result.Notes)[0]
}

# A run can die having taken a task and written no code at all - the task is still sitting in Doing
# with nobody on it, which is the thing worth saying.
Invoke-Case 'reports a dead run task even when it stranded no code' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    Set-Content -Path (Join-Path $dir 'astrid-fixall-windows-task-31338.txt') -Value 'abcdef12-0000-0000-0000-000000000000' -Encoding utf8

    $notes = New-Object System.Collections.ArrayList
    Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -SelfPid 4242 -IsAlive { param($ProcessId) $false } `
        -OnTaskNote { param($TaskId, $Note) $null = $notes.Add([pscustomobject]@{ TaskId = $TaskId; Note = $Note }) } `
        -Log { param($m) } | Out-Null

    Assert-Equal 'it was still reported' 1 $notes.Count
    Assert-Match 'and says nothing was stranded' 'no unfinished work|nothing was stranded' $notes[0].Note
}

Invoke-Case 'never touches the task file of a run that is still alive' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    $mine = Join-Path $dir 'astrid-fixall-windows-task-4242.txt'
    $live = Join-Path $dir 'astrid-fixall-windows-task-5555.txt'
    Set-Content -Path $mine -Value 'aaaaaaaa-0000-0000-0000-000000000000' -Encoding utf8
    Set-Content -Path $live -Value 'bbbbbbbb-0000-0000-0000-000000000000' -Encoding utf8

    $notes = New-Object System.Collections.ArrayList
    Invoke-StartOfTickSweep -Checkouts (New-Checkouts $win $core) -QuietMinutes 30 `
        -TaskFileDirectory $dir -SelfPid 4242 -IsAlive { param($ProcessId) $ProcessId -eq 5555 } `
        -OnTaskNote { param($TaskId, $Note) $null = $notes.Add([pscustomobject]@{ TaskId = $TaskId; Note = $Note }) } `
        -Log { param($m) } | Out-Null

    Assert-Equal 'neither was reported' 0 $notes.Count
    Assert-True 'this tick own file is left for this tick' (Test-Path $mine)
    Assert-True 'and the live run file is left for the live run' (Test-Path $live)
}

Write-Host ''
Write-Host 'guard 2 says WHICH checkout stopped the tick' -ForegroundColor Cyan

Invoke-Case 'names astrid-core rather than the working tree' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    Set-Content -Path (Join-Path $core 'a.txt') -Value 'somebody is in the core' -Encoding utf8 -NoNewline

    $verdict = Get-CheckoutGuardVerdict -Checkouts (New-Checkouts $win $core)

    Assert-False 'the guard refuses' $verdict.Ok
    Assert-Match 'and names the core, not this repo' '^astrid-core is dirty' $verdict.Reason
}

Invoke-Case 'names the working tree when it is the dirty one' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    Set-Content -Path (Join-Path $win 'a.txt') -Value 'uncommitted' -Encoding utf8 -NoNewline

    $verdict = Get-CheckoutGuardVerdict -Checkouts (New-Checkouts $win $core)

    Assert-False 'the guard refuses' $verdict.Ok
    Assert-Match 'and names this repo' '^working tree is dirty' $verdict.Reason
}

Invoke-Case 'refuses a working tree that is off main, and allows a core that is' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'
    & git -C $win checkout -q -b 'somebody/branch' 2>&1 | Out-Null

    $verdict = Get-CheckoutGuardVerdict -Checkouts (New-Checkouts $win $core)
    Assert-False 'the working tree must be on main' $verdict.Ok
    Assert-Match 'and the reason names the branch' 'somebody/branch' $verdict.Reason

    # The core is pushed but never returned to main, so its branch cannot be a reason to skip - it
    # would wedge the loop on exactly the state the sweep leaves behind.
    & git -C $win checkout -q main 2>&1 | Out-Null
    & git -C $core checkout -q -b 'fix/left-by-the-sweep' 2>&1 | Out-Null
    $after = Get-CheckoutGuardVerdict -Checkouts (New-Checkouts $win $core)
    Assert-True 'a core on a branch is fine' $after.Ok
}

Invoke-Case 'passes a clean pair' {
    param($dir)
    $win = New-ScratchRepo $dir 'windows'
    $core = New-ScratchRepo $dir 'core'

    $verdict = Get-CheckoutGuardVerdict -Checkouts (New-Checkouts $win $core)

    Assert-True 'the guard passes' $verdict.Ok
    Assert-Equal 'with nothing to say' '' $verdict.Reason
}

Write-Host ''
Write-Host 'the loop wires the sweep in ahead of the guard it unblocks' -ForegroundColor Cyan

# Read off the source: the ORDER of two blocks in one script is not something either of them can be
# run to demonstrate, and the order is the whole point. A sweep after guard 2 would never run,
# because guard 2 is what the leftovers trip.
$Loop = Get-Content (Join-Path $RepoRoot 'scripts\fixall-loop.ps1') -Raw
$Automation = Get-Content (Join-Path $RepoRoot 'docs\AUTOMATION.md') -Raw

$script:Ran++
Write-Host '  - sweeps after taking the lock and before guard 2 decides anything'
$sweepAt = $Loop.IndexOf('Invoke-StartOfTickSweep')
$lockAt = $Loop.IndexOf("'acquire'")
$guardAt = $Loop.IndexOf('Get-CheckoutGuardVerdict')
Assert-True 'the loop calls the sweep' ($sweepAt -ge 0)
Assert-True 'after guard 1 has the lock' ($sweepAt -gt $lockAt)
Assert-True 'and before guard 2' ($guardAt -gt $sweepAt)

$script:Ran++
Write-Host '  - still saves the run it watched exit, which knows why it ended'
Assert-Match 'the post-exit save is kept' 'Save-UnfinishedWork -RepoRoot \$RepoRoot' $Loop

$script:Ran++
Write-Host '  - passes astrid-core to the sweep, and AUTOMATION.md says the sweep exists'
Assert-Match 'the core is one of the swept checkouts' '\$CoreRepo' $Loop
Assert-Match 'AUTOMATION.md documents the sweep' 'start-of-tick sweep|sweeps both checkouts' $Automation
Assert-Match 'and the quiet window it depends on' 'FIXALL_QUIET_MINUTES' $Automation

Write-Host ''
if ($script:Failures -gt 0) {
    Write-Host ("RESULT: FAILED - {0} assertion(s) across {1} case(s)" -f $script:Failures, $script:Ran) -ForegroundColor Red
    exit 1
}
Write-Host ("RESULT: OK - {0} case(s)" -f $script:Ran) -ForegroundColor Green
exit 0
