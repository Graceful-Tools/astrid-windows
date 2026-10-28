#Requires -Version 5.1
<#
.SYNOPSIS
    Doing must not be a dead end: the loop releases its own abandoned claims. Task 1b9fed0a.

.DESCRIPTION
    WHAT WAS BROKEN. `Ready` -> `Doing` is how a run claims a task, and every way the workflow ends
    one moves it on again: completed, parked in `Waiting`, or handed back. So a task still in `Doing`
    after its session is gone was abandoned mid-task - and the queue never takes `Doing`, on purpose,
    because a `Doing` task is one a peer session or a person is working right now. Nothing here ever
    moved one back.

    `astrid-web/docs/FIXALL_WORKFLOW.md` -> *Doing must not be a dead end* names the backstop, and
    `astrid-web/scripts/fixall-loop.sh` calls it twice (lines 295 and 556). This loop called it
    neither time: `grep -n "release-stuck-doing\|STALE_DOING\|CLAIMS_FILE"` over fixall-loop.ps1
    returned nothing, and `claim-fixall-task.ts` only records a claim when
    `ASTRID_FIXALL_CLAIMS_FILE` is in its environment, which nothing exported. The cost was one task
    per killed run, for ever: on 2026-10-18 "[core] Decode web's wire-shapes.json ..." had sat in
    `Doing` since 01:25 with nothing live in `../astrid-core`, and every tick since reported it as
    "in progress elsewhere; not touched" - which is what the sweep is supposed to say about a LIVE
    peer.

    THREE PASSES, EACH REACHING A CLAIM THE OTHERS CANNOT. Two of them are claims-file passes and
    one reads the board.

      * The AFTER-RUN pass takes `--claims-file` and nothing else, so it is exact: it releases
        precisely what this run claimed and left behind, no heuristic and no board read.
      * The START-OF-TICK CLAIMS pass reads a claims file whose owning loop is GONE, which is the
        same shape as `Get-StrandedTaskFile`: the loop hands each run a claims file named for its
        OWN pid. That covers the killed-loop case - the case the after-run pass structurally cannot
        cover, since a hard kill is what stops it. It needs no board id either.
      * The START-OF-TICK BOARD pass (task 1cc6ac2f) is the only one that can reach a claim in NO
        claims file: one made by an interactive session, or by a session that died before a claims
        file was ever written. `f8dfc84e` sat in Doing from 01:25 on 2026-10-18 for exactly that
        reason and had to be released by hand.

    WHY THE BOARD PASS COULD NOT BE WRITTEN UNTIL NOW. It used to need `--list <listId>`, and this
    board's id is deliberately nowhere in this repo (docs/AUTOMATION.md - it is resolved by NAME,
    because a hardcoded id fails by answering "nothing to do", which reads exactly like a quiet
    day). AWTD-1267 added `release-stuck-doing.ts --board <web|ios|windows|android>`, which resolves
    the board through `GET /api/v1/lists` and exits 1 rather than reporting an empty board when the
    name is not found. With that merged to astrid-web `main` the pass costs this repo no id at all,
    which is the whole of what this task was waiting for.

    WHY THE WIRING IS ASSERTED OVER THE PARSED AST, like fixall-loop-guards.Tests.ps1 and unlike
    fixall-sweep.Tests.ps1: the release itself is a shell-out to astrid-web's tsx and a branch on an
    exit code, and running it would mean a sibling checkout, a node process and real writes to the
    real board inside the gate. What regressed here is WIRING - an absent call and an unexported
    environment variable - so that is what this checks, structurally, where ordering is an offset
    comparison rather than a regex over a region.

    AND THE BEHAVIOUR THAT IS OURS IS RUN FOR REAL. Which claims files belong to a dead run, and what
    happens when a release fails, are decisions this repo makes; they get scratch directories and
    real files. What a release DOES - keeping the agent assignment, naming the branch that carries
    the work, handing back to a human on a second release, never touching a task assigned to a
    person - is `astrid-web/lib/doing-release.ts` and is tested there. The one thing this file
    asserts about that is that it is DELEGATED: a loop that wrote statuses itself would have two
    implementations of "release", and the second would drift.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-loop-doing.Tests.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LoopPath = Join-Path $RepoRoot 'scripts\fixall-loop.ps1'
