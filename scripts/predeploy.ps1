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

# The shell. It links against the DLL the Rust steps above produce, which is why they come first:
# a shell built against a stale core is a build that passes and an app that does not run.
#
# Architecture is chosen with a RID rather than a Platform, because that is what an SDK-style
# project with a WinUI target understands; `-p:Platform=x64` is silently ignored here and builds
# the host architecture twice.
$appSolution = Join-Path $repoRoot 'app/Astrid.sln'
if (Test-Path $appSolution) {
    # Per project, not per solution: a solution cannot be built with a RuntimeIdentifier, and
    # asking it to is an error rather than something it quietly ignores.
    $appProject = Join-Path $repoRoot 'app/Astrid.App/Astrid.App.csproj'
    Invoke-Step 'shell build (x64)' {
        dotnet build $appProject -c Release -r win-x64 --self-contained false
    }
    Invoke-Step 'shell build (ARM64)' {
        dotnet build $appProject -c Release -r win-arm64 --self-contained false
    }
    # The tests are plain net9.0 and run on the host, so they are built and run without a RID.
    # They include the C#-to-Rust boundary tests, which need the DLL the Rust steps built.
    Invoke-Step 'shell tests' { dotnet test $appSolution -c Release }
} else {
    Write-Host ""
    Write-Host "[-] shell build skipped - app/Astrid.sln does not exist yet" -ForegroundColor DarkGray
}

if ($Full) {
    # The UI smoke tests launch the built app and drive it through UI Automation. They need a
    # desktop session, they take about a minute, and they run one at a time — which is why they are
    # here rather than in the ordinary gate.
    #
    # The Release build above is for x64 and ARM64; these run the host's, so the app is built once
    # more for the host RID in Debug, which is what the tests look for first.
    $hostRid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
    $uiTests = Join-Path $repoRoot 'app/Astrid.App.UITests/Astrid.App.UITests.csproj'
    Invoke-Step "shell build for the UI tests ($hostRid)" {
        dotnet build (Join-Path $repoRoot 'app/Astrid.App/Astrid.App.csproj') -c Debug -r $hostRid --self-contained false
    }
    Invoke-Step 'UI smoke tests' { dotnet test $uiTests -c Debug }
}

Write-Host ""
Write-Host "RESULT: OK" -ForegroundColor Green
