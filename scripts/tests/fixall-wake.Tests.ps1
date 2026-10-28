#Requires -Version 5.1
<#
.SYNOPSIS
    The Windows loop must wake for Astrid REPLIES, not only for Ready tasks, and must acknowledge
    what woke it durably. Task 18ba8c2e.

.DESCRIPTION
    THREE BUGS, and the first of them is the whole reason this loop looked healthy while ignoring
    Jon.

    1. GUARD 3 COULD NOT SEE THE INBOX. It asked `ready-tasks.ts windows --json`, which answers
       Ready ∩ lane work and nothing else, so a comment on a task the loop itself had moved to
       `Doing` — or on one it had finished — woke no tick, ever. `fixall-loop.ps1`'s own header
       admitted it and named the reason: *"That script requires a LIST ID, and this board's id is
       deliberately nowhere in this repo."* That reason expired. `agent-queue-status.ts` resolves
       `--board windows` by name through `scripts/lib/board-by-name.ts` (AWTD-1383), refusing a
       duplicate name rather than taking the first match, so the richer preflight — queue +
       attention + lanes, one request — is now reachable without an id in this repo.

       `attention` is half of what the shared workflow calls the engagement contract: *"queue is
       what to work; attention is what has been said to this agent and not answered."* A loop that
       reads only the first is deaf by construction.

    2. UNKNOWN MUST NOT READ AS IDLE. The preflight's exit codes are its interface: 0 work,
       3 nothing to do, 1 could not tell. A queue nobody can see is a reason to RUN and let the
       agent report, never a reason to go quiet — that confusion is how a loop spends weeks
       looking like a quiet board.

    3. ACKNOWLEDGEMENT HAS TO SURVIVE THE RUN IT DESCRIBES. Waking is two-phase on purpose: the
       preflight computes the keys and writes nothing (`--no-write-seen`), and the run's OUTCOME
       records them (`--mark-seen`, plus `--failed` for a run that died, which gives each key a
       strike instead of muting it outright). Two ways to get that wrong, both silent:

         * RECOMPUTING the keys at acknowledgement time. The run has by then answered things, so
           the recomputed set differs from the set that actually woke the tick, and the difference
           is exactly the items that get muted without ever having been looked at. So the keys are
           captured VERBATIM from the version-1 `--output` artifact before Claude starts, and
           carried forward.
         * LOSING them when the tick is killed. A watchdog kill takes the acknowledgement down with
           the tick, and the preflight already promised not to write. So the keys go to a journal
           on disk first; a LATER tick finds a journal whose owning process is gone and replays it
           as a FAILED run, which is what a crashed tick was. A journal whose owner is still alive
           is somebody's live tick and is left strictly alone.

       And an acknowledgement that could not be delivered must PRESERVE the journal and must not
       report success. The next tick is the retry; a deleted journal is a muted item nobody saw.

    WHY THE LOGIC IS IN A LIBRARY. The same reason `fixall-sweep.ps1` is: the loop itself cannot be
    run inside the gate — it takes a machine-wide lock, starts a Claude session and writes real
    board state. So the decisions live in `scripts/lib/fixall-wake.ps1` behind injected closures
    and are exercised here against scratch files, while the WIRING in the loop is asserted over the
    parsed AST, as `fixall-loop-guards.Tests.ps1` does and for the reason its header gives.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/fixall-wake.Tests.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibPath = Join-Path $RepoRoot 'scripts\lib\fixall-wake.ps1'
$LoopPath = Join-Path $RepoRoot 'scripts\fixall-loop.ps1'
$InstallPath = Join-Path $RepoRoot 'scripts\install-fixall-task.ps1'

foreach ($required in @($LibPath, $LoopPath, $InstallPath)) {
    if (-not (Test-Path $required)) {
        Write-Host "FAIL: missing $required" -ForegroundColor Red
        exit 1
    }
}

. $LibPath

$script:Failures = 0
$script:Ran = 0
# Assertions MADE, not passed: a case that returns early makes none, and a vacuous OK in the gate
# that guards the scheduled loop is the exact shape of bug this file is about.
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

