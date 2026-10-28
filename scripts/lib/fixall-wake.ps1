#Requires -Version 5.1
<#
.SYNOPSIS
    Why a scheduled tick wakes, and how it acknowledges what woke it. Task 18ba8c2e.

.DESCRIPTION
    WAKING IS TWO-PHASE, and both phases are here because getting either half wrong is silent.

    PHASE ONE, before the run: ask the shared preflight (astrid-web
    `scripts/agent-queue-status.ts`) for queue + attention + lanes in one request, with
    `--no-write-seen` so nothing is muted yet, and take the wake keys VERBATIM out of its version-1
    `--output` artifact. Verbatim, because the obvious alternative - recomputing the keys when the
    run ends - returns a set that differs from the one that woke the tick by exactly the items the
    run answered, and the difference is what gets muted without ever having been looked at.

    The exit code is the whole interface: 0 work, 3 nothing to do, anything else UNKNOWN. Unknown
    RUNS the tick. A queue nobody can see is a reason to start a session and let the agent report,
    never a reason to go quiet - that confusion is how a loop spends weeks looking like a quiet
    board.

    PHASE TWO, after it: `--mark-seen` those exact keys, with `--failed` when the run died so each
    key takes a strike rather than being muted outright. Between the two phases is a watchdog, a
    budget cap and a scheduler deadline, any of which can take the tick down - so the keys go to a
    JOURNAL on disk first, under %LOCALAPPDATA% rather than %TEMP%, and a later tick that finds a
    journal whose owning process is gone replays it as the failed run it was. A journal whose owner
    is still alive belongs to somebody's live tick and is left strictly alone.

    AND AN ACKNOWLEDGEMENT THAT COULD NOT BE DELIVERED KEEPS ITS JOURNAL. The next tick is the
    retry; a journal deleted on a failed delivery is an item muted that nobody ever saw.

    WHY IT IS A LIBRARY. Same reason as scripts/lib/fixall-sweep.ps1: the loop itself cannot run
    inside the gate - it takes a machine-wide lock, starts a Claude session and writes real board
    state - so the decisions live here behind injected closures and are tested against scratch
    files, while the WIRING in the loop is asserted over the parsed AST.
    See scripts/tests/fixall-wake.Tests.ps1.
#>

# PowerShell 5.1's ConvertTo-Json unwraps a one-element array to a bare scalar, so a journal with a
# single wake key would read back as a STRING and `wakeKeys[0]` would be its first character. Both
# files this library writes are read back - one by the next tick, one by astrid-web's
# `--seen-keys-file` - so the array shape is built rather than inferred.
function ConvertTo-WakeKeysJson([string[]]$Keys) {
    $items = @(@($Keys) | Where-Object { $_ } | ForEach-Object { ConvertTo-Json ([string]$_) -Compress })
    return '[' + ($items -join ',') + ']'
}

<#
.SYNOPSIS
    Run the preflight and say whether this tick should start a session, and what woke it.

.PARAMETER ArtifactPath
    Where the preflight is told to write its version-1 `--output` document. Deleted first, so a
    previous tick's artifact can never be mistaken for this one's.

.PARAMETER Preflight
    param($ArtifactPath) -> @{ ExitCode; Output }. Injected so the gate can exercise every verdict
    without a network, a token or a board.
