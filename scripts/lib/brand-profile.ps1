<#
.SYNOPSIS
    The brand values a packaged build is made with: Astrid's, or a partner's.

.DESCRIPTION
    `Get-AstridBrandProfile` answers one object with every value `scripts/package.ps1` has to put
    into the manifest, the package layout and the build. With no -Brand it answers today's Astrid
    values, exactly; that is the contract the tests hold it to, because hosted Astrid must not
    change.

    A profile is a `*.brand.json` file, the SAME shape astrid-web and the Apple apps read
    (astrid-web/brands/README.md). One profile describes a deployment across platforms, so the
    values this script needs come from the shared `env` block wherever the shared block has them:

        NEXT_PUBLIC_BRAND_TITLE            the name in Start, Settings and the Store
        NEXT_PUBLIC_BRAND_NAME             the short name on the tile, and the fallback for the above
        NEXT_PUBLIC_BRAND_TAGLINE          the one-line description
        NEXT_PUBLIC_BRAND_APP_URL_SCHEME   the scheme the installer registers, without "://"
        NEXT_PUBLIC_BRAND_DOMAIN           the server the build talks to, as https://<domain>

    Four things are Windows's alone and have nowhere to live in the shared block, so they come from
    an optional `windows` object in the same file - the same arrangement as the `ios` block
    (astrid-web task 1ada60b3):

        publisherDisplayName   must match the Partner Center account's publisher display name
        identityName           }  assigned by Partner Center for THAT account; overrides
        identityPublisher      }  packaging/store-identity.json when present
        assets                 a directory of tiles, relative to the profile, replacing packaging/Assets

    Anything else in the file is another platform's and is skipped, not rejected.

    A name is resolved against packaging/brands/ first and then ../astrid-web/brands/, so this repo
    can be built and tested on its own - CI checks out astrid-windows only - while a partner keeps
    one profile beside the web deployment it belongs to. A path to a .json file is taken as given.

    PUBLISHER DISPLAY NAME FALLS BACK TO THE BRAND NAME, not to Graceful Tools LLC. A partner who
    forgets it ships their own name, which is wrong but theirs; inheriting ours would put Graceful
    Tools in a partner's Store listing, and that is the failure worth preventing.

    ASCII only, deliberately. Windows PowerShell 5.1 reads a .ps1 with no byte order mark as the
    system codepage, which turns any non-ASCII character in a string into a parse error pointing at
    the wrong line - so this file stays inside ASCII rather than depending on how it was saved.
#>

Set-StrictMode -Version Latest

