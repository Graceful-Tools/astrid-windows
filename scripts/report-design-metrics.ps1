<#
.SYNOPSIS
    Report the hand-written colours, sizes and strings in the shell, against the committed budget.

.DESCRIPTION
    Board task d7e381a7: audit the reduction of inline styles and unique strings with a before and
    an after, and keep the `fixall` and `fixstuff` loops from regressing it. astrid-web does this
    with a report script and a ratchet test; this is the same pair.

    The counting is NOT done here. `DesignMetrics.cs` in the test project is the one implementation,
    because a second counter in PowerShell would drift from the one in the gate and the drift would
    be invisible — the reports would simply disagree. So this script runs that test with
    `ASTRID_DESIGN_METRICS_OUT` pointing at a temporary file, which makes it write the counts
    instead of asserting them, and then prints them beside `app/Astrid.App.Tests/design-metrics.json`.

    What the counts cover, and what they deliberately do not: every XAML file the window is made of
    EXCEPT `App.xaml` and `Views/ShellResources.xaml`, which are where the tokens are written down —
    a hex colour there is the design system, not drift from it. XAML strings are absent because
    `LocalisationTests` already holds untranslatable literals at zero; `code.strings` is the other
    half of docs/ASTRID.md rule 10, the prose built in C#, which no `x:Uid` can reach.

.PARAMETER Update
    Copy the freshly counted numbers over the committed budget. Needed when a count falls (the gate
    asks for it, so the room just won is kept) and when a rise is genuinely intended — in which case
    say why in the commit message, because nothing else records it.

.EXAMPLE
    powershell -File scripts/report-design-metrics.ps1

.EXAMPLE
    powershell -File scripts/report-design-metrics.ps1 -Update
#>
[CmdletBinding()]
param(
    [switch]$Update
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'app/Astrid.App.Tests/Astrid.App.Tests.csproj'
$committed = Join-Path $repo 'app/Astrid.App.Tests/design-metrics.json'
$fresh = Join-Path ([System.IO.Path]::GetTempPath()) 'astrid-design-metrics.json'

if (Test-Path $fresh) { Remove-Item $fresh -Force }

$env:ASTRID_DESIGN_METRICS_OUT = $fresh
try {
    & dotnet test $project --filter 'FullyQualifiedName~DesignMetricsTests' --nologo -v q | Out-Host
    $failed = $LASTEXITCODE -ne 0
}
finally {
    Remove-Item Env:\ASTRID_DESIGN_METRICS_OUT
}

if ($failed -or -not (Test-Path $fresh)) {
    throw "the counting test did not run; see the output above"
}

$counts = (Get-Content $fresh -Raw | ConvertFrom-Json).counts
$budget = if (Test-Path $committed) { (Get-Content $committed -Raw | ConvertFrom-Json).counts } else { $null }

''
'{0,-22} {1,9} {2,7} {3,7}' -f 'metric', 'budgeted', 'now', 'delta'
'-' * 48
foreach ($metric in $counts.PSObject.Properties.Name) {
    $now = [int]$counts.$metric
    if ($null -eq $budget -or $null -eq $budget.$metric) {
        '{0,-22} {1,9} {2,7} {3,7}' -f $metric, '-', $now, 'new'
        continue
    }
    $was = [int]$budget.$metric
    $delta = $now - $was
    $shown = if ($delta -gt 0) { "+$delta" } elseif ($delta -eq 0) { '' } else { "$delta" }
    '{0,-22} {1,9} {2,7} {3,7}' -f $metric, $was, $now, $shown
}
''

if ($Update) {
    Copy-Item $fresh $committed -Force
    "budget updated: $committed"
}
else {
    'the committed budget is unchanged; pass -Update to rewrite it'
}
