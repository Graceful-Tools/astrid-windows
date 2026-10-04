#Requires -Version 5.1
<#
.SYNOPSIS
    Sweep BOTH checkouts at the START of a tick, for work a killed run stranded. Task 07c3b420.

.DESCRIPTION
    WHY THIS EXISTS, AND WHY IT IS NOT THE SAVE IN fixall-cleanup.ps1. That one runs after the
    `claude` child exits, inside the loop, and it is the better path when it runs — it knows the
    reason the run ended. It cannot run when the loop itself is killed. On 2026-09-29 the 06:10 tick
    took a core task, worked in `../astrid-core` for about ten minutes, and was interrupted:
    `LastTaskResult 3221225786` = `STATUS_CONTROL_C_EXIT`, no `RESULT:` line in the log, no WIP
    commit, no task comment. `Save-UnfinishedWork` never got the chance, and the `finally` that
    releases the lock is the only thing PowerShell guarantees on a kill.

    A cleanup that only runs at death is the wrong shape. THIS ONE RUNS AT THE START OF A TICK, so it
    survives `kill -9`, a reboot and a closed console: none of it depends on the dying process doing
    anything at all.

    AND IT LOOKS AT astrid-core, WHICH NOTHING DID. Guard 2 read `git status --porcelain` in
    `$RepoRoot` only, and the post-exit save worked the same tree. So the Windows tree was clean on
    main, guard 2 passed, and every tick from 06:40 for the next four days reported
    `SKIPPED - nothing to do` while 449 uncommitted lines for a task in Doing sat in the other
    checkout. Note the asymmetry that makes this worse than it sounds: CLAUDE.md sends a task whose
    fix is a rule, a service, the Outbox or sync INTO the core, so the checkout nothing checked is
    the one most likely to hold a died run's work.

    DIRTY IS NOT THE SAME AS STRANDED, and this is the whole design.

      * In this repo, guard 2 exists BECAUSE a dirty tree cannot be told apart from Jon's
        uncommitted work or a `/fixstuff` session — neither takes the lock. A sweep that fired on
        dirtiness would commit and push somebody's work in progress out from under them, which is a
        worse bug than the one being fixed.
      * `../astrid-core` is a plain clone shared with the other repos' loops, not a `git worktree`.
        `git add -A` there can swallow a live run's files and `git checkout main` moves its HEAD;
        that happened on 2026-09-27. Pushing a branch touches neither, which is why the core is
        pushed and left where it stands.

    So a checkout is swept only when it has work AND is QUIET: no live `fixall-session` holder, and
    nothing touched for `-QuietMinutes` (default 30, one whole tick interval — "quiet for longer
    than a tick" is the rule, and a long model turn or a cargo build is minutes, not thirty). The
    asymmetry picks that default: sweeping a live run costs work nobody can get back, while waiting
    costs one skipped tick that says why. A checkout with work that is not quiet is reported BUSY,
    and guard 2 then skips the tick naming it.

    THE DECISION IS ALL THAT IS NEW HERE. The commit, the push and the unwind are
    `Save-UnfinishedWork`'s, already specified by scripts/tests/fixall-loop-cleanup.Tests.ps1. This
    file decides only whether and where. scripts/tests/fixall-sweep.Tests.ps1 is its specification,
    and runs it against scratch repos because where HEAD and the work end up is the behaviour.
#>

# The commit/push/unwind this delegates to. Dot-sourcing it here rather than relying on the caller
# means a sweep cannot be loaded half-working; re-defining the same functions is harmless.
. (Join-Path $PSScriptRoot 'fixall-cleanup.ps1')

# A checkout is described by the caller as { Name; Path; RequireMain; ReturnToMain }. Read through a
# helper so a hashtable and a pscustomobject both work and a missing field means its safe default
# rather than $null.
function Get-CheckoutField {
    param($Checkout, [Parameter(Mandatory)][string]$Field, $Default = $null)
    if ($null -eq $Checkout) { return $Default }
    if ($Checkout -is [System.Collections.IDictionary]) {
        if ($Checkout.Contains($Field) -and $null -ne $Checkout[$Field]) { return $Checkout[$Field] }
        return $Default
    }
    $property = $Checkout.PSObject.Properties[$Field]
    if ($property -and $null -ne $property.Value) { return $property.Value }
    return $Default
}

# `git status --porcelain` lines are two status characters, a space, then the path. A rename carries
# both sides; only the destination exists on disk. Quoted paths are unquoted bluntly: a path this
# cannot resolve is skipped rather than guessed at, and skipping costs only a less precise age.
function Get-PorcelainPath {
    param([string]$Porcelain)
    $paths = New-Object System.Collections.ArrayList
    foreach ($line in ($Porcelain -split "`r?`n")) {
        if (-not $line -or $line.Length -le 3) { continue }
        $path = $line.Substring(3)
        if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1] }
        $path = $path.Trim().Trim('"')
        if ($path) { $null = $paths.Add($path) }
    }
    return $paths.ToArray()
}