function Get-AstridBrandProfile {
    [CmdletBinding()]
    param(
        # A profile name (resolved as described above) or a path to a *.brand.json file.
        # Empty means hosted Astrid, and must keep meaning exactly that.
        [string]$Brand = '',

        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    # Today's values, read as the defaults every profile overrides one field at a time. A profile
    # that names nothing therefore cannot accidentally blank something out.
    #
    # Named $values rather than $profile: $PROFILE is an automatic variable, and a local that
    # shadows it reads like a bug even where it behaves.
    $values = [ordered]@{
        Name                 = 'astrid'
        DisplayName          = 'Astrid Tasks'
        PublisherDisplayName = 'Graceful Tools LLC'
        Description          = 'Tasks, shared with the people you share them with.'
        ShortName            = 'Astrid'
        Scheme               = 'astrid'
        SchemeDisplayName    = 'Astrid'
        ServerUrl            = 'https://astrid.cc'
        AssetsDirectory      = (Join-Path $RepoRoot 'packaging/Assets')
        IdentityName         = $null
        IdentityPublisher    = $null
    }

    if ([string]::IsNullOrWhiteSpace($Brand)) {
        return [pscustomobject]$values
    }

    $file = Resolve-AstridBrandFile -Brand $Brand -RepoRoot $RepoRoot
    $json = Get-Content $file -Raw | ConvertFrom-Json
    $shared = Get-AstridBrandMember $json 'env'
    $windows = Get-AstridBrandMember $json 'windows'

    $name = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_NAME'
    $title = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_TITLE'
    $tagline = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_TAGLINE'
    $scheme = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_APP_URL_SCHEME'
    $domain = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_DOMAIN'

    $profileName = Get-AstridBrandMember $json 'name'
    if ($profileName) { $values.Name = $profileName }

    # The order is the fallback chain: a profile with only NEXT_PUBLIC_BRAND_NAME still renames
    # everything a reader sees, which is what makes a one-line partner profile work.
    if ($name) {
        $values.DisplayName = $name
        $values.PublisherDisplayName = $name
        $values.ShortName = $name
        $values.SchemeDisplayName = $name
    }
    if ($title) { $values.DisplayName = $title }
    if ($tagline) { $values.Description = $tagline }
    if ($scheme) { $values.Scheme = $scheme }
    if ($domain) { $values.ServerUrl = "https://$domain" }

    # The Windows block, last, because it is the only one written for this platform and so it wins.
    foreach ($pair in @(
            @('displayName', 'DisplayName'),
            @('publisherDisplayName', 'PublisherDisplayName'),
            @('description', 'Description'),
            @('shortName', 'ShortName'),
            @('scheme', 'Scheme'),
            @('schemeDisplayName', 'SchemeDisplayName'),
            @('serverUrl', 'ServerUrl'),
            @('identityName', 'IdentityName'),
            @('identityPublisher', 'IdentityPublisher'))) {
        $value = Get-AstridBrandMember $windows $pair[0]
        if ($value) { $values[$pair[1]] = $value }
    }

    $assets = Get-AstridBrandMember $windows 'assets'
    if ($assets) {
        # Relative to the profile, so a partner's tiles travel with the file that names them.
        $values.AssetsDirectory = [System.IO.Path]::GetFullPath(
            (Join-Path (Split-Path -Parent $file) $assets))
    }

    Assert-AstridBrandProfile $values $file
    return [pscustomobject]$values
}

function Resolve-AstridBrandFile {
    param([string]$Brand, [string]$RepoRoot)

    if ($Brand -like '*.json') {
        if (-not (Test-Path -LiteralPath $Brand)) {
            throw "brand profile not found: $Brand"
        }
        return (Resolve-Path -LiteralPath $Brand).Path
    }

    $candidates = @(
        (Join-Path $RepoRoot "packaging/brands/$Brand.brand.json"),
        (Join-Path $RepoRoot "../astrid-web/brands/$Brand.brand.json")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw ("no brand profile named '$Brand'. Looked in:" + [Environment]::NewLine +
        '  ' + ($candidates -join ([Environment]::NewLine + '  ')))
}

<#
    A member of a ConvertFrom-Json object, or $null. ConvertFrom-Json returns a PSCustomObject, so
    an absent key is not $null but a missing property - which under Set-StrictMode throws rather
    than reading as empty.
#>
function Get-AstridBrandMember {
    param($Object, [string]$Name)

    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    if ($property.Value -is [string] -and [string]::IsNullOrWhiteSpace($property.Value)) { return $null }
    return $property.Value
}

<#
    What the package cannot be built without. Each of these fails LATE otherwise - a bad scheme is
    a manifest makeappx rejects, a missing assets directory is an install that stops on a missing
    splash screen, and a server with no scheme is a build that signs in to nothing. Named here
    instead, before a twenty-minute publish.
#>
function Assert-AstridBrandProfile {
    param($Values, [string]$File)

    # RFC 3986 scheme, minus the "+" and "." a Windows protocol registration will not take.
    if ($Values.Scheme -notmatch '^[a-z][a-z0-9-]*$') {
        throw "brand profile $File has an unusable URL scheme '$($Values.Scheme)': lower-case letters, digits and hyphens, starting with a letter."
    }
    if ($Values.ServerUrl -notmatch '^https://[^/\s]+$') {
        throw "brand profile $File has an unusable server URL '$($Values.ServerUrl)': https and a host, no path."
    }
    if (-not (Test-Path -LiteralPath $Values.AssetsDirectory)) {
        throw "brand profile $File names an assets directory that does not exist: $($Values.AssetsDirectory)"
    }
}
