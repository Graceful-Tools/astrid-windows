#Requires -Version 5.1
<#
.SYNOPSIS
    Is there room to build, and what to say when there is not. Task 4d9d42e6.

.DESCRIPTION
    THE FAILURE THIS EXISTS FOR. The scheduled tick of 2026-11-13 05:10 spent itself getting to

        error: failed to build archive at `...\target\debug\deps\libastrid_core-....rlib`:
        There is not enough space on the disk. (os error 112)

    with 31 MB free of 509 GB used. `astrid-windows/target` was 103 GB and `astrid-core/target`
    51 GB. An `os error 112` three quarters of the way through a build reads exactly like a broken
    change; it is not, and finding that out costs a whole tick. A reading taken before anything is
    compiled costs milliseconds.

    WHY THE CACHES GET THIS BIG. `npm run predeploy` builds, in one run: host debug, host release,
    the ARM64 cross-build, the x64 cross-build and the .NET shell in Release - and `deny.toml`'s
    all-features graph compiles the dependency tree more than one way. Every pin bump adds a fresh
    `astrid-core-<hash>` tree beside the ones before it, and until this task nothing ever collected
    them: the artefacts of every revision this app has ever pinned were still on disk.

    THE TWO MARKS, and why they are different numbers.

      * THE FLOOR is the point below which starting is pointless - the tick would die mid-build and
        report something that looks like a code failure. Under it the tick stops as FAILED.
      * THE WARN MARK is the point at which the next few ticks are in danger. Above the floor the
        tick RUNS; the warning exists so the log says "this is coming" while there is still time,
        rather than only once it has already happened.

    FAILED AND NOT SKIPPED. A SKIPPED tick is indistinguishable from an empty board, and this
    machine would go quiet for a week with nobody knowing why. A full disk is something to fix.

    AND A GUARD WITH NO WAY TO CLEAR IT IS A LOOP THAT STOPS FOR EVER, so both messages name
    scripts/prune-build-caches.ps1 - which is also why that script is age-based rather than a
    `cargo clean`: a full clean costs the next gate a cold build of the whole graph, so the obvious
    remedy would make the loop's next tick worse than the one it just saved.

    AN UNREADABLE READING RUNS THE TICK. Same call guard 1b makes about an unreadable pause state
    and guard 2b about an unreadable load average (fixall-loop.ps1): a loop that stalls on a reading
    it could not take is a worse outage than one build too many.

    WHY IT IS A LIBRARY. Same reason as scripts/lib/fixall-wake.ps1 - the loop itself cannot run
    inside the gate, so the decision lives here and is tested by value, while the WIRING in the loop
    is asserted over the parsed AST. See scripts/tests/fixall-disk.Tests.ps1.
#>

# Defaults, in bytes. Exposed as script-scope so the loop can name them in one place and a future
# machine with a different disk can override them without editing the decision.
#
# THE FLOOR IS 15 GB. A cold `npm run predeploy` compiles the whole graph five ways; the measured
# trees say that is tens of gigabytes, and the floor only has to cover the single build in front of
# it plus the shell's bin/obj. 15 GB is comfortably above a warm gate's incremental write and far
# enough from zero that the tick stops while the message is still the true one.
#
# THE WARN MARK IS 40 GB, which on 2026-11-12 was just under what the machine actually had (45 GB)
# - deliberately, because with 103 GB of unpruned cache still in place "you are close" is the true
# statement about this machine, and a warning that only fires after the floor has been crossed is a
# warning nobody can act on.
$script:DiskFloorBytes = 15GB
$script:DiskWarnBytes = 40GB

<#
.SYNOPSIS
    Free bytes on the volume holding a path, or -1 if the reading could not be taken.

.DESCRIPTION
    THE VOLUME THE PATH IS ON, not `C:`. This repo and astrid-core are both under the user profile
    today, but a checkout moved to a second disk would otherwise be guarded against the free space
    of a drive it never writes to - a guard that is wrong in both directions at once.

    -1 RATHER THAN A THROW. Every caller's answer to "the reading failed" is the same: run the tick
    anyway. Returning a sentinel keeps that decision in Get-DiskSpaceVerdict with the other three
    instead of putting half of it in a try/catch at each call site.
#>
function Get-FreeSpaceBytes {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        $full = [System.IO.Path]::GetFullPath($Path)
        $root = [System.IO.Path]::GetPathRoot($full)
        if (-not $root) { return -1L }
        # DriveInfo and not Get-PSDrive: Get-PSDrive reports the PROVIDER drive, whose Free is null
        # for anything that is not a filesystem drive, and null compares as 0 - which would read as
        # a disk with no space at all and stop every tick.
        $drive = New-Object System.IO.DriveInfo($root)
        if (-not $drive.IsReady) { return -1L }
        return [long]$drive.AvailableFreeSpace
    }
    catch { return -1L }
}

<#
.SYNOPSIS
    Decide whether a tick may start, given the free space it measured.

.OUTPUTS
    @{ Ok = [bool]; Level = 'ok'|'low'|'critical'|'unknown'; Reason = [string] }

    Ok is whether the tick runs. Level is for the log and the tests. Reason is pasted verbatim into
    a RESULT: line, so it is one line, and it always names the way out.
#>
function Get-DiskSpaceVerdict {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][long]$FreeBytes,
        [long]$FloorBytes = $script:DiskFloorBytes,
        [long]$WarnBytes = $script:DiskWarnBytes
    )

    $prune = 'powershell -File scripts/prune-build-caches.ps1 -Apply'

    if ($FreeBytes -lt 0) {
        return [pscustomobject]@{
            Ok     = $true
            Level  = 'unknown'
            Reason = 'could not read the free space on this volume; running the tick anyway'
        }
    }

    $freeGb = '{0:N2}' -f ($FreeBytes / 1GB)
    $floorGb = '{0:N0}' -f ($FloorBytes / 1GB)
    $warnGb = '{0:N0}' -f ($WarnBytes / 1GB)

    # STRICTLY BELOW. At exactly the floor the tick still starts: the floor is the lowest reading a
    # build is allowed to begin on, not the first one it is refused at. Written down because the
    # boundary is the half of a threshold that silently flips in a refactor.
    if ($FreeBytes -lt $FloorBytes) {
        return [pscustomobject]@{
            Ok     = $false
            Level  = 'critical'
            Reason = "only $freeGb GB free, under the $floorGb GB floor - the build would die part-way through with an os error 112 that reads like a broken change; prune the build caches with ``$prune``"
        }
    }

    if ($FreeBytes -lt $WarnBytes) {
        return [pscustomobject]@{
            Ok     = $true
            Level  = 'low'
            Reason = "$freeGb GB free, under the $warnGb GB warn mark - running, but prune the build caches soon with ``$prune``"
        }
    }

    return [pscustomobject]@{ Ok = $true; Level = 'ok'; Reason = '' }
}
