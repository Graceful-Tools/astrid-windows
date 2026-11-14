#Requires -Version 5.1
<#
.SYNOPSIS
    Format a `-p:Name=Value` argument so MSBuild reads the whole value. Task 13c6af65.

.DESCRIPTION
    THE BUG. `package.ps1` hands the brand profile to `dotnet publish` as MSBuild properties, and
    on 2026-11-13 one of those values gained a comma — `AuthProviders` became `passkey,google`.
    MSBuild SPLITS `-p:` on commas and semicolons, so it took `passkey` as the value and then tried
    to parse `google` as a switch of its own:

        MSBUILD : error MSB1006: Property is not valid.
        Switch: google

    No MSIX could be built, for any brand or architecture, from then until this was fixed — and
    `npm run predeploy` never runs `package.ps1`, so the gate stayed green throughout.

    WHY A HELPER RATHER THAN AN ESCAPE AT THE ONE CALL SITE. Seven brand values go across this way,
    and a partner can put anything in any of them: a wordmark with a comma, a short name with a
    semicolon, a list of inbound addresses. Each would fail exactly the same way, and a partner
    whose first build dies with `Switch: <half their company name>` has been given a puzzle rather
    than an error. Escaping is a property of the boundary, so it belongs at the boundary.

    THE ORDER MATTERS. `%` is MSBuild's escape character, so it has to be escaped FIRST — otherwise
    `%2C` written by this function would itself be re-escaped on the next pass, or a literal `%2C`
    typed by a partner would silently become a comma. Everything else can follow in any order.
#>

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    One `-p:Name=Value` argument, with Value escaped for MSBuild's command line.

.PARAMETER Name
    The property name. Not escaped: a property name is an identifier, and one needing an escape is
    a mistake worth failing on rather than encoding.

.PARAMETER Value
    The value. Null or empty gives `-p:Name=`, which MSBuild reads as an empty property — that is
    the right answer for a brand field a profile left out, and matches what the unescaped string
    interpolation did before.
#>
function Format-MsBuildProperty {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [AllowNull()][AllowEmptyString()][string] $Value
    )

    if ($Name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "not an MSBuild property name: '$Name'"
    }

    return "-p:$Name=$(ConvertTo-MsBuildValue $Value)"
}

<#
.SYNOPSIS
    Escape a string so MSBuild reads it as one value.

.DESCRIPTION
    `%` first, for the reason in this file's header. Then the two characters that end a value on the
    command line (`,` and `;`), and the four that MSBuild's own expression syntax would otherwise
    act on inside a property value (`$`, `@`, `'`, `"`). MSBuild un-escapes `%XX` when it reads the
    property, so what the build sees is exactly what was passed in.
#>
function ConvertTo-MsBuildValue {
    [CmdletBinding()]
    param([Parameter(Mandatory = $false)][AllowNull()][AllowEmptyString()][string] $Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return ''
    }

    $escaped = $Value.Replace('%', '%25')
    $escaped = $escaped.Replace(',', '%2C')
    $escaped = $escaped.Replace(';', '%3B')
    $escaped = $escaped.Replace('$', '%24')
    $escaped = $escaped.Replace('@', '%40')
    $escaped = $escaped.Replace("'", '%27')
    $escaped = $escaped.Replace('"', '%22')
    return $escaped
}