$SweepLib = Join-Path $RepoRoot 'scripts\lib\fixall-sweep.ps1'
foreach ($required in @($LoopPath, $SweepLib)) {
    if (-not (Test-Path $required)) {
        Write-Host "FAIL: missing $required" -ForegroundColor Red
        exit 1
    }
}

. $SweepLib

$script:Failures = 0
$script:Ran = 0
# Assertions MADE, not passed: a case that returns early makes none, and that is the one outcome a
# failure count cannot see.
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

# A scratch directory per case, so one case's leftovers cannot satisfy another's assertion.
function New-ScratchDirectory {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ('fixall-doing-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

function New-ClaimsFile([string]$Directory, [int]$HolderPid, [string[]]$TaskIds) {
    $path = Join-Path $Directory "astrid-fixall-windows-claims-$HolderPid.txt"
    Set-Content -Path $path -Value ($TaskIds -join "`n") -Encoding utf8
    return $path
}

# ── The loop, parsed ──────────────────────────────────────────────────────────────────────────
$tokens = $null
$errors = $null
$Ast = [System.Management.Automation.Language.Parser]::ParseFile($LoopPath, [ref]$tokens, [ref]$errors)
if ($errors -and $errors.Count -gt 0) {
    Write-Host "FAIL: scripts/fixall-loop.ps1 does not parse ($($errors.Count) error(s)):" -ForegroundColor Red
    $errors | Select-Object -First 5 | ForEach-Object { Write-Host "    $($_.Message)" -ForegroundColor Red }
    exit 1
}
$Commands = @($Ast.FindAll(
        { param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))
$Assignments = @($Ast.FindAll(
        { param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))

function Find-LoopCommands([string[]]$AllOf) {
    $found = New-Object System.Collections.ArrayList
    foreach ($cmd in $Commands) {
        $text = $cmd.Extent.Text
        $ok = $true
        foreach ($needle in $AllOf) { if ($text -notmatch $needle) { $ok = $false; break } }
        if ($ok) { $null = $found.Add($cmd) }
    }
    return $found.ToArray()
}
function Find-LoopCommand([string[]]$AllOf) {
    $all = @(Find-LoopCommands $AllOf)
    if ($all.Count -eq 0) { return $null }
    return $all[0]
}

$ReleaseTool = Find-LoopCommand @('release-stuck-doing\.ts')
$ReleaseCalls = @(Find-LoopCommands @('^Invoke-DoingRelease\b'))
$StrandedRelease = Find-LoopCommand @('Invoke-StrandedClaimRelease')
# Guard 3. `agent-queue-status.ts` since task 18ba8c2e, which is also what made it the INBOX guard
# rather than the Ready-only one; the board pass still has to come before it, for the reason the
# cases below give. Its node is the Get-WakeVerdict call that wraps the shell-out, so its extent
# starts where the guard does.
$QueueGuard = Find-LoopCommand @('agent-queue-status\.ts')
$StartRun = Find-LoopCommand @('Start-Process')
$SaveWork = Find-LoopCommand @('Save-UnfinishedWork', '\$RepoRoot')
# The board pass, and the two guards it has to precede. Task 1cc6ac2f.
$BoardRelease = Find-LoopCommand @('^Invoke-DoingRelease\b', '-Board')
# The helper itself, because the flags are now built across two statements - a selector chosen per
# pass, then one shell-out that concatenates it with the shared arguments. Asserting the flags over
# the single command node would only ever see the shared half.
$ReleaseFn = @($Ast.FindAll({
            param($n)
            $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $n.Name -eq 'Invoke-DoingRelease'
        }, $true)) | Select-Object -First 1
$TreeGuard = Find-LoopCommand @('Get-CheckoutGuardVerdict')
# The whole file as text, for the two things that are not commands: the parameter default that
# carries the env fallback, and the absence of a hardcoded board id anywhere in it.
$LoopText = Get-Content $LoopPath -Raw
$ClaimsExport = @($Assignments | Where-Object { $_.Left.Extent.Text -match 'env:ASTRID_FIXALL_CLAIMS_FILE' }) |
    Select-Object -First 1
# The path the export names, which is where the pid actually is. Asserted separately from the export
# so a claims file shared by every tick on the machine - the bug that would make a live run's claims
# look stranded - fails here rather than passing because the export line happened to mention $PID.
#
# Found by the LIBRARY CALL that builds it rather than by the old literal filename: since task
# 18ba8c2e the record is durable and worker-scoped, and its name is Get-FixallStateFile's
# (scripts/tests/fixall-state.Tests.ps1 is where that shape is specified).
$ClaimsPath = @($Assignments | Where-Object {
        $_.Right.Extent.Text -match "Get-FixallStateFile" -and $_.Right.Extent.Text -match "'claims'" }) |
    Select-Object -First 1

Write-Host ''
Write-Host 'the loop releases its own abandoned Doing claims (task 1b9fed0a)' -ForegroundColor Cyan

Invoke-Case 'the loop exports ASTRID_FIXALL_CLAIMS_FILE before starting the run' {
    # claim-fixall-task.ts appends a task id here only when the variable is set. Without it the
    # after-run pass has nothing to read and can release nothing, however correctly it is called.
    Assert-True 'the claims file is exported' ($null -ne $ClaimsExport)
    if (-not $ClaimsExport -or -not $StartRun) { return }
    Assert-True 'before the claude child is started' ($ClaimsExport.Extent.StartOffset -lt $StartRun.Extent.StartOffset)
    # Named for the loop's own pid, like the task file beside it: that is what lets a LATER tick tell
    # a dead run's claims from a live one's. One file shared by every tick would make a live run's
    # claims look stranded, and the start-of-tick pass would release a task out from under it.
    Assert-True 'the claims file has a path of its own' ($null -ne $ClaimsPath)
    if ($ClaimsPath) { Assert-Match 'named for this loop' '\$PID' $ClaimsPath.Right.Extent.Text }
}

Invoke-Case 'the release is delegated to astrid-web, not reimplemented' {
    Assert-True 'the loop calls release-stuck-doing.ts' ($null -ne $ReleaseTool)
    if (-not $ReleaseTool) { return }
    $shared = $ReleaseTool.Extent.Text
    Assert-Match 'as this harness' "'claude'" $shared
    # --repo is where the task branches live, so the release comment can name the branch carrying the
    # work; without it the script would read astrid-web's branches instead of this repo's. Shared by
    # every pass, so it belongs on the one shell-out rather than in a selector.
    Assert-Match 'pointed at THIS repo for the branch names' "--repo" $shared
    Assert-Match 'by path' '\$RepoRoot' $shared

    Assert-True 'the helper that builds its argv is there' ($null -ne $ReleaseFn)
    if (-not $ReleaseFn) { return }
    $fn = $ReleaseFn.Extent.Text
    Assert-Match 'it selects over the claims this run recorded' "--claims-file" $fn
    # And over the board, for a claim that is in no claims file at all (task 1cc6ac2f).
    Assert-Match 'and over the board for claims no file records' '--board' $fn
    Assert-Match 'with a staleness bound rather than releasing live work' '--stale-minutes' $fn
    # STILL ONE call site for the tool. Three callers build one argv between them; a second shell-out
    # is where --agent, --repo or the output handling drifts out of step with the first.
    $shellOuts = @(Find-LoopCommands @('release-stuck-doing\.ts'))
    Assert-Equal 'from exactly one place in the loop' 1 $shellOuts.Count
    # Keeping the agent assignment, naming the branch and handing back on a second release are
    # lib/doing-release.ts's. A loop that wrote statuses itself would be a second implementation.
    $own = @(Find-LoopCommands @('set-task-status\.ts'))
    Assert-Equal 'and the loop writes no statuses of its own' 0 $own.Count
}

Invoke-Case 'the after-run pass releases what this run left in Doing' {
    Assert-True 'the release is called at all' ($ReleaseCalls.Count -gt 0)
    if ($ReleaseCalls.Count -eq 0 -or -not $StartRun -or -not $SaveWork) { return }
    $after = @($ReleaseCalls | Where-Object { $_.Extent.StartOffset -gt $StartRun.Extent.StartOffset }) |
        Select-Object -First 1
    Assert-True 'after the child has exited' ($null -ne $after)
    if (-not $after) { return }
    # After the save, which is what pushes the branch: the release comments with the branch that
    # carries the task's id, so it has to run once that branch exists.
    Assert-True 'and after the unfinished work is saved and pushed' ($after.Extent.StartOffset -gt $SaveWork.Extent.StartOffset)

    # ON THE WATCHDOG PATH TOO. A killed run is the likeliest way a claim is abandoned, and that
    # branch ends the tick with Finish - so a release placed after it would never run in the one
    # case it exists for.
    $killBranch = @($Ast.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.IfStatementAst] }, $true) |
        Where-Object { $_.Extent.Text -match '\$killed' -and $_.Extent.Text -match 'Finish' }) |
        Select-Object -First 1
    Assert-True 'the watchdog branch still ends the tick' ($null -ne $killBranch)
    if (-not $killBranch) { return }
    Assert-True 'and the release runs before it' ($after.Extent.StartOffset -lt $killBranch.Extent.StartOffset)
}

Invoke-Case 'the start-of-tick pass runs before the is-there-work guard' {
    Assert-True 'the loop sweeps stranded claims files' ($null -ne $StrandedRelease)
    if (-not $StrandedRelease -or -not $QueueGuard) { return }
    # Before guard 3, so a task released this tick counts as work for this tick rather than waiting
    # half an hour for the next one.
    Assert-True 'before guard 3 counts the queue' ($StrandedRelease.Extent.StartOffset -lt $QueueGuard.Extent.StartOffset)
    # Inside the main try, whose finally releases the working-tree lock: a skip from outside it would
    # leak the lock and the next tick would refuse this checkout.
    $main = @($Ast.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.TryStatementAst] }, $true) |
        Where-Object {
            $_.Body.Extent.StartOffset -lt $StrandedRelease.Extent.StartOffset -and
            $_.Body.Extent.EndOffset -gt $StrandedRelease.Extent.EndOffset
        }) | Select-Object -First 1
    Assert-True 'and inside the try that releases the lock' ($null -ne $main)
    if ($main) { Assert-Match 'by calling the release closure' 'releaseLock' $main.Finally.Extent.Text }
}