# How long ago the newest uncommitted change was written. $null means no uncommitted path resolved to
# a file at all — a tree dirty only with DELETIONS — and the caller treats that as quiet: there is no
# editor writing files, and the save is non-destructive either way.
function Get-DirtyAgeMinutes {
    param([Parameter(Mandatory)][string]$Path, [string]$Porcelain)
    $newest = $null
    foreach ($relative in (Get-PorcelainPath -Porcelain $Porcelain)) {
        $full = Join-Path $Path $relative
        if (-not (Test-Path -LiteralPath $full)) { continue }
        $item = Get-Item -LiteralPath $full -Force -ErrorAction SilentlyContinue
        if (-not $item) { continue }
        # An untracked DIRECTORY is one porcelain line; its own timestamp says nothing about the
        # files in it, so take the newest thing inside.
        $stamps = @($item.LastWriteTime)
        if ($item.PSIsContainer) {
            $stamps += @(Get-ChildItem -LiteralPath $full -Recurse -File -Force -ErrorAction SilentlyContinue |
                    ForEach-Object { $_.LastWriteTime })
        }
        foreach ($stamp in $stamps) {
            if ($null -eq $newest -or $stamp -gt $newest) { $newest = $stamp }
        }
    }
    if ($null -eq $newest) { return $null }
    return ((Get-Date) - $newest).TotalMinutes
}

<#
.SYNOPSIS
    What one checkout looks like, and whether it is holding a previous run's work.

.OUTPUTS
    Exists      - a git checkout is actually there
    Dirty       - the porcelain status, empty when clean
    Branch      - the current branch, or 'HEAD' when detached
    Unpushed    - on a branch that is not main whose tip is not on origin
    HasWork     - Dirty or Unpushed: something a died run could have left
    AgeMinutes  - how long since that work was last touched; $null when it cannot be dated
#>
function Get-CheckoutStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Path,
        [AllowEmptyString()][string]$Name = ''
    )

    $absent = [pscustomobject]@{
        Name = $Name; Path = $Path; Exists = $false; Dirty = ''
        Branch = ''; Unpushed = $false; HasWork = $false; AgeMinutes = $null
    }
    if (-not $Path -or -not (Test-Path $Path)) { return $absent }
    if ((Invoke-CleanupGit -RepoRoot $Path -GitArgs @('rev-parse', '--git-dir')).ExitCode -ne 0) { return $absent }

    $dirty = (Invoke-CleanupGit -RepoRoot $Path -GitArgs @('status', '--porcelain')).Output
    $branch = (Invoke-CleanupGit -RepoRoot $Path -GitArgs @('rev-parse', '--abbrev-ref', 'HEAD')).Output

    # A branch whose tip is already on origin is not stranded work, it is just a branch. Without this
    # the sweep would re-push it and mention it in the RESULT line every half hour. 'HEAD' is what
    # --abbrev-ref answers when detached: nothing to push, and nothing that would survive a checkout.
    $unpushed = $false
    if ($branch -and $branch -ne 'main' -and $branch -ne 'HEAD') {
        $local = (Invoke-CleanupGit -RepoRoot $Path -GitArgs @('rev-parse', 'HEAD')).Output
        $remote = Invoke-CleanupGit -RepoRoot $Path -GitArgs @('rev-parse', '--verify', '--quiet', "refs/remotes/origin/$branch")
        $unpushed = ($remote.ExitCode -ne 0) -or ($remote.Output -ne $local)
    }

    $age = $null
    if ($dirty) {
        $age = Get-DirtyAgeMinutes -Path $Path -Porcelain $dirty
    }
    elseif ($unpushed) {
        # Clean but unpushed: there is no file to date, so the commit itself is the clock. This is
        # the 95c7a68f shape - the run committed and died before it could push or merge.
        $committed = Invoke-CleanupGit -RepoRoot $Path -GitArgs @('log', '-1', '--format=%ct', 'HEAD')
        if ($committed.ExitCode -eq 0 -and $committed.Output -match '^\d+$') {
            $when = [DateTimeOffset]::FromUnixTimeSeconds([long]$committed.Output).LocalDateTime
            $age = ((Get-Date) - $when).TotalMinutes
        }
    }

    return [pscustomobject]@{
        Name       = $Name
        Path       = $Path
        Exists     = $true
        Dirty      = $dirty
        Branch     = $branch
        Unpushed   = $unpushed
        HasWork    = [bool]($dirty -or $unpushed)
        AgeMinutes = $age
    }
}