# A scratch directory per case: these functions write journals and key files, and a case that read
# a previous case's leftovers would pass for the wrong reason.
function New-Scratch {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("astrid-wake-test-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

# A fake preflight: writes the artifact the real `--output` would write, then reports an exit code.
function New-FakePreflight([int]$ExitCode, $Artifact, [string[]]$Output = @()) {
    return {
        param($ArtifactPath)
        if ($null -ne $Artifact) {
            ($Artifact | ConvertTo-Json -Depth 6) | Set-Content -Path $ArtifactPath -Encoding utf8
        }
        return [pscustomobject]@{ ExitCode = $ExitCode; Output = $Output }
    }.GetNewClosure()
}

Write-Host ''
Write-Host 'the Windows loop wakes for replies and acknowledges durably (task 18ba8c2e)' -ForegroundColor Cyan

# ── The verdict ───────────────────────────────────────────────────────────────────────────────

Invoke-Case 'exit 0 is work, and the keys come from the artifact VERBATIM' {
    $scratch = New-Scratch
    try {
        $artifact = Join-Path $scratch 'preflight.json'
        $verdict = Get-WakeVerdict -ArtifactPath $artifact -Preflight (New-FakePreflight 0 @{
                version = 1; listId = 'abb4f961'; work = $true; reason = '1 ready, 2 unanswered'
                wakeKeys = @('comment:task-1:c9', 'chat:msg-7')
            })
        Assert-True 'there is work' $verdict.Work
        Assert-True 'and the verdict is known' $verdict.Known
        Assert-Equal 'both keys were captured' 2 $verdict.WakeKeys.Count
        # Verbatim, and in the artifact's own order: the ack has to hand back the SAME set, since a
        # set recomputed after the run differs by exactly the items the run answered.
        Assert-Equal 'the first key is the artifact s own' 'comment:task-1:c9' $verdict.WakeKeys[0]
        Assert-Equal 'and the second' 'chat:msg-7' $verdict.WakeKeys[1]
        Assert-Equal 'the board id is carried forward from the artifact' 'abb4f961' $verdict.ListId
        Assert-Match 'and the reason is reported' 'unanswered' $verdict.Reason
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'exit 3 is an idle tick, with nothing to acknowledge' {
    $scratch = New-Scratch
    try {
        $artifact = Join-Path $scratch 'preflight.json'
        $verdict = Get-WakeVerdict -ArtifactPath $artifact -Preflight (New-FakePreflight 3 @{
                version = 1; listId = 'abb4f961'; work = $false; reason = 'nothing to do'; wakeKeys = @()
            })
        Assert-False 'no work' $verdict.Work
        Assert-True 'and that is a KNOWN answer, so the tick may skip' $verdict.Known
        Assert-Equal 'no keys woke it' 0 @($verdict.WakeKeys).Count
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'an error exit is UNKNOWN, and unknown runs the tick' {
    $scratch = New-Scratch
    try {
        # No artifact at all: this is what a network or auth failure leaves behind.
        $artifact = Join-Path $scratch 'preflight.json'
        $verdict = Get-WakeVerdict -ArtifactPath $artifact `
            -Preflight (New-FakePreflight 1 $null @('QUEUE: unknown - OAuth token request failed with HTTP 503'))
        Assert-False 'the answer is not known' $verdict.Known
        # THE bug this guards. A queue nobody can see must not read as a quiet board: the tick runs
        # and the agent reports properly.
        Assert-True 'so the tick runs anyway' $verdict.Work
        Assert-Match 'and says it could not tell' 'unknown' $verdict.Reason
        Assert-Equal 'with no keys to mute' 0 @($verdict.WakeKeys).Count
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'an artifact of another version is refused rather than guessed at' {
    $scratch = New-Scratch
    try {
        $artifact = Join-Path $scratch 'preflight.json'
        $verdict = Get-WakeVerdict -ArtifactPath $artifact -Preflight (New-FakePreflight 0 @{
                version = 2; listId = 'abb4f961'; work = $true; wakeKeys = @('comment:task-1:c9')
            })
        Assert-True 'the exit code still decides whether to run' $verdict.Work
        # Muting a key read out of a document whose shape we do not know is how an unanswered
        # question disappears. Better to wake again next tick.
        Assert-Equal 'but no keys are taken from it' 0 @($verdict.WakeKeys).Count
        Assert-Match 'and the mismatch is named' 'version' $verdict.Reason
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

# ── The journal, and acknowledgement ─────────────────────────────────────────────────────────

Invoke-Case 'the journal is written before the run and names its owner' {
    $scratch = New-Scratch
    try {
        $journal = Join-Path $scratch 'wake-4242.json'
        Assert-True 'it saves' (Save-WakeJournal -JournalPath $journal -OwnerPid 4242 `
                -ListId 'abb4f961' -WakeKeys @('comment:task-1:c9'))
        Assert-True 'the file exists' (Test-Path $journal)
        $read = Get-Content $journal -Raw | ConvertFrom-Json
        Assert-Equal 'the owning pid is recorded' 4242 $read.pid
        Assert-Equal 'the board is recorded' 'abb4f961' $read.listId
        Assert-Equal 'and the keys are the ones handed in' 'comment:task-1:c9' $read.wakeKeys[0]
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a finished run marks exactly the journal s keys, and clears the journal' {
    $scratch = New-Scratch
    try {
        $journal = Join-Path $scratch 'wake-4242.json'
        Save-WakeJournal -JournalPath $journal -OwnerPid 4242 -ListId 'abb4f961' `
            -WakeKeys @('comment:task-1:c9', 'chat:msg-7') | Out-Null
        $seen = @()
        $ack = {
            param($KeysFile, $Failed, $ListId)
            # The real call is `--mark-seen --seen-keys-file <path>`: the file form exists because
            # PowerShell 5.1 loses quotes on native JSON arguments.
            $script:ackListId = $ListId
            $script:ackFailed = $Failed
            # ASSIGN, THEN WRAP. PowerShell 5.1's ConvertFrom-Json writes an array as ONE object,
            # so `@(… | ConvertFrom-Json)` wraps it instead of unrolling it and a two-key file
            # reads back as a single key whose value is both of them joined by a space. The same
            # 5.1 trap in reverse - ConvertTo-Json unwrapping a one-element array to a bare scalar
            # - is why scripts/lib/fixall-wake.ps1 builds these files by hand.
            $parsed = Get-Content $KeysFile -Raw | ConvertFrom-Json
            $seen = @($parsed)
            $script:ackKeys = $seen
            return $true
        }
        $result = Invoke-WakeAck -JournalPath $journal -Ack $ack
        Assert-True 'it was attempted' $result.Attempted
        Assert-True 'and acknowledged' $result.Acknowledged
        Assert-Equal 'exactly the journal s two keys' 2 @($script:ackKeys).Count
        Assert-Equal 'in the journal s order' 'comment:task-1:c9' $script:ackKeys[0]
        Assert-False 'a finished run is not a failed one' $script:ackFailed
        Assert-Equal 'and the board came from the journal' 'abb4f961' $script:ackListId
        Assert-False 'the journal is cleared once it is delivered' (Test-Path $journal)
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a failed run takes a strike instead of being muted' {
    $scratch = New-Scratch
    try {
        $journal = Join-Path $scratch 'wake-4242.json'
        Save-WakeJournal -JournalPath $journal -OwnerPid 4242 -ListId 'abb4f961' `
            -WakeKeys @('comment:task-1:c9') | Out-Null
        $script:ackFailed = $null
        $result = Invoke-WakeAck -JournalPath $journal -Failed $true -Ack {
            param($KeysFile, $Failed, $ListId)
            $script:ackFailed = $Failed
            return $true
        }
        Assert-True 'it was acknowledged' $result.Acknowledged
        # --failed gives each key a strike and mutes it only at MAX_FAILED_ATTEMPTS, so a run that
        # keeps dying on one item neither wakes forever nor loses it on the first death.
        Assert-True 'as a FAILED run' $script:ackFailed
        Assert-False 'and the journal is cleared' (Test-Path $journal)
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'an acknowledgement that fails PRESERVES the journal and cannot report success' {
    $scratch = New-Scratch
    try {
        $journal = Join-Path $scratch 'wake-4242.json'
        Save-WakeJournal -JournalPath $journal -OwnerPid 4242 -ListId 'abb4f961' `
            -WakeKeys @('comment:task-1:c9') | Out-Null
        $result = Invoke-WakeAck -JournalPath $journal -Ack { param($KeysFile, $Failed, $ListId) return $false }
        Assert-True 'it was attempted' $result.Attempted
        Assert-False 'and did not succeed' $result.Acknowledged
        # The journal is the only record of what woke this tick. Deleting it on a failed delivery
        # mutes an item nobody ever saw, which is the failure mode the two-phase write exists for.
        Assert-True 'the journal survives for the next tick to retry' (Test-Path $journal)
        Assert-Match 'and the summary says so' 'not acknowledged|could not' $result.Summary
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'nothing to acknowledge is not a failure, and writes no journal' {
    $scratch = New-Scratch
    try {
        $journal = Join-Path $scratch 'wake-4242.json'
        # An idle tick, and a tick whose artifact could not be read: no keys, so no journal.
        Assert-False 'no journal is written for an empty key set' (
            Save-WakeJournal -JournalPath $journal -OwnerPid 4242 -ListId 'abb4f961' -WakeKeys @())
        Assert-False 'and none exists' (Test-Path $journal)
        $called = $false
        $result = Invoke-WakeAck -JournalPath $journal -Ack {
            param($KeysFile, $Failed, $ListId)
            $script:ackCalled = $true
            return $true
        }
        Assert-False 'the acknowledgement is not attempted' $result.Attempted
        Assert-False 'the tool is never called' $script:ackCalled
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

# ── A tick that was killed: the next one replays its journal ─────────────────────────────────

Invoke-Case 'an abandoned journal whose owner is DEAD is replayed as a failed run' {
    $scratch = New-Scratch
    try {
        Save-WakeJournal -JournalPath (Join-Path $scratch 'wake-911.json') -OwnerPid 911 `
            -ListId 'abb4f961' -WakeKeys @('comment:task-1:c9') | Out-Null
        $script:replayFailed = $null
        $result = Invoke-AbandonedWakeAck -JournalDirectory $scratch -SelfPid 4242 `
            -IsAlive { param($OwnerPid) return $false } `
            -Ack {
            param($KeysFile, $Failed, $ListId)
            $script:replayFailed = $Failed
            return $true
        }
        Assert-Equal 'one journal was replayed' 1 $result.Replayed
        # A tick that was watchdog-killed IS a failed run: the preflight promised not to write, so
        # without this the keys that woke it are lost and the item wakes a session every tick.
        Assert-True 'as a failed run, not a finished one' $script:replayFailed
        Assert-False 'and the journal is cleared' (Test-Path (Join-Path $scratch 'wake-911.json'))
        Assert-Match 'the summary names what it recovered' 'wake' $result.Summary
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a journal whose owner is still ALIVE is left strictly alone' {
    $scratch = New-Scratch
    try {
        $other = Join-Path $scratch 'wake-911.json'
        Save-WakeJournal -JournalPath $other -OwnerPid 911 -ListId 'abb4f961' `
            -WakeKeys @('comment:task-1:c9') | Out-Null
        $script:ackCalled = $false
        $result = Invoke-AbandonedWakeAck -JournalDirectory $scratch -SelfPid 4242 `
            -IsAlive { param($OwnerPid) return $true } `
            -Ack { param($KeysFile, $Failed, $ListId) $script:ackCalled = $true; return $true }
        Assert-Equal 'nothing was replayed' 0 $result.Replayed
        Assert-False 'the tool was not called' $script:ackCalled
        # Muting the keys of a LIVE tick would take the inbox out from under a session that is
        # about to answer it.
        Assert-True 'and the live tick s journal is untouched' (Test-Path $other)
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'this tick s own journal is never replayed as abandoned' {
    $scratch = New-Scratch
    try {
        $mine = Join-Path $scratch "wake-$PID.json"
        Save-WakeJournal -JournalPath $mine -OwnerPid $PID -ListId 'abb4f961' `
            -WakeKeys @('comment:task-1:c9') | Out-Null
        # $PID is alive by definition, so the liveness probe would also catch this - the pid filter
        # is belt and braces, and asserted so a future probe that guesses cannot eat our own keys
        # before the run has had its chance.
        $result = Invoke-AbandonedWakeAck -JournalDirectory $scratch -SelfPid $PID `
            -IsAlive { param($OwnerPid) return $false } `
            -Ack { param($KeysFile, $Failed, $ListId) return $true }
        Assert-Equal 'nothing was replayed' 0 $result.Replayed
        Assert-True 'our journal is still there for our own acknowledgement' (Test-Path $mine)
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Invoke-Case 'a replay that cannot be delivered keeps the journal' {
    $scratch = New-Scratch
    try {
        $abandoned = Join-Path $scratch 'wake-911.json'
        Save-WakeJournal -JournalPath $abandoned -OwnerPid 911 -ListId 'abb4f961' `
            -WakeKeys @('comment:task-1:c9') | Out-Null
        $result = Invoke-AbandonedWakeAck -JournalDirectory $scratch -SelfPid 4242 `
            -IsAlive { param($OwnerPid) return $false } `
            -Ack { param($KeysFile, $Failed, $ListId) return $false }
        Assert-Equal 'it counts nothing as recovered' 0 $result.Replayed
        Assert-True 'and leaves the journal for the tick after this one' (Test-Path $abandoned)
    }
    finally { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

# ── The wiring in the loop ───────────────────────────────────────────────────────────────────
# Asserted over the parsed AST for the reason fixall-loop-guards.Tests.ps1 gives at length: both
# halves are a shell-out to astrid-web's tsx and a branch on an exit code, and running them inside
# the gate would need a sibling checkout, a node process and real board state. What regresses here
# is WIRING - a missing flag, a dropped branch - so that is what this checks.

$tokens = $null
$errors = $null
$Ast = [System.Management.Automation.Language.Parser]::ParseFile($LoopPath, [ref]$tokens, [ref]$errors)
if ($errors -and $errors.Count -gt 0) {
    Write-Host "FAIL: scripts/fixall-loop.ps1 does not parse ($($errors.Count) error(s)):" -ForegroundColor Red
    $errors | Select-Object -First 5 | ForEach-Object { Write-Host "    $($_.Message)" -ForegroundColor Red }
    exit 1
}
$Commands = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true))

function Find-LoopCommand([string[]]$AllOf) {
    foreach ($cmd in $Commands) {
        $text = $cmd.Extent.Text
        $ok = $true
        foreach ($needle in $AllOf) { if ($text -notmatch $needle) { $ok = $false; break } }
        if ($ok) { return $cmd }
    }
    return $null
}

$Preflight = Find-LoopCommand @('agent-queue-status\.ts')
$VerdictCall = Find-LoopCommand @('Get-WakeVerdict')
$AckCall = Find-LoopCommand @('Invoke-WakeAck')
$ReplayCall = Find-LoopCommand @('Invoke-AbandonedWakeAck')

Invoke-Case 'guard 3 asks the shared preflight, scoped to this board and this mailbox' {
    Assert-True 'the loop calls agent-queue-status.ts' ($null -ne $Preflight)
    if (-not $Preflight) { return }
    $text = $Preflight.Extent.Text
    # By NAME. A hardcoded id is account data and a stale one answers "nothing to do", which reads
    # exactly like a quiet day (docs/AUTOMATION.md).
    Assert-Match 'for the windows board, resolved by name' "--board'?,?\s*'?windows" $text
    Assert-Match 'as the claude mailbox' "--agent'?,?\s*'?claude" $text
    # Ready is the hand-off on this board, so an unassigned Ready task is work for whoever claims it.
    Assert-Match 'including unassigned Ready work' '--include-unassigned' $text
    # Polling mode is what makes this loop the dispatcher. An API/webhook/off mode is an ERROR, not
    # an idle queue, or the loop would race the hosted dispatcher.
    Assert-Match 'refusing to run beside a hosted dispatcher' '--require-polling' $text
    # Phase one of waking writes NOTHING: a tick that is killed must not have muted what woke it.
    Assert-Match 'and writing no seen state at preflight' '--no-write-seen' $text
    Assert-Match 'the keys arrive in the version-1 artifact' '--output' $text
}

Invoke-Case 'the loop no longer uses the Ready-only queue as its preflight' {
    # ready-tasks.ts answers Ready and lane work only. It is still the sweep that runs INSIDE
    # agent-queue-status.ts (--board), but as guard 3 it made the inbox invisible.
    $readyAsGuard = @($Commands | Where-Object {
            $_.Extent.Text -match 'ready-tasks\.ts' -and $_.Extent.Text -match '--json'
        })
    Assert-Equal 'no ready-tasks.ts --json preflight remains' 0 $readyAsGuard.Count
}

Invoke-Case 'the verdict is taken from the library, and an unknown one runs the tick' {
    Assert-True 'the loop calls Get-WakeVerdict' ($null -ne $VerdictCall)
    if (-not $VerdictCall) { return }
    # The branch that decides a skip, found as AST nodes rather than by slicing the file: extent
    # offsets count the UTF-8 BOM and Get-Content -Raw strips it, so a substring starts three
    # characters late.
    $region = ((@($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] }, $true)) |
                Where-Object { $_.Extent.Text -match '\$wake\.Work' -or $_.Extent.Text -match '\$wake\.Known' } |
                ForEach-Object { $_.Extent.Text }) -join "`n")
    Assert-True 'the verdict is branched on' ($region.Length -gt 0)
    Assert-Match 'no work ends the tick as a SKIP' 'RESULT: SKIPPED' $region
    Assert-NotMatch 'and never as a failure - a quiet board is healthy' 'RESULT: FAILED' $region
}

Invoke-Case 'the run s outcome acknowledges what woke it, and a dead tick s journal is replayed' {
    Assert-True 'the loop acknowledges after the run' ($null -ne $AckCall)
    Assert-True 'and replays an abandoned journal' ($null -ne $ReplayCall)
    if (-not $AckCall -or -not $ReplayCall -or -not $Preflight) { return }
    # The replay is a START-OF-TICK pass: it covers the one case the after-run pass structurally
    # cannot, a tick hard-killed before it could acknowledge. So it has to run before the
    # preflight writes this tick's own journal.
    Assert-True 'the replay comes before this tick s own preflight' (
        $ReplayCall.Extent.StartOffset -lt $Preflight.Extent.StartOffset)
    Assert-True 'and the acknowledgement comes after it' (
        $AckCall.Extent.StartOffset -gt $Preflight.Extent.StartOffset)
}

Invoke-Case 'a dry run acknowledges nothing' {
    # `--dry-run cannot mark seen keys` is the shared script's own rule, and a -DryRun tick has not
    # run anything to acknowledge the outcome of.
    $dryRunGuards = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] }, $true)) |
        Where-Object { $_.Extent.Text -match '\$DryRun' }
    Assert-True 'the loop still has a dry-run exit' (@($dryRunGuards).Count -gt 0)
    if (-not $AckCall) { Assert-True 'and an acknowledgement to skip' $false; return }
    $dryRunExit = @($dryRunGuards | Where-Object { $_.Extent.Text -match 'RESULT: SKIPPED' } | Select-Object -First 1)
    Assert-True 'which ends the tick' (@($dryRunExit).Count -eq 1)
    if (@($dryRunExit).Count -ne 1) { return }
    # Structural: the dry-run Finish is BEFORE the acknowledgement, so no path reaches --mark-seen.
    Assert-True 'before any acknowledgement could be reached' (
        $dryRunExit[0].Extent.StartOffset -lt $AckCall.Extent.StartOffset)
}

# ── The scheduler must not kill a run before its own watchdog does ───────────────────────────

$installTokens = $null
$installErrors = $null
$InstallAst = [System.Management.Automation.Language.Parser]::ParseFile($InstallPath, [ref]$installTokens, [ref]$installErrors)
if ($installErrors -and $installErrors.Count -gt 0) {
    Write-Host "FAIL: scripts/install-fixall-task.ps1 does not parse" -ForegroundColor Red
    exit 1
}

Invoke-Case 'the scheduler deadline is derived from the watchdog, not hardcoded under it' {
    $text = (Get-Content $InstallPath -Raw)
    # The bug: ExecutionTimeLimit was a literal 55 minutes against a 75-minute launcher watchdog, so
    # Task Scheduler killed every long tick twenty minutes BEFORE the watchdog that is supposed to
    # catch a wedged run - and a scheduler kill runs none of the loop's recovery: no WIP commit, no
    # Doing release, no acknowledgement.
    Assert-NotMatch 'no literal 55-minute deadline' 'Minutes 55' $text
    $limit = @($InstallAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)) |
        Where-Object { $_.Extent.Text -match 'New-ScheduledTaskSettingsSet' } | Select-Object -First 1
    Assert-True 'the settings set is still built' ($null -ne $limit)
    if (-not $limit) { return }
    Assert-Match 'and its deadline is computed from the watchdog' 'ExecutionTimeLimit[\s\S]{0,80}Watchdog' $limit.Extent.Text

    # ONE validated watchdog, FORWARDED. Two independently-defaulted numbers is how these drifted
    # apart in the first place.
    $action = @($InstallAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)) |
        Where-Object { $_.Extent.Text -match 'New-ScheduledTaskAction' } | Select-Object -First 1
    Assert-True 'the action is still built' ($null -ne $action)
    if ($action) { Assert-Match 'and passes the watchdog to the loop explicitly' '-MaxMinutes' $action.Extent.Text }

    $param = @($InstallAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.ParameterAst] }, $true)) |
        Where-Object { $_.Name.VariablePath.UserPath -eq 'WatchdogMinutes' } | Select-Object -First 1
    Assert-True 'the watchdog is a validated parameter' ($null -ne $param)
    if ($param) { Assert-Match 'with a range' 'ValidateRange' $param.Extent.Text }
}

Invoke-Case 'the deadline leaves the loop room to recover after its own watchdog fires' {
    # The watchdog kills CLAUDE and then the loop saves unfinished work, releases its Doing claims,
    # acknowledges the wake keys and comments on the task. A deadline equal to the watchdog kills
    # the loop in the middle of exactly that.
    $text = (Get-Content $InstallPath -Raw)
    Assert-Match 'the headroom is named and additive' 'RecoveryMinutes' $text
    $recovery = @($InstallAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.ParameterAst] }, $true)) |
        Where-Object { $_.Name.VariablePath.UserPath -eq 'RecoveryMinutes' } | Select-Object -First 1
    Assert-True 'as a validated parameter of its own' ($null -ne $recovery)
    if ($recovery) { Assert-Match 'with a range' 'ValidateRange' $recovery.Extent.Text }
}

Write-Host ''
if ($script:Checks -lt 55) {
    Write-Host "FAIL: only $($script:Checks) assertion(s) ran - this file is meant to make more" -ForegroundColor Red
    $script:Failures++
}
if ($script:Failures -gt 0) {
    Write-Host "RESULT: FAILED - $($script:Failures) failure(s) in $($script:Ran) case(s)" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: OK - $($script:Ran) case(s), $($script:Checks) assertion(s)" -ForegroundColor Green
exit 0