#>
function Get-WakeVerdict {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ArtifactPath,
        [Parameter(Mandatory = $true)][scriptblock]$Preflight
    )

    Remove-Item $ArtifactPath -Force -ErrorAction SilentlyContinue

    $raw = & $Preflight $ArtifactPath
    $result = @($raw)[-1]
    $exitCode = if ($null -ne $result) { [int]$result.ExitCode } else { 1 }
    $output = @(if ($null -ne $result) { $result.Output })

    # 0 work, 3 idle, anything else could not tell. THE rule this file exists for: unknown is not
    # idle, so it runs.
    $work = $true
    $known = $true
    if ($exitCode -eq 3) { $work = $false }
    elseif ($exitCode -ne 0) { $known = $false }

    $artifact = $null
    $complaint = ''
    if (Test-Path $ArtifactPath) {
        try { $artifact = (Get-Content -Path $ArtifactPath -Raw -Encoding utf8 | ConvertFrom-Json) }
        catch { $artifact = $null; $complaint = 'the preflight artifact could not be read, so no wake keys were taken from it' }
    }

    $keys = @()
    $listId = ''
    $reason = ''
    if ($artifact) {
        if ([int]$artifact.version -ne 1) {
            # Muting a key read out of a document whose shape we do not know is how an unanswered
            # question disappears. Waking again next tick is the cheaper mistake.
            $complaint = "the preflight artifact is version $($artifact.version), not the version 1 this loop reads, so no wake keys were taken from it"
        }
        else {
            $keys = @(@($artifact.wakeKeys) | Where-Object { $_ })
            $listId = [string]$artifact.listId
            $reason = [string]$artifact.reason
        }
    }

    if (-not $known) {
        # Nothing is muted for a verdict nobody could compute.
        $keys = @()
        $said = @($output | Where-Object { "$_".Trim() } | Select-Object -Last 1)
        $reason = "unknown - the preflight exited $exitCode"
        if ($said.Count -gt 0) { $reason += ": $(("$($said[0])").Trim())" }
    }
    elseif ($complaint) { $reason = $complaint }
    elseif (-not $reason) { $reason = if ($work) { 'there is work' } else { 'nothing to do' } }

    return [pscustomobject]@{
        Work     = $work
        Known    = $known
        WakeKeys = $keys
        ListId   = $listId
        Reason   = $reason
    }
}

<#
.SYNOPSIS
    Write down what woke this tick, before the thing that might kill it.

.DESCRIPTION
    Returns $true when a journal was written. An empty key set writes nothing and is NOT a failure:
    an idle tick and a tick whose artifact could not be read both have nothing to acknowledge.
#>
function Save-WakeJournal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$JournalPath,
        [Parameter(Mandatory = $true)][int]$OwnerPid,
        [string]$ListId = '',
        [string[]]$WakeKeys = @()
    )

    $keys = @(@($WakeKeys) | Where-Object { $_ })
    if ($keys.Count -eq 0) { return $false }

    $directory = Split-Path -Parent $JournalPath
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    # The board id is carried IN the journal so the acknowledgement needs no second board
    # resolution - and so a resolution that fails later cannot strand keys that were already read.
    $json = '{"pid":' + $OwnerPid +
    ',"listId":' + (ConvertTo-Json ([string]$ListId) -Compress) +
    ',"savedAt":' + (ConvertTo-Json ((Get-Date).ToUniversalTime().ToString('o')) -Compress) +
    ',"wakeKeys":' + (ConvertTo-WakeKeysJson $keys) + '}'
    try {
        Set-Content -Path $JournalPath -Value $json -Encoding utf8
        return $true
    }
    catch { return $false }
}

<#
.SYNOPSIS
    Hand a journal's keys to `--mark-seen`, and clear it only if that got through.

.PARAMETER Failed
    The run died - watchdog, budget, a crash. Each key takes a strike instead of being muted, so a
    run that keeps dying on one item neither wakes forever nor loses it on the first death.

.PARAMETER Ack
    param($KeysFile, $Failed, $ListId) -> $true when the keys were recorded. A FILE, not an
    argument: PowerShell 5.1 loses the quotes on a JSON array handed to a native command.
