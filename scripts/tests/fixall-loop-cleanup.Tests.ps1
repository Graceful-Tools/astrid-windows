#Requires -Version 5.1
<#
.SYNOPSIS
    A scheduled /fixall run that dies mid-task must not wedge the loop. Task 6392dfdb.

.DESCRIPTION
    THE BUG. Three tasks were left mid-flight on 2026-09-27, all the same way: `claude` hit the
    $10 cap and exited 1 wherever it happened to be — after the code was written, before it was
    committed, gated, pushed or reported. Each time the working tree was left dirty, and guard 2
    in fixall-loop.ps1 then rightly refused every later tick. `8aa5732c` cost seventeen ticks and
    8½ hours that way.

    WHY IT IS RUN, NOT PATTERN-MATCHED. What matters is where HEAD and the work END UP, and a
    regex over the source cannot tell you that. So each case here builds a scratch repo with a
    bare origin, calls Save-UnfinishedWork against it, and reads the answer out of git. Ported
    from astrid-web's tests/rules/scheduled-loop-saves-a-killed-run.test.ts, which does the same
    to the bash loop; that file is the specification.

    WHY NOT PESTER. This machine has only the in-box Pester 3.4, whose syntax is not the one
    anybody writes today, and installing Pester 5 would make the gate depend on a module no
    other machine is promised to have. The whole harness this needs is an assert and a counter.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-loop-cleanup.Tests.ps1
#>
[CmdletBinding()]
param()

# 'Continue', not 'Stop': this file shells out to git constantly and `2>&1` makes a native command's
# stderr an ErrorRecord, so 'Stop' would abort the run on the very cases that exist to prove a git
# failure is handled. What decides the outcome here is the assertion count, not an exception.
$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CleanupLib = Join-Path $RepoRoot 'scripts\lib\fixall-cleanup.ps1'
if (-not (Test-Path $CleanupLib)) {
    Write-Host "FAIL: no cleanup library at $CleanupLib" -ForegroundColor Red
    exit 1
}
. $CleanupLib

$script:Failures = 0
$script:Ran = 0
# How many assertions have been MADE, not how many passed - see Invoke-Case for what that catches.
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