Write-Host ''
Write-Host 'the board pass reaches a claim that is in no claims file (task 1cc6ac2f)' -ForegroundColor Cyan

Invoke-Case 'the loop asks the board, not just its own claims files' {
    # The case neither claims-file pass can cover: a claim made by an interactive session, or by a
    # session that died before a claims file was ever written. f8dfc84e sat in Doing all of
    # 2026-10-18 for that reason. Without this call the loop is blind to it for ever.
    Assert-True 'the board pass is called' ($null -ne $BoardRelease)
    if (-not $BoardRelease) { return }
    $text = $BoardRelease.Extent.Text
    # BY NAME. The whole reason this task waited for AWTD-1267 is that `--list <id>` would have put
    # account data in this repo, and a stale id fails by answering "nothing to do".
    Assert-Match 'naming this board' "windows" $text
    Assert-Match 'with a staleness bound' '-StaleMinutes' $text
}

Invoke-Case 'the board id stays out of this repo' {
    # docs/AUTOMATION.md: resolved by NAME, never by a hardcoded id, because a stale id fails by
    # returning an empty list - which reads exactly like a quiet day. Asserted over the whole loop
    # rather than the one call, since the id would be just as wrong in a variable at the top.
    $uuid = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
    $hits = @([regex]::Matches($LoopText, $uuid) | ForEach-Object { $_.Value })
    Assert-Equal 'no list id is written down anywhere in the loop' '' (@($hits) -join ',')
}

