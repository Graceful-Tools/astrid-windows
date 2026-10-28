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

<#
.SYNOPSIS
    Where a tick leaves the two records a LATER tick reads. Task 18ba8c2e, scope item 2.

.DESCRIPTION
    `ASTRID_FIXALL_TASK_FILE` and `ASTRID_FIXALL_CLAIMS_FILE` are read by a later tick BY
    DEFINITION — a surviving task file is how the next tick knows a run died without reporting, and
    a surviving claims file is the only record of which tasks that run left in `Doing`. They were in
    `%TEMP%`, which Storage Sense and every disk-cleanup run sweep: the one record of an abandoned
    claim could be deleted by Windows housekeeping between two ticks, and the cost of that is
    invisible and permanent (`f8dfc84e` sat in `Doing` from 01:25 on 2026-10-18 until a human
    released it by hand). So they live beside the log and the wake journal instead.

    ONE function for the directory, used by all three, so a change to where state lives cannot move
    two of them and leave the third behind.
#>
function Get-FixallStateDir {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$Root = $env:LOCALAPPDATA)

    # Per board, because the wake keys and the claims are per board. A root this cannot resolve is
    # not worth guessing at: the caller gets the relative path and the failure is visible.
    $dir = Join-Path $Root 'Astrid\state\fixall-windows'
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    return $dir
}

<#
.SYNOPSIS
    Which CHECKOUT wrote a record. Task 18ba8c2e, scope item 2.

.DESCRIPTION
    `%LOCALAPPDATA%` is one directory per USER for every checkout on the machine, where `%TEMP%` at
    least turned over, and a pid is not unique across checkouts — Windows reuses them freely. It
    matters because a release names the branch carrying the task's id and runs with `--repo` pointed
    at the sweeping checkout: a record drained from another worktree would be released against the
    wrong repository. So each record's name carries a short digest of the checkout's own path, and a
    scan takes only its own worker's. Another worker sweeps its own.

    The digest is of the NORMALISED path — full, trailing separators trimmed, lower-cased — because
    the loop is handed its root by Task Scheduler, by an interactive shell and by the test suite,
    and a scope that changed with the spelling would make a tick blind to its own predecessor.
#>
function Get-FixallWorkerScope {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$Path)

    $normalised = [string]$Path
    try { $normalised = [System.IO.Path]::GetFullPath($normalised) } catch { }
    $normalised = $normalised.Replace('/', '\').TrimEnd('\').ToLowerInvariant()

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalised))
    }
    finally { $sha.Dispose() }
    # Eight hex characters: it goes in a filename and is read by eye in a log line, and collisions
    # between the two or three checkouts on one machine are not the risk being managed here.
    return (-join ($bytes[0..3] | ForEach-Object { $_.ToString('x2') }))
}

# The name of one record. ONE place that knows the shape, so the scan below cannot be looking for
# something nothing writes - which is a bug that looks exactly like a quiet board.
function Get-FixallStateFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][ValidateSet('task', 'claims')][string]$Kind,
        [Parameter(Mandatory)][string]$Scope,
        [int]$OwnerPid = $PID
    )
    return (Join-Path $Directory "$Kind-$Scope-$OwnerPid.txt")
}

<#
.SYNOPSIS
    The records of runs that are gone, from the durable directory AND the legacy one.

.DESCRIPTION
    THE DRAIN IS NOT OPTIONAL. The tick that upgrades is a tick whose predecessors' records are all
    in `%TEMP%` under the old unscoped names. A scan that looked only in the new directory would
    make every one of them invisible on the first tick after the change — the precise stranding
    these records exist to prevent, delivered by the fix for it. So both locations are searched,
    both namings are matched, and a legacy record is acted on identically.

    A legacy record carries no scope by construction, so it is drained whatever this worker's scope
    is; that is what the old behaviour already was. A SCOPED record belongs to the worker named in
    it and nothing else touches it — not even to delete it.

    Liveness is checked rather than assumed, because the one thing worse than missing a stranded
    task is releasing a task out from under a run that is still working it.