<#
.SYNOPSIS
    Guard 2, over every checkout, saying WHICH one stopped the tick.

.DESCRIPTION
    `SKIPPED - astrid-core is dirty` is a different instruction to a human than
    `SKIPPED - working tree is dirty`, and before this the second was printed for both because only
    one tree was looked at.

    RequireMain is this repo's rule, not the core's. The sweep deliberately leaves the core on the
    branch it pushed (a shared clone's HEAD is not ours to move), so making a core branch a reason to
    skip would wedge the loop on the state the sweep itself just created.
#>
function Get-CheckoutGuardVerdict {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][array]$Checkouts)

    foreach ($checkout in $Checkouts) {
        $name = Get-CheckoutField $checkout 'Name' 'a checkout'
        $path = Get-CheckoutField $checkout 'Path' ''
        $status = Get-CheckoutStatus -Path $path -Name $name
        # A checkout that is not there is a degraded run, not a failed one - the loop warns about it
        # separately, loudly, where it decides whether to pass --add-dir.
        if (-not $status.Exists) { continue }

        if ($status.Dirty) {
            return [pscustomobject]@{ Ok = $false; Reason = "$name is dirty, leaving it alone" }
        }
        if ((Get-CheckoutField $checkout 'RequireMain' $false) -and $status.Branch -ne 'main') {
            return [pscustomobject]@{ Ok = $false; Reason = "$name is on $($status.Branch), not main" }
        }
    }
    return [pscustomobject]@{ Ok = $true; Reason = '' }
}

<#
.SYNOPSIS
    The task files of runs that died without reporting.

.DESCRIPTION
    The loop hands each run `ASTRID_FIXALL_TASK_FILE` as `...-task-<its own pid>.txt` and deletes it
    in its `finally`. A kill that takes the process down never runs that, so A SURVIVING TASK FILE IS
    THE SIGNAL that a run ended without writing a `RESULT:` line — which is requirement 4 of the
    task: "the next tick seeing a task file with no matching completion is how it knows to look".

    The pid in the name is what lets a later tick tell a dead run's file from its own or from a live
    one's. Liveness is checked rather than assumed, because the one thing worse than missing a
    stranded task is commenting "this did not finish" on a task somebody is working right now.
#>
function Get-StrandedTaskFile {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$Directory,
        [int]$SelfPid = $PID,
        [scriptblock]$IsAlive = $null
    )

    $found = New-Object System.Collections.ArrayList
    if (-not $Directory -or -not (Test-Path $Directory)) { return $found.ToArray() }

    $files = @(Get-ChildItem -Path $Directory -Filter 'astrid-fixall-windows-task-*.txt' -File -ErrorAction SilentlyContinue)
    foreach ($file in $files) {
        if ($file.Name -notmatch '^astrid-fixall-windows-task-(\d+)\.txt$') { continue }
        $holder = [int]$Matches[1]
        if ($holder -eq $SelfPid) { continue }
        $alive = $false
        if ($IsAlive) { $alive = [bool](& $IsAlive $holder) }
        else { $alive = [bool](Get-Process -Id $holder -ErrorAction SilentlyContinue) }
        if ($alive) { continue }

        $taskId = (Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue)
        if ($taskId) { $taskId = $taskId.Trim() }
        if (-not $taskId) {
            # A run that was killed before it took anything. Nothing to report, and leaving the file
            # would make every later tick look at it again.
            Remove-Item $file.FullName -Force -ErrorAction SilentlyContinue
            continue
        }
        $null = $found.Add([pscustomobject]@{ Path = $file.FullName; HolderPid = $holder; TaskId = $taskId })
    }
    return $found.ToArray()
}