Invoke-Case 'the board pass runs before both of the remaining guards' {
    if (-not $BoardRelease) { Assert-True 'the board pass is called' $false; return }
    # Before guard 3, so a task released on THIS tick counts as work for this tick rather than
    # waiting half an hour for the next one.
    Assert-True 'before guard 3 counts the queue' ($null -ne $QueueGuard)
    if ($QueueGuard) {
        Assert-True 'and the release comes first' ($BoardRelease.Extent.StartOffset -lt $QueueGuard.Extent.StartOffset)
    }
    # Before guard 2 as well, deliberately: this is a board operation, not a tree one, and a dirty
    # checkout is no reason to leave somebody's task stranded in Doing.
    Assert-True 'before the dirty-checkout guard too' ($null -ne $TreeGuard)
    if ($TreeGuard) {
        Assert-True 'which can end the tick' ($BoardRelease.Extent.StartOffset -lt $TreeGuard.Extent.StartOffset)
    }
}

Invoke-Case 'a failed board release does not stop the tick' {
    if (-not $BoardRelease) { Assert-True 'the board pass is called' $false; return }
    # A release that cannot reach the board leaves the task exactly where it already was, which is
    # no worse than not having tried. Ending the tick on it would turn a board outage into a day of
    # no work at all.
    $statement = @($Assignments | Where-Object {
            $_.Extent.StartOffset -le $BoardRelease.Extent.StartOffset -and
            $_.Extent.EndOffset -ge $BoardRelease.Extent.EndOffset
        } | Sort-Object { $_.Extent.EndOffset - $_.Extent.StartOffset } | Select-Object -First 1)
    Assert-True 'its verdict is kept rather than discarded' ($statement.Count -eq 1)
    if ($statement.Count -ne 1) { return }
    Assert-Equal 'and the tick does not end on it' $false ([bool]($statement[0].Extent.Text -match '\bFinish\b'))
}