#>
function Get-FixallStateRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('task', 'claims')][string]$Kind,
        [AllowEmptyString()][string]$Directory,
        [AllowEmptyString()][string]$LegacyDirectory,
        [AllowEmptyString()][string]$Scope,
        [int]$SelfPid = $PID,
        [scriptblock]$IsAlive = $null
    )

    $found = New-Object System.Collections.ArrayList
    $seen = New-Object System.Collections.ArrayList

    # `task-<scope>-<pid>.txt` beside the log, and the pre-18ba8c2e `astrid-fixall-windows-task-<pid>.txt`
    # wherever it was left. The legacy name is matched in BOTH directories: a %TEMP% that is also the
    # durable directory is not a case worth failing on, and the suites that predate this change hand
    # one scratch directory for both.
    $patterns = @(
        [pscustomobject]@{ Filter = "$Kind-*.txt"; Regex = "^$Kind-([0-9a-f]+)-(\d+)\.txt$"; Scoped = $true },
        [pscustomobject]@{ Filter = "astrid-fixall-windows-$Kind-*.txt"; Regex = "^astrid-fixall-windows-$Kind-(\d+)\.txt$"; Scoped = $false }
    )

    foreach ($directory in @($Directory, $LegacyDirectory)) {
        if (-not $directory -or -not (Test-Path $directory)) { continue }
        foreach ($pattern in $patterns) {
            foreach ($file in @(Get-ChildItem -Path $directory -Filter $pattern.Filter -File -ErrorAction SilentlyContinue)) {
                if ($file.Name -notmatch $pattern.Regex) { continue }
                if ($pattern.Scoped) {
                    $recordScope = $Matches[1]
                    $holder = [int]$Matches[2]
                    # Another checkout's record. Left exactly as it is, including on disk: deleting
                    # it would strand its task as surely as %TEMP% housekeeping did.
                    if ($Scope -and $recordScope -ne $Scope) { continue }
                }
                else { $holder = [int]$Matches[1] }

                # One directory can be handed twice (the suites that predate this change pass one
                # scratch path as both), and a file acted on twice would be reported twice.
                if ($seen -contains $file.FullName) { continue }
                $null = $seen.Add($file.FullName)

                if ($holder -eq $SelfPid) { continue }
                $alive = $false
                if ($IsAlive) { $alive = [bool](& $IsAlive $holder) }
                else { $alive = [bool](Get-Process -Id $holder -ErrorAction SilentlyContinue) }
                if ($alive) { continue }

                $lines = @(@((Get-Content $file.FullName -ErrorAction SilentlyContinue)) |
                        ForEach-Object { if ($null -ne $_) { $_.Trim() } } |
                        Where-Object { $_ })
                if ($lines.Count -eq 0) {
                    # A run that was killed before it took anything, or claimed nothing. Nothing to
                    # report, and leaving the record would make every later tick look at it again.
                    Remove-Item $file.FullName -Force -ErrorAction SilentlyContinue
                    continue
                }
                $null = $found.Add([pscustomobject]@{
                        Path      = $file.FullName
                        HolderPid = $holder
                        Lines     = $lines
                        Legacy    = (-not $pattern.Scoped)
                    })
            }
        }
    }
    return $found.ToArray()
}

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

    WHERE it looks, and the drain of the pre-18ba8c2e `%TEMP%` records, is Get-FixallStateRecord's.