<#
.SYNOPSIS
    The sweep itself: run it at the start of a tick, before guard 2 decides anything.

.PARAMETER Checkouts
    { Name; Path; RequireMain; ReturnToMain } each. Order is the order they are reported in.

.PARAMETER QuietMinutes
    How long a checkout must have been untouched before its leftovers are treated as a dead run's.

.PARAMETER SessionProbe
    Called with a checkout path; returns $null or { Harness; HolderPid; Live }. The loop passes one
    that asks `fixall-session.ts status`. With no probe the quiet window is the only defence, which
    is why it is the primary one.

.PARAMETER OnTaskNote
    Called with (TaskId, Note) for each task a dead run had taken. The loop passes one that comments
    on the board; it is injected because this library is git and the clock, nothing else.

.OUTPUTS
    Swept   - the checkouts whose leftovers were saved, with the branches
    Busy    - the checkouts that had work but were not ours to touch, each with a reason
    Notes   - the task ids reported on
    Summary - one clause for this tick's RESULT line, empty when nothing was swept
#>
function Invoke-StartOfTickSweep {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][array]$Checkouts,
        [int]$QuietMinutes = 30,
        [AllowEmptyString()][string]$TaskFileDirectory = $env:TEMP,
        [int]$SelfPid = $PID,
        [scriptblock]$SessionProbe = $null,
        [scriptblock]$IsAlive = $null,
        [scriptblock]$OnTaskNote = $null,
        [scriptblock]$Log = $null
    )

    $swept = New-Object System.Collections.ArrayList
    $busy = New-Object System.Collections.ArrayList
    $noted = New-Object System.Collections.ArrayList

    # UNIQUELY NAMED, AND A CLOSURE, both deliberately. PowerShell resolves a scriptblock's free
    # variables against the scope it is INVOKED from, not the one it was written in, and
    # Save-UnfinishedWork has a parameter called $Log too: a sink that read `$Log` would, once handed
    # down there, resolve $Log to ITSELF and recurse until "The script failed due to call depth
    # overflow". GetNewClosure binds $SweepLogSink at creation, so the name cannot be captured again.
    $SweepLogSink = $Log
    $say = {
        param([string]$Line)
        if ($SweepLogSink) { & $SweepLogSink $Line } else { Write-Host $Line }
    }.GetNewClosure()

    foreach ($checkout in $Checkouts) {
        $name = Get-CheckoutField $checkout 'Name' 'a checkout'
        $path = Get-CheckoutField $checkout 'Path' ''
        $status = Get-CheckoutStatus -Path $path -Name $name
        if (-not $status.Exists -or -not $status.HasWork) { continue }

        $holder = $null
        if ($SessionProbe) { $holder = & $SessionProbe $path }
        # OUR OWN LOCK IS NOT SOMEBODY ELSE'S RUN. fixall-loop.ps1's guard 1 acquires this repo's
        # lock under the loop's own pid seconds before the sweep, and it is live - the loop is the
        # live process. Reading that at face value would refuse to sweep astrid-windows for ever,
        # blocked by its own launcher. Holding the lock is exactly what makes these leftovers ours.
        if ($holder -and (Get-CheckoutField $holder 'HolderPid' 0) -eq $SelfPid) { $holder = $null }
        if ($holder -and (Get-CheckoutField $holder 'Live' $false)) {
            $reason = "a live session holds it ($(Get-CheckoutField $holder 'Harness' 'unknown'), pid $(Get-CheckoutField $holder 'HolderPid' '?'))"
            & $say "  SWEEP: $name has leftovers but $reason - not touching it"
            $null = $busy.Add([pscustomobject]@{ Name = $name; Path = $path; Reason = $reason; Status = $status })
            continue
        }

        if ($null -ne $status.AgeMinutes -and $status.AgeMinutes -lt $QuietMinutes) {
            $reason = ('it changed {0:N0} minute(s) ago, so somebody may still be working in it' -f $status.AgeMinutes)
            & $say "  SWEEP: $name has uncommitted changes but $reason - not touching it"
            $null = $busy.Add([pscustomobject]@{ Name = $name; Path = $path; Reason = $reason; Status = $status })
            continue
        }

        & $say "  SWEEP: $name was left with work by a previous run - saving it"
        $returnToMain = [bool](Get-CheckoutField $checkout 'ReturnToMain' $true)
        # ClaudeExitCode 0 is honest: a swept checkout is one whose run never reported an exit code
        # at all. The commit body says it was saved by the loop, and the subject says UNVERIFIED.
        $saved = Save-UnfinishedWork -RepoRoot $path -ClaudeExitCode 0 -ReturnToMain $returnToMain -Log $say

        $branch = @($saved.SavedBranch, $saved.PushedBranch, $saved.UnpushedBranch, $status.Branch) |
            Where-Object { $_ } | Select-Object -First 1
        $null = $swept.Add([pscustomobject]@{
                Name           = $name
                Path           = $path
                Branch         = $branch
                SavedBranch    = $saved.SavedBranch
                PushedBranch   = $saved.PushedBranch
                UnpushedBranch = $saved.UnpushedBranch
                ReturnedToMain = $saved.ReturnedToMain
            })
    }

    # The board half. A checkout is swept whether or not a task file names an owner, and a task file
    # is reported whether or not anything was stranded: a task sitting in Doing with nobody on it is
    # the thing worth saying either way.
    $stranded = @(Get-StrandedTaskFile -Directory $TaskFileDirectory -SelfPid $SelfPid -IsAlive $IsAlive)
    if ($stranded.Count -gt 0) {
        $leftovers = 'No unfinished work was stranded in either checkout, so the run ended before it wrote anything.'
        if ($swept.Count -gt 0) {
            $where = (@($swept) | ForEach-Object { "$($_.Name) -> ``$($_.Branch)``" }) -join ', '
            $leftovers = "Its unfinished work was committed and pushed by a later tick: $where."
        }
        foreach ($item in $stranded) {
            $note = "**A scheduled /fixall (windows) tick found this task stranded.** The run that took it " +
            "(pid $($item.HolderPid)) did not finish and never reported: its log entry has no ``RESULT:`` line, " +
            "so it was killed from outside rather than ending. $leftovers " +
            'This task is still in Doing and nothing on that branch has passed a gate: a tip reading ' +
            '`wip: ... UNFINISHED, UNVERIFIED` is a resume point, not something to ship. ' +
            'Log: %LOCALAPPDATA%\Astrid\logs\fixall-windows.log'
            & $say "  SWEEP: reporting the stranded run on task $($item.TaskId)"
            if ($OnTaskNote) { & $OnTaskNote $item.TaskId $note }
            $null = $noted.Add($item.TaskId)
            # Cleared whether or not the comment landed: a task file that survives is re-reported on
            # every tick forever, and the RESULT line and the log have said it either way.
            Remove-Item $item.Path -Force -ErrorAction SilentlyContinue
        }
    }

    $summary = ''
    if ($swept.Count -gt 0) {
        $parts = @($swept) | ForEach-Object {
            if ($_.PushedBranch) { "$($_.Name): $($_.PushedBranch) pushed" }
            elseif ($_.UnpushedBranch) { "$($_.Name): $($_.UnpushedBranch) committed locally, push failed" }
            elseif ($_.SavedBranch) { "$($_.Name): $($_.SavedBranch) committed" }
            else { "$($_.Name): left for a human" }
        }
        $summary = 'swept a previous run''s leftovers (' + ($parts -join '; ') + ')'
    }

    return [pscustomobject]@{
        Swept   = $swept.ToArray()
        Busy    = $busy.ToArray()
        Notes   = $noted.ToArray()
        Summary = $summary
    }
}