Invoke-Case 'the staleness bound is configurable and sits past the watchdog' {
    # 180 by default, and the watchdog kills a run at 75: a bound inside the watchdog window would
    # release a task out from under a session that is still working it.
    Assert-Match 'the env knob the Mac loop uses is read here too' 'FIXALL_STALE_DOING_MINUTES' $LoopText
    Assert-Match 'with the same default' 'FIXALL_STALE_DOING_MINUTES[^\r\n]*\b180\b' $LoopText
}

Invoke-Case 'a claims file outlives the tick only when its release failed' {
    # The task file is deleted unconditionally - it has been reported on either way. A claims file is
    # different: deleting one whose release FAILED throws away the only record of which tasks are
    # stranded, and nothing would ever retry them.
    $main = @($Ast.FindAll(
            { param($n) $n -is [System.Management.Automation.Language.TryStatementAst] }, $true) |
        Where-Object { $_.Finally -and $_.Finally.Extent.Text -match 'releaseLock' }) | Select-Object -First 1
    Assert-True 'the loop has a finally that releases the lock' ($null -ne $main)
    if (-not $main) { return }
    Assert-Match 'and it removes the claims file only when the release succeeded' 'claimsReleased' $main.Finally.Extent.Text
}

Write-Host ''
Write-Host 'which claims files belong to a run that is gone' -ForegroundColor Cyan