function Invoke-Case([string]$Name, [scriptblock]$Body) {
    $script:Ran++
    Write-Host "  - $Name"
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('fixall-cleanup-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
    $origin = Join-Path $dir 'origin.git'
    $repo = Join-Path $dir 'repo'
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    try {
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

        $before = $script:Checks
        try { & $Body $repo }
        catch {
            Write-Host "    FAIL the case threw: $($_.Exception.Message)" -ForegroundColor Red
            $script:Failures++
        }
        # THE CASE THAT ASSERTS NOTHING IS THE ONE A FAILURE COUNT CANNOT SEE. A terminating error in
        # the code under test abandons the body, so none of its Assert-* lines run, nothing is
        # counted, and the harness prints RESULT: OK over a case that proved nothing. That happened
        # six times in fixall-sweep.Tests.ps1 on its first green run (task 07c3b420), and a silent
        # false OK in the gate that guards the scheduled loop is the same bug as a quiet board.
        if ($script:Checks -eq $before) {
            Write-Host '    FAIL the case made no assertions - it exited early' -ForegroundColor Red
            $script:Failures++
        }
    }
    finally {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Reading git back out of the scratch repo. Trimmed, because a trailing newline is not a finding.
function Git-In([string]$Repo, [string[]]$GitArgs) {
    $out = & git -C $Repo @GitArgs 2>&1
    return (($out | Out-String) -replace '\s+$', '')
}

Write-Host ''
Write-Host 'the scheduled loop saves a died run instead of wedging (task 6392dfdb)' -ForegroundColor Cyan

Invoke-Case 'commits uncommitted work on the task branch, pushes it, and returns to main' {
    param($repo)
    & git -C $repo checkout -q -b 'fix/8aa5732c-quick-add' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $repo 'a.txt') -Value 'half done' -Encoding utf8 -NoNewline
    Set-Content -Path (Join-Path $repo 'new.rs') -Value 'fn main() {}' -Encoding utf8 -NoNewline

    $saved = Save-UnfinishedWork -RepoRoot $repo -ClaudeExitCode 1 -Log { param($m) }

    Assert-Equal 'HEAD is back on main' 'main' (Git-In $repo @('rev-parse', '--abbrev-ref', 'HEAD'))
    Assert-Equal 'the tree is clean' '' (Git-In $repo @('status', '--porcelain'))
    Assert-Match 'the branch tip is marked unfinished' 'UNFINISHED, UNVERIFIED' (Git-In $repo @('log', '-1', '--format=%s', 'origin/fix/8aa5732c-quick-add'))
    Assert-Equal 'the edit reached origin' 'half done' (Git-In $repo @('show', 'origin/fix/8aa5732c-quick-add:a.txt'))
    Assert-Equal 'the new file reached origin' 'fn main() {}' (Git-In $repo @('show', 'origin/fix/8aa5732c-quick-add:new.rs'))
    Assert-Equal 'it reports the branch it saved' 'fix/8aa5732c-quick-add' $saved.SavedBranch
    Assert-Equal 'it reports the branch it pushed' 'fix/8aa5732c-quick-add' $saved.PushedBranch
}

Invoke-Case 'never commits to main - work left on main goes to a wip/ branch' {
    param($repo)
    Set-Content -Path (Join-Path $repo 'a.txt') -Value 'dirtied on main' -Encoding utf8 -NoNewline

    $saved = Save-UnfinishedWork -RepoRoot $repo -ClaudeExitCode 1 -Log { param($m) }

    Assert-Equal 'HEAD is back on main' 'main' (Git-In $repo @('rev-parse', '--abbrev-ref', 'HEAD'))
    Assert-Equal 'the tree is clean' '' (Git-In $repo @('status', '--porcelain'))
    Assert-Equal 'main did not gain the commit' 'init' (Git-In $repo @('log', '-1', '--format=%s', 'main'))
    Assert-Equal 'origin/main is untouched' 'one' (Git-In $repo @('show', 'origin/main:a.txt'))
    Assert-Match 'the work went to a wip/ branch' '^wip/fixall-windows-\d{8}-\d{6}$' $saved.SavedBranch
    Assert-Equal 'and that branch reached origin' 'dirtied on main' (Git-In $repo @('show', ('origin/' + $saved.SavedBranch + ':a.txt')))
}

Invoke-Case 'leaves a clean main alone' {
    param($repo)
    $saved = Save-UnfinishedWork -RepoRoot $repo -ClaudeExitCode 0 -Log { param($m) }

    Assert-Equal 'HEAD is still main' 'main' (Git-In $repo @('rev-parse', '--abbrev-ref', 'HEAD'))
    Assert-Equal 'no commit was invented' 'init' (Git-In $repo @('log', '-1', '--format=%s'))
    Assert-Equal 'nothing was saved' '' $saved.SavedBranch
    Assert-Equal 'nothing was pushed' '' $saved.PushedBranch
}

# The 09:40 case: the run committed its work and was killed before it could push or merge. Nothing
# to commit, but leaving HEAD there is what made `95c7a68f` invisible for eleven ticks.
Invoke-Case 'pushes a clean task branch the run never got to merge, and returns to main' {
    param($repo)
    & git -C $repo checkout -q -b 'fix/95c7a68f-landing' 2>&1 | Out-Null
    Set-Content -Path (Join-Path $repo 'a.txt') -Value 'finished but unpushed' -Encoding utf8 -NoNewline
    & git -C $repo add -A 2>&1 | Out-Null
    & git -C $repo commit -q -m 'the run committed this and died' 2>&1 | Out-Null

    $saved = Save-UnfinishedWork -RepoRoot $repo -ClaudeExitCode 1 -Log { param($m) }

    Assert-Equal 'HEAD is back on main' 'main' (Git-In $repo @('rev-parse', '--abbrev-ref', 'HEAD'))
    Assert-Equal 'no WIP commit was invented over a clean tree' '' $saved.SavedBranch
    Assert-Equal 'the branch was pushed anyway' 'fix/95c7a68f-landing' $saved.PushedBranch
    Assert-Equal 'the work is reviewable on origin' 'finished but unpushed' (Git-In $repo @('show', 'origin/fix/95c7a68f-landing:a.txt'))
}

# A detached HEAD is not a branch anybody can push, and it is what a killed rebase or a checked-out
# sha leaves behind. It must be treated like main: the work goes to a wip/ branch.
Invoke-Case 'saves work left on a detached HEAD to a wip/ branch' {
    param($repo)
    $head = Git-In $repo @('rev-parse', 'HEAD')
    & git -C $repo checkout -q $head 2>&1 | Out-Null
    Set-Content -Path (Join-Path $repo 'a.txt') -Value 'dirtied while detached' -Encoding utf8 -NoNewline

    $saved = Save-UnfinishedWork -RepoRoot $repo -ClaudeExitCode 1 -Log { param($m) }

    Assert-Match 'the work went to a wip/ branch' '^wip/fixall-windows-' $saved.SavedBranch
    Assert-Equal 'HEAD is back on main' 'main' (Git-In $repo @('rev-parse', '--abbrev-ref', 'HEAD'))
    Assert-Equal 'the tree is clean' '' (Git-In $repo @('status', '--porcelain'))
}

# A push that fails must still leave the checkout usable: the whole point is that the next tick is
# not blocked. The work stays on the local branch and the loop says so.
Invoke-Case 'returns to main even when the push fails' {
    param($repo)
    & git -C $repo checkout -q -b 'fix/no-remote' 2>&1 | Out-Null
    & git -C $repo remote set-url origin (Join-Path $repo '..\missing.git') 2>&1 | Out-Null
    Set-Content -Path (Join-Path $repo 'a.txt') -Value 'half done' -Encoding utf8 -NoNewline

    $saved = Save-UnfinishedWork -RepoRoot $repo -ClaudeExitCode 1 -Log { param($m) }

    Assert-Equal 'the work was still committed' 'fix/no-remote' $saved.SavedBranch
    Assert-Equal 'nothing is claimed to be on origin' '' $saved.PushedBranch
    Assert-Equal 'the failed push is named' 'fix/no-remote' $saved.UnpushedBranch
    Assert-Equal 'HEAD is back on main' 'main' (Git-In $repo @('rev-parse', '--abbrev-ref', 'HEAD'))
    Assert-Equal 'the tree is clean' '' (Git-In $repo @('status', '--porcelain'))
}

# --no-verify, because the work is by definition unverified: a pre-commit hook would refuse it and
# put the loop back where it started, with a dirty tree nobody will clean up.
Invoke-Case 'bypasses a pre-commit hook, which would refuse unverified work' {
    param($repo)
    & git -C $repo checkout -q -b 'fix/hooked' 2>&1 | Out-Null
    $hooks = Git-In $repo @('rev-parse', '--git-path', 'hooks')
    $hook = Join-Path $repo $hooks
    if (-not (Test-Path $hook)) { New-Item -ItemType Directory -Path $hook -Force | Out-Null }
    Set-Content -Path (Join-Path $hook 'pre-commit') -Value "#!/bin/sh`nexit 1`n" -Encoding ascii
    Set-Content -Path (Join-Path $repo 'a.txt') -Value 'half done' -Encoding utf8 -NoNewline

    $saved = Save-UnfinishedWork -RepoRoot $repo -ClaudeExitCode 1 -Log { param($m) }

    Assert-Equal 'the hook did not stop the save' 'fix/hooked' $saved.SavedBranch
    Assert-Equal 'the tree is clean' '' (Git-In $repo @('status', '--porcelain'))
}

Write-Host ''
Write-Host 'the loop takes one task per run and can name the task it took' -ForegroundColor Cyan

# These four are read off the source, because they are contracts between two processes rather than
# behaviour one of them can be run to demonstrate. The env vars are how the loop and the agent agree;
# docs/AUTOMATION.md is where the agreement is written down, and a variable nobody documents is a
# variable the next change silently drops.
$Loop = Get-Content (Join-Path $RepoRoot 'scripts\fixall-loop.ps1') -Raw
$Automation = Get-Content (Join-Path $RepoRoot 'docs\AUTOMATION.md') -Raw

$script:Ran++
Write-Host '  - caps the run at one task so it fits inside the watchdog'
Assert-Match 'the loop exports the cap' '\$env:ASTRID_FIXALL_MAX_TASKS\s*=' $Loop
Assert-Match 'and it defaults to one task' 'FIXALL_MAX_TASKS.*\}\s*else\s*\{\s*1\s*\}' ($Loop -replace '\r?\n', ' ')
Assert-Match 'and AUTOMATION.md says so' 'ASTRID_FIXALL_MAX_TASKS' $Automation

$script:Ran++
Write-Host '  - gives one task a watchdog it can finish inside'
Assert-Match 'the watchdog default is 75 minutes' 'FIXALL_MAX_MINUTES.*\}\s*else\s*\{\s*75\s*\}' ($Loop -replace '\r?\n', ' ')

$script:Ran++
Write-Host '  - hands the run a file to name the task it takes, so a died run can be reported on it'
Assert-Match 'the loop exports the path' '\$env:ASTRID_FIXALL_TASK_FILE\s*=' $Loop
Assert-Match 'and reports on that task when the run dies' 'Send-DiedRunNote' $Loop
Assert-Match 'and AUTOMATION.md says so' 'ASTRID_FIXALL_TASK_FILE' $Automation

Write-Host ''
if ($script:Failures -gt 0) {
    Write-Host ("RESULT: FAILED - {0} assertion(s) across {1} case(s)" -f $script:Failures, $script:Ran) -ForegroundColor Red
    exit 1
}
Write-Host ("RESULT: OK - {0} case(s)" -f $script:Ran) -ForegroundColor Green
exit 0
