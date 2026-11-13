#Requires -Version 5.1
<#
.SYNOPSIS
    Measure the Rust and .NET build caches, and prune the artefacts no current build touches.
    Task 4d9d42e6.

.DESCRIPTION
    WHAT THIS IS FOR. On 2026-11-13 the scheduled tick died mid-build with `os error 112` and
    31 MB free: `astrid-windows/target` was 103 GB and `astrid-core/target` 51 GB, and nothing
    had ever pruned either. `npm run predeploy` builds host debug, host release, both cross
    targets and the .NET shell in Release, and every pin bump adds a fresh `astrid-core-<hash>`
    tree beside the ones before it. The artefacts of every revision this app has ever pinned were
    still on disk.

    WHY AGE AND NOT `cargo clean`. A full clean frees everything and costs the NEXT gate a cold
    build of the whole graph - which, on a loop that runs every half hour, trades one slow tick
    for another. Cargo rebuilds anything it finds missing, so the only unsafe prune is one that
    takes what the CURRENT build is still touching; everything cargo has not written to in
    -Days days belongs to a revision nobody pins any more. This is what `cargo-sweep --time` does,
    hand-rolled here so the gate needs no extra cargo subcommand installed (and see
    docs/context/stack.md: this machine installs its tools deliberately, not on demand).

    REPORT-ONLY UNLESS -Apply. CLAUDE.md requires asking before deleting files, and a pruner whose
    bare run deletes is one a scheduled loop could be pointed at by accident. A bare run measures,
    says what it would remove, and changes nothing.

    OPTION 3 ON THE TASK: `app/*/bin/` and `obj/` hold their own copies per configuration and per
    target RID - five of them after a full gate - so they are measured alongside. They are NOT
    pruned by age: MSBuild's incremental check is timestamp-based against inputs rather than a
    content hash like cargo's fingerprints, and removing a file underneath it produces a build
    that succeeds and an app that does not run, which is a far worse failure than a slow rebuild.
    `dotnet clean` is the honest tool there, and the report says so.

.PARAMETER Root
    The checkout(s) to measure and prune. Defaults to this repo and ../astrid-core, which are the
    two trees a tick builds in.

.PARAMETER Days
    Keep anything cargo has written in this many days. 14 by default: comfortably longer than the
    gap between pin bumps, so the current revision's artefacts are never in range.

.PARAMETER Apply
    Actually delete. Without it nothing is removed.

.EXAMPLE
    powershell -File scripts/prune-build-caches.ps1
    powershell -File scripts/prune-build-caches.ps1 -Apply
#>
[CmdletBinding()]
param(
    [string[]]$Root,
    [int]$Days = 14,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $Root -or $Root.Count -eq 0) {
    $Root = @($repoRoot, (Join-Path (Split-Path -Parent $repoRoot) 'astrid-core'))
}

$cutoff = (Get-Date).AddDays(-$Days)

function Format-Size([double]$Bytes) {
    if ($Bytes -ge 1GB) { return ('{0:N2} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    return ('{0:N0} B' -f $Bytes)
}

# One enumeration, reused for the total and for the prune list. Walking 103 GB twice is minutes of
# metadata reads for a number that was already in hand the first time.
function Get-TreeFiles([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    return @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue)
}

$totalFreed = 0L
$totalSeen = 0L

Write-Host ''
if ($Apply) {
    Write-Host "pruning build artefacts untouched since $($cutoff.ToString('yyyy-MM-dd'))" -ForegroundColor Cyan
}
else {
    Write-Host "report only - nothing will be deleted. Re-run with -Apply to prune." -ForegroundColor Yellow
    Write-Host "would prune build artefacts untouched since $($cutoff.ToString('yyyy-MM-dd'))" -ForegroundColor Cyan
}

# $checkout, NOT $root: PowerShell variable names are case-insensitive, so `foreach ($root in $Root)`
# iterates the parameter while overwriting it with each element. The loop itself survives - the
# collection is evaluated once, up front - but $Root is a bare string by the second pass, and the
# next person to read it after the loop gets the last checkout rather than the list. The same
# silent coercion has already cost this repo once, in package.ps1.
foreach ($checkout in $Root) {
    if (-not (Test-Path -LiteralPath $checkout)) {
        Write-Host ""
        Write-Host "  $checkout - not here, skipped" -ForegroundColor DarkGray
        continue
    }

    $target = Join-Path $checkout 'target'
    $files = Get-TreeFiles $target
    $size = ($files | Measure-Object -Property Length -Sum).Sum
    if (-not $size) { $size = 0L }
    $totalSeen += $size

    Write-Host ''
    Write-Host "  $target" -ForegroundColor White
    Write-Host "    $(Format-Size $size) in $($files.Count) file(s)"

    # LastWriteTime, not LastAccessTime: NTFS last-access updates are off by default on Windows, so
    # every file in the tree would look untouched since it was written and the "keep what the
    # current build needs" half of this would silently do nothing.
    $stale = @($files | Where-Object { $_.LastWriteTime -lt $cutoff })
    $staleSize = ($stale | Measure-Object -Property Length -Sum).Sum
    if (-not $staleSize) { $staleSize = 0L }

    if ($stale.Count -eq 0) {
        Write-Host "    nothing older than $Days days" -ForegroundColor DarkGray
    }
    elseif ($Apply) {
        $removed = 0
        foreach ($file in $stale) {
            try {
                Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
                $removed++
            }
            catch {
                # A locked file is a build running right now, or an antivirus holding it open.
                # Neither is a reason to stop: the rest of the tree is still worth freeing, and the
                # next run picks this one up.
                Write-Host "    could not remove $($file.FullName): $($_.Exception.Message)" -ForegroundColor DarkYellow
            }
        }
        $totalFreed += $staleSize
        Write-Host "    freed $(Format-Size $staleSize) from $removed file(s)" -ForegroundColor Green
    }
    else {
        $totalFreed += $staleSize
        Write-Host "    would free $(Format-Size $staleSize) from $($stale.Count) file(s)" -ForegroundColor Yellow
    }

    # Measured, never pruned - see the header. MSBuild's incremental check is timestamp-based, so
    # taking a file from under it yields a build that passes and an app that does not run.
    $appDirs = @()
    $appRoot = Join-Path $checkout 'app'
    if (Test-Path -LiteralPath $appRoot) {
        $appDirs = @(Get-ChildItem -LiteralPath $appRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' })
    }
    if ($appDirs.Count -gt 0) {
        $appSize = 0L
        foreach ($dir in $appDirs) {
            $s = ((Get-TreeFiles $dir.FullName) | Measure-Object -Property Length -Sum).Sum
            if ($s) { $appSize += $s }
        }
        $totalSeen += $appSize
        Write-Host "    plus $(Format-Size $appSize) in $($appDirs.Count) app bin/obj director(ies) - clear with ``dotnet clean app/Astrid.sln``" -ForegroundColor DarkGray
    }
}

$free = $null
try {
    $drive = New-Object System.IO.DriveInfo([System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($repoRoot)))
    if ($drive.IsReady) { $free = [long]$drive.AvailableFreeSpace }
}
catch { $free = $null }

Write-Host ''
Write-Host "  measured $(Format-Size $totalSeen) of build cache in total"
if ($Apply) { Write-Host "  freed $(Format-Size $totalFreed)" -ForegroundColor Green }
else { Write-Host "  would free $(Format-Size $totalFreed) - re-run with -Apply" -ForegroundColor Yellow }
if ($null -ne $free) { Write-Host "  $(Format-Size $free) free on the volume now" }

Write-Host ''
Write-Host 'RESULT: OK' -ForegroundColor Green
exit 0
