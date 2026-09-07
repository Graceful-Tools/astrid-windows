# The standard gate. Run it before pushing; CI runs the same steps.
#
#   npm run predeploy         format, lint, test, cross-build, contracts
#   npm run predeploy:quick   skip the ARM64 cross-build (fast inner loop)
#   npm run predeploy:full    adds the packaged-app build and UI smoke tests
#
# Every step prints its own heading and the script stops at the first failure, so the last heading
# on screen names what broke.

[CmdletBinding()]
param(
    [switch]$Quick,
    [switch]$Full
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$script:StepNumber = 0

function Invoke-Step {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )
    $script:StepNumber++
    Write-Host ""
    Write-Host "[$script:StepNumber] $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "RESULT: FAILED - $Name" -ForegroundColor Red
        exit 1
    }
}

Invoke-Step 'rustfmt' { cargo fmt --all -- --check }
Invoke-Step 'clippy' { cargo clippy --all-targets --all-features -- -D warnings }
Invoke-Step 'core tests' { cargo test --workspace }

# The contract fixtures are generated from astrid-web. This fails when web has moved and the
# fixtures here have not, which is the moment to make it a cross-repo change rather than a
# surprise in production.
Invoke-Step 'cross-platform contracts' { cargo xtask check-contracts }

if (-not $Quick) {
    # BOTH shipping architectures are built by name on every run, rather than trusting that the
    # host covers one of them. The machine this was written on is ARM64, where a bare
    # `cargo build` and an "ARM64 cross-build" are the same command twice and x64 — the
    # architecture most users are on — is never compiled at all. A dependency that cannot target
    # one of them is caught the day it lands, whichever machine lands it.
    Invoke-Step 'x64 build' { cargo build --workspace --target x86_64-pc-windows-msvc }
    Invoke-Step 'ARM64 build' { cargo build --workspace --target aarch64-pc-windows-msvc }
}

# The shell arrives in M2. Until then there is nothing to build, and claiming otherwise would make
# this gate read as greener than it is.
$appSolution = Join-Path $repoRoot 'app/Astrid.sln'
if (Test-Path $appSolution) {
    Invoke-Step 'shell build (x64)' { dotnet build $appSolution -c Release -p:Platform=x64 }
    Invoke-Step 'shell build (ARM64)' { dotnet build $appSolution -c Release -p:Platform=ARM64 }
    Invoke-Step 'shell tests' { dotnet test $appSolution -c Release --no-build }
} else {
    Write-Host ""
    Write-Host "[-] shell build skipped - app/Astrid.sln does not exist yet (M2)" -ForegroundColor DarkGray
}

if ($Full) {
    Write-Host ""
    Write-Host "[-] UI smoke tests skipped - WinAppDriver suite arrives with the shell (M2)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "RESULT: OK" -ForegroundColor Green