#>
function Invoke-WakeAck {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$JournalPath,
        [bool]$Failed = $false,
        [Parameter(Mandatory = $true)][scriptblock]$Ack
    )

    $nothing = [pscustomobject]@{ Attempted = $false; Acknowledged = $false; Summary = '' }
    if (-not (Test-Path $JournalPath)) { return $nothing }

    $journal = $null
    try { $journal = (Get-Content -Path $JournalPath -Raw -Encoding utf8 | ConvertFrom-Json) } catch { $journal = $null }
    $keys = @()
    if ($journal) { $keys = @(@($journal.wakeKeys) | Where-Object { $_ }) }
    if ($keys.Count -eq 0) {
        # Nothing to record and nothing to retry, so the file is just litter.
        Remove-Item $JournalPath -Force -ErrorAction SilentlyContinue
        return $nothing
    }
    $listId = if ($journal) { [string]$journal.listId } else { '' }

    # Deliberately not `*.json`: the abandoned-journal sweep globs wake-*.json, and a keys file
    # sitting beside its journal would be read as another tick's.
    $keysFile = [System.IO.Path]::ChangeExtension($JournalPath, '.keys')
    $delivered = $false
    try {
        Set-Content -Path $keysFile -Value (ConvertTo-WakeKeysJson $keys) -Encoding utf8
        $answer = @(& $Ack $keysFile $Failed $listId)
        if ($answer.Count -gt 0) { $delivered = [bool]$answer[-1] }
    }
    catch { $delivered = $false }
    finally { Remove-Item $keysFile -Force -ErrorAction SilentlyContinue }

    if ($delivered) {
        Remove-Item $JournalPath -Force -ErrorAction SilentlyContinue
        $what = if ($Failed) { 'a strike each after a failed run' } else { 'seen' }
        return [pscustomobject]@{
            Attempted = $true; Acknowledged = $true
            Summary   = "WAKE: $($keys.Count) key(s) recorded as $what"
        }
    }
    # The journal is the only record of what woke this tick. Deleting it here would mute an item
    # nobody ever saw, which is the failure mode the two-phase write exists for.
    return [pscustomobject]@{
        Attempted = $true; Acknowledged = $false
        Summary   = "WAKE: $($keys.Count) key(s) could not be acknowledged; the journal is kept for the next tick to retry"
    }
}

<#
.SYNOPSIS
    Replay the journals of ticks that were killed before they could acknowledge.

.DESCRIPTION
    The one case the after-run acknowledgement structurally cannot cover: a hard kill is precisely
    what stops it. A tick that was killed IS a failed run, so that is how its keys are recorded.

.PARAMETER IsAlive
    param($OwnerPid) -> $true while that tick is still running. A live tick's journal is left
    alone: muting its keys would take the inbox out from under a session about to answer it.
#>
function Invoke-AbandonedWakeAck {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$JournalDirectory,
        [Parameter(Mandatory = $true)][int]$SelfPid,
        [Parameter(Mandatory = $true)][scriptblock]$IsAlive,
        [Parameter(Mandatory = $true)][scriptblock]$Ack,
        [scriptblock]$Log
    )

    $replayed = 0
    $kept = 0
    if (Test-Path $JournalDirectory) {
        foreach ($file in @(Get-ChildItem -Path $JournalDirectory -Filter 'wake-*.json' -File -ErrorAction SilentlyContinue)) {
            $journal = $null
            try { $journal = (Get-Content -Path $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json) } catch { continue }
            if (-not $journal) { continue }
            $ownerPid = [int]$journal.pid
            # Belt and braces over the liveness probe below, which would also catch this: our own
            # keys must survive until our own acknowledgement has had its chance.
            if ($ownerPid -eq $SelfPid) { continue }

            # An unreadable liveness answer counts as ALIVE. Replaying a journal whose owner is
            # still working is the expensive mistake; waiting one more tick is the cheap one.
            $alive = $true
            try {
                $answer = @(& $IsAlive $ownerPid)
                if ($answer.Count -gt 0) { $alive = [bool]$answer[-1] }
            }
            catch { $alive = $true }
            if ($alive) { continue }

            if ($Log) { & $Log "  WAKE: replaying the journal of a tick that was killed (pid $ownerPid)" }
            $delivery = Invoke-WakeAck -JournalPath $file.FullName -Failed $true -Ack $Ack
            if ($delivery.Acknowledged) { $replayed++ } else { $kept++ }
        }
    }

    $summary = ''
    if ($replayed -gt 0) { $summary = "replayed $replayed abandoned wake journal(s) as a failed run" }
    if ($kept -gt 0) {
        if ($summary) { $summary += '; ' }
        $summary += "$kept abandoned wake journal(s) could not be acknowledged and were kept"
    }
    return [pscustomobject]@{ Replayed = $replayed; Kept = $kept; Summary = $summary }
}