#>
function Get-StrandedTaskFile {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$Directory,
        [AllowEmptyString()][string]$LegacyDirectory,
        [AllowEmptyString()][string]$Scope,
        [int]$SelfPid = $PID,
        [scriptblock]$IsAlive = $null
    )

    $found = New-Object System.Collections.ArrayList
    foreach ($record in @(Get-FixallStateRecord -Kind 'task' -Directory $Directory `
                -LegacyDirectory $LegacyDirectory -Scope $Scope -SelfPid $SelfPid -IsAlive $IsAlive)) {
        # One task per run, so one line - the run overwrites the file as it takes a task, and the
        # first line is the task it was on when it died.
        $null = $found.Add([pscustomobject]@{
                Path      = $record.Path
                HolderPid = $record.HolderPid
                TaskId    = @($record.Lines)[0]
                Legacy    = $record.Legacy
            })
    }
    return $found.ToArray()
}

<#
.SYNOPSIS
    The claims files of runs that died before releasing their own Doing claims. Task 1b9fed0a.

.DESCRIPTION
    THE SAME SHAPE AS Get-StrandedTaskFile ABOVE, AND FOR THE SAME REASON. `Ready` -> `Doing` is how
    a run claims a task, and every way the workflow ends a task moves it on again — completed,
    parked in Waiting, or handed back. So a task still in `Doing` after its session is gone was
    abandoned mid-task, and the queue never takes `Doing` on purpose: a `Doing` task is one a peer
    session or a person is working right now. Nothing moved one back, and the cost was one task per
    killed run, permanently (astrid-web docs/FIXALL_WORKFLOW.md -> *Doing must not be a dead end*).

    The loop hands each run `ASTRID_FIXALL_CLAIMS_FILE` as `...-claims-<its own pid>.txt` and
    `claim-fixall-task.ts` appends every Ready claim to it. The loop's own after-run pass releases
    whatever is still in Doing and then deletes the file — so A SURVIVING CLAIMS FILE IS A TICK THAT
    WAS KILLED BEFORE IT COULD, which is precisely the case the after-run pass cannot cover, since a
    hard kill is what stops it.

    This needs NO BOARD ID, which is why it is the start-of-tick pass that exists here.
    astrid-web's loop asks the board instead — every `Doing` claim of this agent's with no activity
    for FIXALL_STALE_DOING_MINUTES — and that form of `release-stuck-doing.ts` takes `--list
    <listId>`. This board's id is deliberately nowhere in this repo (docs/AUTOMATION.md: it is
    resolved by NAME, because a hardcoded id fails by answering "nothing to do", which reads exactly
    like a quiet day), so that half waits on a `--board windows` flag filed on the Astrid Web board.

    Liveness is checked rather than assumed, as above: a live loop is mid-run and will do its own
    after-run pass, and releasing a task out from under it would hand somebody's work back to the
    queue while they were still on it.
#>
function Get-StrandedClaimsFile {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$Directory,
        [AllowEmptyString()][string]$LegacyDirectory,
        [AllowEmptyString()][string]$Scope,
        [int]$SelfPid = $PID,
        [scriptblock]$IsAlive = $null
    )

    $found = New-Object System.Collections.ArrayList
    foreach ($record in @(Get-FixallStateRecord -Kind 'claims' -Directory $Directory `
                -LegacyDirectory $LegacyDirectory -Scope $Scope -SelfPid $SelfPid -IsAlive $IsAlive)) {
        # Every line is a claim: `claim-fixall-task.ts` APPENDS, and a run under a cap of one still
        # records a resume claim beside a Ready one.
        $null = $found.Add([pscustomobject]@{
                Path      = $record.Path
                HolderPid = $record.HolderPid
                TaskIds   = @($record.Lines)
                Legacy    = $record.Legacy
            })
    }
    return $found.ToArray()
}

<#
.SYNOPSIS
    Release the Doing claims of runs that are gone. Task 1b9fed0a.

.DESCRIPTION
    THE DECISION IS ALL THAT IS HERE, as everywhere else in this file. What a release DOES —
    keeping the agent assignment so the next run resumes rather than starting over, commenting with
    the branch that carries the task's id, handing the task back to its human creator in `Waiting`
    on a SECOND release rather than retrying it a third time, and never touching a `Doing` task
    assigned to a person — is `astrid-web/lib/doing-release.ts`, reached through
    `scripts/release-stuck-doing.ts`. A second implementation here would drift, and the drift would
    be silent: a loop that releases the wrong thing looks exactly like a loop that releases nothing.

.PARAMETER Release
    Called with one claims file path; returns truthy when the release succeeded. Injected so this
    library stays the clock and the filesystem, nothing else.

