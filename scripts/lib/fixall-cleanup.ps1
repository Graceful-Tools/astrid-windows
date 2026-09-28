#Requires -Version 5.1
<#
.SYNOPSIS
    Leave the checkout where the next scheduled /fixall tick can use it. Task 6392dfdb.

.DESCRIPTION
    WHY THIS EXISTS. `.claude/commands/fixall.md` works each task on its own branch and merges back
    at the end. A run that dies never gets to the end — the watchdog kills it, or `--max-budget-usd`
    makes `claude` exit 1 wherever it happens to be. On 2026-09-27 that was three times in one day,
    each leaving the working tree dirty; guard 2 in fixall-loop.ps1 then rightly refused every later
    tick, and `8aa5732c` cost seventeen of them over 8½ hours.

    So the wrapper — which holds the session lock, and whose child is by now gone — is the one place
    that may move HEAD. It commits whatever is left as a WIP commit, pushes the branch so the work is
    reviewable, and returns the checkout to main.

    THREE THINGS THIS IS CAREFUL ABOUT.

      * It never commits to main. Work found on main (or on a detached HEAD, which is what a killed
        rebase leaves) goes to a fresh `wip/` branch instead, so a died run cannot put unverified
        code on the branch everything else builds from.
      * The commit says UNFINISHED, UNVERIFIED in its subject, because it has passed no gate. A
        branch whose tip reads that is a RESUME POINT, not something to ship.
      * --no-verify, because the work is unverified by definition: a pre-commit hook would refuse it
        and put the loop back where it started, with a dirty tree nobody will clean up.

    Ported from astrid-web/scripts/fixall-loop.sh, whose cleanup block this mirrors; that repo's
    tests/rules/scheduled-loop-saves-a-killed-run.test.ts is the specification. Kept in its own file
    rather than inline in the loop so that scripts/tests/fixall-loop-cleanup.Tests.ps1 can RUN it
    against a scratch repo — where HEAD and the work end up is the whole behaviour, and no regex over
    a script can tell you that.
#>

# Run one git command against a repo and hand back both halves of the answer. `git -C` rather than
# Push-Location: the caller may be anywhere, and a cleanup that depends on the current directory is
# one more way for a run that already went wrong to go wrong again.
function Invoke-CleanupGit {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$GitArgs
    )
    # LOCAL to this function, because `2>&1` turns a native command's stderr into ErrorRecords and a
    # caller running under 'Stop' - scripts/predeploy.ps1 does - would then take a failed git as
    # terminating. Every failure here is one this function is meant to HANDLE: the push that cannot
    # reach origin is exactly the case where the checkout still has to get back to main.
    $ErrorActionPreference = 'Continue'
    $output = & git -C $RepoRoot @GitArgs 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output   = (($output | Out-String) -replace '\s+$', '')
    }
}

<#
.SYNOPSIS
    Commit, push and unwind whatever a died /fixall run left behind.

.PARAMETER RepoRoot
    The checkout to clean up.

.PARAMETER ClaudeExitCode
    Recorded in the commit body, so the branch says how its run ended.

.PARAMETER WipPrefix
    Branch prefix for work found on main or on a detached HEAD.

.PARAMETER Log
    Called with one line at a time. Defaults to Write-Host; the loop passes its own Say so the lines
    reach the log file too, and the tests pass a sink.

.OUTPUTS
    SavedBranch    - the branch a WIP commit was made on, if any
    PushedBranch   - the branch that reached origin, if any
    UnpushedBranch - the branch left only locally, if the push failed
    ReturnedToMain - whether the checkout is back on main
#>
function Save-UnfinishedWork {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [int]$ClaudeExitCode = 0,
        [string]$WipPrefix = 'wip/fixall-windows',
        [scriptblock]$Log = $null
    )

    $result = [pscustomobject]@{
        SavedBranch    = ''
        PushedBranch   = ''
        UnpushedBranch = ''
        ReturnedToMain = $false
    }

    function Say-Line([string]$Line) {
        if ($Log) { & $Log $Line } else { Write-Host $Line }
    }

    $dirty = (Invoke-CleanupGit -RepoRoot $RepoRoot -GitArgs @('status', '--porcelain')).Output
    $branch = (Invoke-CleanupGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', '--abbrev-ref', 'HEAD')).Output

    if ($dirty) {
        # 'HEAD' is what --abbrev-ref answers when HEAD is detached: no branch to push, and nothing
        # that would survive the checkout back to main, so it is treated exactly like main.
        if ($branch -eq 'main' -or $branch -eq 'HEAD' -or -not $branch) {
            $branch = $WipPrefix + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
            $created = Invoke-CleanupGit -RepoRoot $RepoRoot -GitArgs @('checkout', '-q', '-b', $branch)
            if ($created.ExitCode -ne 0) {
                Say-Line "  WARNING: run left uncommitted changes and $branch could not be created - leaving it for a human"
                return $result
            }
        }

        $message = @"
wip: scheduled /fixall run ended mid-task - UNFINISHED, UNVERIFIED

Saved by scripts/fixall-loop.ps1 (claude exit $ClaudeExitCode) so the checkout can return to main.
predeploy has not been run on this. Continue from here; do not ship it.
"@
        $added = Invoke-CleanupGit -RepoRoot $RepoRoot -GitArgs @('add', '-A')
        $committed = $added
        if ($added.ExitCode -eq 0) {
            $committed = Invoke-CleanupGit -RepoRoot $RepoRoot -GitArgs @('commit', '-q', '--no-verify', '-m', $message)
        }
        if ($committed.ExitCode -eq 0) {
            $result.SavedBranch = $branch
            Say-Line "  run left uncommitted changes - saved as a WIP commit on $branch"
        }
        else {
            Say-Line "  WARNING: run left $branch with uncommitted changes and they could not be committed - leaving it for a human"
            return $result
        }
    }

    # A CLEAN branch that is not main is the other half of the same problem, and the one that cost
    # `95c7a68f` eleven ticks: the run committed its work and died before pushing or merging, so the
    # code existed only here, invisible, while the task sat in Doing.
    $stillDirty = (Invoke-CleanupGit -RepoRoot $RepoRoot -GitArgs @('status', '--porcelain')).Output
    if ($branch -ne 'main' -and -not $stillDirty) {
        $pushed = Invoke-CleanupGit -RepoRoot $RepoRoot -GitArgs @('push', '-q', '-u', 'origin', $branch)
        if ($pushed.ExitCode -eq 0) {
            $result.PushedBranch = $branch
            Say-Line "  run left $branch - pushed it to origin for review"
        }
        else {
            $result.UnpushedBranch = $branch
            Say-Line "  WARNING: run left $branch and it could not be pushed - the work is still in $RepoRoot"
        }
        $back = Invoke-CleanupGit -RepoRoot $RepoRoot -GitArgs @('checkout', '-q', 'main')
        if ($back.ExitCode -eq 0) {
            $result.ReturnedToMain = $true
            Say-Line "  returned to main from $branch"
        }
        else {
            Say-Line "  WARNING: could not return to main from $branch - the next tick will skip on the branch guard"
        }
    }
    elseif ($branch -eq 'main') {
        $result.ReturnedToMain = $true
    }

    return $result
}
