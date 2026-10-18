<#
.SYNOPSIS
    Run a native build tool, stay quiet when it works, and say why when it does not.

.DESCRIPTION
    `package.ps1` and `sign-local.ps1` drive three native executables - dotnet, makeappx and
    signtool - and all three are chatty on success. makeappx alone enumerates every packed file,
    268 of them for this app, and those scripts are meant to print five deliberate lines. So the
    output was piped to `Out-Null`, and with it went the only description of any failure: a red
    build said

        makeappx pack failed for arm64

    and nothing else, four minutes into a publish. On 2026-10-18 the cause was a line makeappx had
    already printed and `Out-Null` had already eaten - "error C00CEE23 ... Line 57, Column 39" -
    which named the fault exactly. Task 5a9cb679.

    THE TRAP THIS EXISTS TO CONTAIN. Both callers set `$ErrorActionPreference = 'Stop'`, and under
    Stop, Windows PowerShell 5.1 turns a native executable's stderr into a TERMINATING error the
    moment it is redirected. It does not matter whether the redirect goes to a variable or to a
    file; measured against a .cmd that writes one stderr line and exits 0:

        $out = & $exe 2>&1        under Stop -> throws (RemoteException)
        & $exe *> $file           under Stop -> throws (RemoteException)
        the same, EAP = Continue  -> fine, exit 0, both lines captured

    A tool that merely WARNS on stderr would therefore fail the build. dotnet and makeappx both
    do, which is why capturing output has to narrow `$ErrorActionPreference` for exactly the
    length of the call and put it back afterwards - and why this is one function rather than the
    same four lines copied to six call sites, five of which would eventually get it wrong.

    `$LASTEXITCODE` is the verdict, never `$?`: under the same redirect `$?` reads false on a
    zero exit, so a success would throw. `scripts/tests/packaging-scripts.Tests.ps1` holds all of
    this in place.

    This file is ASCII only, so it reads the same under any codepage Windows PowerShell 5.1 picks.
#>

function Invoke-BuildTool {
    <#
    .PARAMETER Message
        What to throw if the tool fails. Phrased as the callers already phrased it, e.g.
        "makeappx pack failed for arm64".

    .PARAMETER Tool
        A scriptblock holding the native invocation and nothing else.

    .EXAMPLE
        Invoke-BuildTool "makeappx pack failed for $architecture" {
            & $makeappx.FullName pack /d $publish /p $package /o
        }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$Message,
        [Parameter(Mandatory = $true, Position = 1)][scriptblock]$Tool
    )

    # Narrowed to the call and restored in the finally, so a tool that writes a warning to stderr
    # cannot terminate the script - see THE TRAP above.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Tool 2>&1
    } finally {
        $ErrorActionPreference = $previous
    }

    if ($LASTEXITCODE -ne 0) {
        # Indented under the caller's own "[*] ..." line, and only on failure - the success path
        # stays as quiet as `Out-Null` ever was.
        $output | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw $Message
    }
}