Invoke-Case 'a dead loop pid claims file is stranded; a live one is not' {
    $dir = New-ScratchDirectory
    try {
        $dead = New-ClaimsFile $dir 424242 @('aaaaaaaa-0000-0000-0000-000000000001')
        $live = New-ClaimsFile $dir 525252 @('bbbbbbbb-0000-0000-0000-000000000002')
        $mine = New-ClaimsFile $dir 626262 @('cccccccc-0000-0000-0000-000000000003')
        $found = @(Get-StrandedClaimsFile -Directory $dir -SelfPid 626262 -IsAlive { param($p) $p -eq 525252 })
        Assert-Equal 'one file is stranded' 1 $found.Count
        if ($found.Count -ne 1) { return }
        Assert-Equal 'the dead loop''s' $dead $found[0].Path
        Assert-Equal 'with its holder pid' 424242 $found[0].HolderPid
        Assert-Equal 'and the ids it claimed' 'aaaaaaaa-0000-0000-0000-000000000001' (@($found[0].TaskIds) -join ',')
        # A live loop is mid-run and will do its own after-run pass; our own file is this very tick's.
        Assert-True 'the live loop''s file is untouched' (Test-Path $live)
        Assert-True 'and so is our own' (Test-Path $mine)
    }
    finally { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'an empty claims file is cleared rather than reported for ever' {
    $dir = New-ScratchDirectory
    try {
        $empty = New-ClaimsFile $dir 424242 @()
        $found = @(Get-StrandedClaimsFile -Directory $dir -SelfPid 1 -IsAlive { param($p) $false })
        Assert-Equal 'a run that claimed nothing strands nothing' 0 $found.Count
        Assert-False 'and its file does not survive to be looked at again' (Test-Path $empty)
    }
    finally { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a missing directory is quiet, not an error' {
    $found = @(Get-StrandedClaimsFile -Directory (Join-Path ([System.IO.Path]::GetTempPath()) 'no-such-fixall-dir') -SelfPid 1 -IsAlive { param($p) $false })
    Assert-Equal 'nothing stranded' 0 $found.Count
}

Write-Host ''
Write-Host 'releasing a dead run''s claims, and surviving a release that fails' -ForegroundColor Cyan

Invoke-Case 'a dead run''s claims are released and its file cleared' {
    $dir = New-ScratchDirectory
    try {
        $dead = New-ClaimsFile $dir 424242 @('aaaaaaaa-0000-0000-0000-000000000001', 'aaaaaaaa-0000-0000-0000-000000000002')
        $seen = New-Object System.Collections.ArrayList
        $result = Invoke-StrandedClaimRelease -ClaimsFileDirectory $dir -SelfPid 1 `
            -IsAlive { param($p) $false } `
            -Release { param($Path) $null = $seen.Add($Path); return $true } `
            -Log { param($Line) }
        Assert-Equal 'the release was asked once' 1 $seen.Count
        Assert-Equal 'for the dead run''s file' $dead (@($seen) -join ',')
        Assert-Equal 'both claimed ids are reported' 2 (@($result.TaskIds).Count)
        Assert-Equal 'nothing failed' 0 (@($result.Failed).Count)
        Assert-False 'and the file is cleared so no later tick repeats it' (Test-Path $dead)
        Assert-Match 'the RESULT line says what moved' 'abandoned' $result.Summary
    }
    finally { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a release that fails keeps the file and does not fail the tick' {
    $dir = New-ScratchDirectory
    try {
        $dead = New-ClaimsFile $dir 424242 @('aaaaaaaa-0000-0000-0000-000000000001')
        $result = Invoke-StrandedClaimRelease -ClaimsFileDirectory $dir -SelfPid 1 `
            -IsAlive { param($p) $false } `
            -Release { param($Path) return $false } `
            -Log { param($Line) }
        Assert-Equal 'the failure is reported' 1 (@($result.Failed).Count)
        # Kept, so the next tick tries again. A release that cannot reach the board leaves the task
        # exactly where it already was, which is the state this whole file is about.
        Assert-True 'the file survives for the next tick' (Test-Path $dead)
    }
    finally { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a release that throws is no more fatal than one that fails' {
    $dir = New-ScratchDirectory
    try {
        $dead = New-ClaimsFile $dir 424242 @('aaaaaaaa-0000-0000-0000-000000000001')
        $result = Invoke-StrandedClaimRelease -ClaimsFileDirectory $dir -SelfPid 1 `
            -IsAlive { param($p) $false } `
            -Release { param($Path) throw 'no network' } `
            -Log { param($Line) }
        Assert-Equal 'the throw became a reported failure' 1 (@($result.Failed).Count)
        Assert-True 'the file survives for the next tick' (Test-Path $dead)
    }
    finally { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'nothing stranded costs nothing and says nothing' {
    $dir = New-ScratchDirectory
    try {
        $asked = 0
        $result = Invoke-StrandedClaimRelease -ClaimsFileDirectory $dir -SelfPid 1 `
            -IsAlive { param($p) $false } `
            -Release { param($Path) $asked++; return $true } `
            -Log { param($Line) }
        Assert-Equal 'the release was never asked' 0 $asked
        Assert-Equal 'and the RESULT line gains no clause' '' $result.Summary
    }
    finally { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ''
if ($script:Checks -lt 40) {
    Write-Host "FAIL: only $($script:Checks) assertion(s) ran - this file is meant to make more" -ForegroundColor Red
    $script:Failures++
}
if ($script:Failures -gt 0) {
    Write-Host "RESULT: FAILED - $($script:Failures) failure(s) in $($script:Ran) case(s)" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: OK - $($script:Ran) case(s), $($script:Checks) assertion(s)" -ForegroundColor Green
exit 0