.OUTPUTS
    Files   - the claims files acted on
    TaskIds - every id that was in them
    Failed  - the files whose release did not succeed, and which are therefore KEPT
    Summary - one clause for this tick's RESULT line, empty when nothing was stranded
#>
function Invoke-StrandedClaimRelease {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$ClaimsFileDirectory = $env:TEMP,
        # The pre-18ba8c2e location, drained on every tick so a record written before the move is
        # not made invisible by the move itself.
        [AllowEmptyString()][string]$LegacyDirectory,
        # This checkout's own records. Empty takes any, which is what the behaviour was before
        # worker scoping existed.
        [AllowEmptyString()][string]$Scope,
        [int]$SelfPid = $PID,
        [scriptblock]$IsAlive = $null,
        [Parameter(Mandatory)][scriptblock]$Release,
        [scriptblock]$Log = $null
    )

    # A closure under a unique name, for the reason Invoke-StartOfTickSweep spells out below: a
    # scriptblock's free variables resolve in the scope it is INVOKED from, so a sink called $Log
    # can be handed somewhere that has its own $Log and recurse to a call-depth overflow.
    $ReleaseLogSink = $Log
    $say = {
        param([string]$Line)
        if ($ReleaseLogSink) { & $ReleaseLogSink $Line } else { Write-Host $Line }
    }.GetNewClosure()

    $files = New-Object System.Collections.ArrayList
    $ids = New-Object System.Collections.ArrayList
    $failed = New-Object System.Collections.ArrayList

    foreach ($item in @(Get-StrandedClaimsFile -Directory $ClaimsFileDirectory -LegacyDirectory $LegacyDirectory `
                -Scope $Scope -SelfPid $SelfPid -IsAlive $IsAlive)) {
        & $say "  RELEASE: the run that claimed $(@($item.TaskIds) -join ', ') (pid $($item.HolderPid)) is gone - releasing its Doing claim(s)"
        $ok = $false
        # NEVER FATAL TO THE TICK. A release that fails leaves the task in Doing, which is the state
        # it was already in; a release that TAKES THE TICK DOWN costs the whole queue. Same call the
        # script's own header makes.
        try { $ok = [bool](& $Release $item.Path) }
        catch {
            $ok = $false
            & $say "  RELEASE: failed for pid $($item.HolderPid) - $($_.Exception.Message)"
        }
        $null = $files.Add($item.Path)
        foreach ($id in @($item.TaskIds)) { $null = $ids.Add($id) }
        if ($ok) {
            Remove-Item $item.Path -Force -ErrorAction SilentlyContinue
        }
        else {
            # KEPT, deliberately, unlike the task file above. A task file has been reported on either
            # way; a claims file is the only record of WHICH tasks are stranded, and deleting one
            # whose release failed means nothing ever retries them. It clears itself on the first
            # tick that succeeds — including the one where the task has already left Doing, which the
            # script treats as "nothing to release" and exits 0.
            & $say "  RELEASE: keeping $(Split-Path -Leaf $item.Path) so the next tick tries again"
            $null = $failed.Add($item.Path)
        }
    }

    $summary = ''
    if ($files.Count -gt 0) {
        $summary = "released $($ids.Count) abandoned Doing claim(s) from $($files.Count) dead run(s)"
        if ($failed.Count -gt 0) { $summary += ", $($failed.Count) of which could not be reached" }
    }

    return [pscustomobject]@{
        Files   = $files.ToArray()
        TaskIds = $ids.ToArray()
        Failed  = $failed.ToArray()
        Summary = $summary
    }
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
        # As Invoke-StrandedClaimRelease above, and for the same two reasons.
        [AllowEmptyString()][string]$LegacyDirectory,
        [AllowEmptyString()][string]$Scope,
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
    $stranded = @(Get-StrandedTaskFile -Directory $TaskFileDirectory -LegacyDirectory $LegacyDirectory `
            -Scope $Scope -SelfPid $SelfPid -IsAlive $IsAlive)
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
