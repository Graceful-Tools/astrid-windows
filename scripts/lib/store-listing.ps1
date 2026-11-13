<#
.SYNOPSIS
    The Partner Center listing, filled from a brand profile (task AWTD-1636).

.DESCRIPTION
    `packaging/listing/store-listing.md` is a TEMPLATE. `ConvertTo-AstridStoreListing` fills it for
    one brand profile (scripts/lib/brand-profile.ps1) and `Write-AstridStoreListing` writes the
    result into dist/, beside the .msixupload it describes. The filled copy is what gets pasted
    into Partner Center; the template is not.

    THE CONTRACT, which scripts/tests/store-listing.Tests.ps1 holds it to: with no profile - or
    with `-Brand astrid`, which is the same thing - the output is byte for byte the listing hosted
    Astrid has always submitted. A partner now having a template must not change our submission.

    The template carries three kinds of markup, all of which this file removes:

      {PLACEHOLDER}            an identifying value - a name, an address, a URL. See
                               Get-AstridStoreListingValues for the list. A value the profile does
                               not give is written as a visible "[TO FILL: ...]" hole, never as
                               Astrid's value.

      <!-- brand: astrid -->   prose that is hosted Astrid's own - the marketing copy, and the
      ...                      notes about decisions made for astrid.cc. Hosted Astrid gets the
      <!-- brand: partner -->  first half; anyone else gets the second, which is guidance and
      ...                      "[TO WRITE: ...]" holes. A partner's marketing copy is theirs to
      <!-- brand: end -->      write; the template's job is the structure, the field limits and
                               Partner Center's rules. Each marker is a whole line of its own.

      <!-- template: ... -->   a note to whoever edits the template, dropped from every output.

    WHICH HALF a profile gets is decided by its server: https://astrid.cc is hosted Astrid,
    anything else is a partner. The copy describes a service, and the server is the service.

    UTF-8 in, UTF-8 without a byte order mark out, read and written through System.IO rather than
    Get-Content/Set-Content: Windows PowerShell 5.1 reads a file with no BOM as the system codepage
    and writes UTF-8 WITH one, either of which alone breaks "byte for byte". The listing is full of
    em dashes and umlauts.

    ASCII only, for the same reason scripts/lib/brand-profile.ps1 is.
#>

Set-StrictMode -Version Latest

$script:AstridHostedServerUrl = 'https://astrid.cc'

<#
    Every placeholder the template may use, and its value for this profile. $null means the
    profile does not say, and the listing shows a hole with the hint beside it.
#>
function Get-AstridStoreListingValues {
    param([Parameter(Mandatory = $true)]$BrandProfile)

    $serverHost = $BrandProfile.ServerUrl -replace '^https://', ''
    $providers = (@("$($BrandProfile.AuthProviders)".Split(',') | Where-Object { $_ }) -join ', ')

    return [ordered]@{
        DISPLAY_NAME   = @($BrandProfile.DisplayName, 'the reserved product name')
        SHORT_NAME     = @($BrandProfile.ShortName, 'the short product name')
        DESCRIPTION    = @($BrandProfile.Description, 'a one-line description - NEXT_PUBLIC_BRAND_TAGLINE')
        FILE_STEM      = @($BrandProfile.FileStem, 'the file stem')
        WEBSITE        = @($BrandProfile.ServerUrl, 'the website')
        SERVER_HOST    = @($serverHost, 'the server')
        SUPPORT_EMAIL  = @($BrandProfile.SupportEmail, 'the support address - set NEXT_PUBLIC_BRAND_SUPPORT_EMAIL in the brand profile; certification writes to it and expects a person to answer')
        PRIVACY_URL    = @($BrandProfile.PrivacyUrl, 'the privacy policy - NEXT_PUBLIC_BRAND_PRIVACY_URL')
        OPERATOR_NAME  = @($BrandProfile.OperatorName, 'the copyright holder - NEXT_PUBLIC_BRAND_OPERATOR_NAME')
        AUTH_PROVIDERS = @($providers, 'the sign-in methods - NEXT_PUBLIC_BRAND_AUTH_PROVIDERS')
    }
}

function Test-AstridHostedBrand {
    param([Parameter(Mandatory = $true)]$BrandProfile)
    return ($BrandProfile.ServerUrl -eq $script:AstridHostedServerUrl)
}

function ConvertTo-AstridStoreListing {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Template,
        [Parameter(Mandatory = $true)]$BrandProfile
    )

    # Notes to the template's editor. Whole lines, possibly several, so nothing is left behind.
    $text = [regex]::Replace($Template, '(?ms)^<!-- template:.*?-->[ \t]*\r?\n', '')

    $hosted = Test-AstridHostedBrand $BrandProfile
    $block = '(?ms)^<!-- brand: astrid -->\r?\n(?<astrid>.*?)^<!-- brand: partner -->\r?\n(?<partner>.*?)^<!-- brand: end -->[ \t]*\r?\n'
    $text = [regex]::Replace($text, $block, [System.Text.RegularExpressions.MatchEvaluator] {
            param($match)
            if ($hosted) { return $match.Groups['astrid'].Value }
            return $match.Groups['partner'].Value
        })

    # A marker left over is a block the regex could not pair - a typo in a marker, or one half
    # missing. Named rather than pasted into Partner Center.
    $stray = [regex]::Match($text, '(?m)^<!-- brand:.*$')
    if ($stray.Success) {
        throw "store listing template: unpaired marker '$($stray.Value.Trim())'. Each block is <!-- brand: astrid -->, <!-- brand: partner -->, <!-- brand: end -->, each on a line of its own."
    }

    $values = Get-AstridStoreListingValues $BrandProfile
    foreach ($name in $values.Keys) {
        $value = $values[$name][0]
        if ([string]::IsNullOrWhiteSpace($value)) { $value = "[TO FILL: $($values[$name][1])]" }
        $text = $text.Replace("{$name}", $value)
    }

    $unknown = [regex]::Match($text, '\{[A-Z_]+\}')
    if ($unknown.Success) {
        throw "store listing template: no value for $($unknown.Value). Add it to Get-AstridStoreListingValues in scripts/lib/store-listing.ps1."
    }
    return $text
}

<#
    The holes a filled listing still has, one line each - what is left for a person to do before
    pasting it.
#>
function Get-AstridStoreListingHoles {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Listing)
    # One line each, however many lines the hole spans in the listing (a quoted one carries its
    # "> " prefixes), and cut short: the warning points at the hole, the file says the rest.
    return @([regex]::Matches($Listing, '\[TO (FILL|WRITE): [^\]]*\]') | ForEach-Object {
            $line = [regex]::Replace($_.Value, '\s*\r?\n>?\s*', ' ')
            if ($line.Length -gt 100) { $line = $line.Substring(0, 97) + '...' }
            $line
        })
}

<#
    Writes the filled listing into $OutDir and answers its path. Named after the upload it
    describes - <stem>-<version>.store-listing.md beside <stem>-<version>.msixupload - so two
    brands' outputs in one dist/ cannot be mistaken for each other.
#>
function Write-AstridStoreListing {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$BrandProfile,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$OutDir,
        [string]$Version = ''
    )

    $templatePath = Join-Path $RepoRoot 'packaging/listing/store-listing.md'
    $template = [System.IO.File]::ReadAllText($templatePath, [System.Text.Encoding]::UTF8)
    $listing = ConvertTo-AstridStoreListing -Template $template -BrandProfile $BrandProfile

    $stem = if ($Version) { "$($BrandProfile.FileStem)-$Version" } else { $BrandProfile.FileStem }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $path = Join-Path $OutDir "$stem.store-listing.md"
    [System.IO.File]::WriteAllText($path, $listing, (New-Object System.Text.UTF8Encoding $false))

    $holes = @(Get-AstridStoreListingHoles $listing)
    if ($holes.Count -gt 0) {
        Write-Warning ("$path has $($holes.Count) hole(s) to fill before it is pasted into Partner Center:" +
            [Environment]::NewLine + '  ' + ($holes -join ([Environment]::NewLine + '  ')))
    }
    return $path
}
