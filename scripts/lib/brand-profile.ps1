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
        NEXT_PUBLIC_BRAND_INBOUND_TASK_EMAIL  the address a task can be mailed to
        NEXT_PUBLIC_BRAND_AGENT_NAME       what the assistant is called on screen
        NEXT_PUBLIC_BRAND_WORDMARK         the handle it is mentioned by, without the "@"
        NEXT_PUBLIC_BRAND_AUTH_PROVIDERS   the ways a reader may sign in, in the order shown
        NEXT_PUBLIC_BRAND_SUPPORT_EMAIL    the Store listing's support contact
        NEXT_PUBLIC_BRAND_PRIVACY_URL      the Store listing's privacy policy
        NEXT_PUBLIC_BRAND_OPERATOR_NAME    the Store listing's copyright line

    The last three are read only by the Store listing (scripts/lib/store-listing.ps1, task
    AWTD-1636), and none of them falls back to Astrid's once the profile names another domain:
    the privacy policy becomes the deployment's own built-in /privacy page, the copyright holder
    the publisher, and the support address NOTHING - it is the address Microsoft's certification
    writes to and expects a person to answer, so a guessed one fails certification where a
    visible hole only costs a line of typing.

    AUTH PROVIDERS ARE RESOLVED HERE, not in the app, because this is the only thing on the Windows
    side that reads a profile. The rule is astrid-web's (lib/brand/auth-providers.ts): a stated list
    wins and keeps its order; with none stated the three legacy switches decide, each ON unless
    turned off, in their documented order; and github and sso are never on unless listed, because a
    provider that needs credentials must not appear by default.

    The last three are the words a reader sees rather than the package's identity (task 8d241526),
    and each falls back the way astrid-web's does: the agent's name and handle to the brand name,
    the address to remindme@<this brand's domain>.

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
        InboundTaskEmail     = 'remindme@astrid.cc'
        AgentName            = 'Astrid'
        Wordmark             = 'astrid'
        # What hosted Astrid actually offers on its sign-in page, which is what the card has always
        # said. NOT astrid-web's own switch default of google+apple+passkey: the Apple button is
        # drawn only where NextAuth has Apple credentials too, and astrid.cc has none.
        AuthProviders        = 'passkey,google'
        AssetsDirectory      = (Join-Path $RepoRoot 'packaging/Assets')
        IdentityName         = $null
        IdentityPublisher    = $null
        # The stem of every file the build WRITES - the .msix, the .msixbundle, the .msixupload.
        # Derived from ShortName below rather than set here, so a profile that moves the short name
        # moves the file names with it and there is nothing extra for a partner to fill in. The one
        # brand value that outlived the manifest work, because it lives in a path rather than in the
        # manifest: a partner's upload reached Partner Center called Astrid-1.2.3.msixupload
        # (task 5bf1f1dc).
        FileStem             = 'Astrid'
        # The Store listing's three (task AWTD-1636), exactly as packaging/listing/store-listing.md
        # has always said them.
        SupportEmail         = 'support@astrid.cc'
        PrivacyUrl           = 'https://www.astrid.cc/privacy'
        OperatorName         = 'Graceful Tools LLC'
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
    $inbound = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_INBOUND_TASK_EMAIL'
    $agentName = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_AGENT_NAME'
    $wordmark = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_WORDMARK'
    $supportEmail = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_SUPPORT_EMAIL'
    $privacyUrl = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_PRIVACY_URL'
    $operatorName = Get-AstridBrandMember $shared 'NEXT_PUBLIC_BRAND_OPERATOR_NAME'

    $profileName = Get-AstridBrandMember $json 'name'
    if ($profileName) { $values.Name = $profileName }

    # The order is the fallback chain: a profile with only NEXT_PUBLIC_BRAND_NAME still renames
    # everything a reader sees, which is what makes a one-line partner profile work.
    if ($name) {
        $values.DisplayName = $name
        $values.PublisherDisplayName = $name
        $values.ShortName = $name
        $values.SchemeDisplayName = $name
        # Most deployments call the assistant after the product, and the handle it is mentioned by
        # is that name in lower case - astrid-web's BRAND.agentIdentityName and BRAND.wordmark,
        # which fall back the same way. A deployment that disagrees says so below.
        $values.AgentName = $name
        $values.Wordmark = $name.ToLowerInvariant()
    }
    if ($title) { $values.DisplayName = $title }
    if ($tagline) { $values.Description = $tagline }
    if ($scheme) { $values.Scheme = $scheme }
    if ($domain) {
        $values.ServerUrl = "https://$domain"
        # THE BRAND'S OWN DOMAIN, never ours. astrid-web's BRAND.inboundTaskEmail falls back to the
        # literal remindme@astrid.cc, which is harmless on a server that knows its own routing and
        # is not harmless in a window: it is the address a partner's user would be told to mail,
        # and it would file their task in someone else's account. A deliberate divergence from web.
        $values.InboundTaskEmail = "remindme@$domain"
        # The deployment's own policy page, which astrid-web serves at /privacy unless the operator
        # publishes one elsewhere (NEXT_PUBLIC_BRAND_PRIVACY_URL, below). The support address has
        # no such answer - see the header - so it is emptied rather than guessed.
        $values.PrivacyUrl = "https://$domain/privacy"
        $values.SupportEmail = $null
    }
    if ($inbound) { $values.InboundTaskEmail = $inbound }
    if ($agentName) { $values.AgentName = $agentName }
    if ($wordmark) { $values.Wordmark = $wordmark.ToLowerInvariant() }
    if ($supportEmail) { $values.SupportEmail = $supportEmail }
    if ($privacyUrl) { $values.PrivacyUrl = $privacyUrl }
    $values.AuthProviders = Get-AstridBrandAuthProviders $shared

    # The Windows block, last, because it is the only one written for this platform and so it wins.
    foreach ($pair in @(
            @('displayName', 'DisplayName'),
            @('publisherDisplayName', 'PublisherDisplayName'),
            @('description', 'Description'),
            @('shortName', 'ShortName'),
            @('scheme', 'Scheme'),
            @('schemeDisplayName', 'SchemeDisplayName'),
            @('serverUrl', 'ServerUrl'),
            @('inboundTaskEmail', 'InboundTaskEmail'),
            @('agentName', 'AgentName'),
            @('wordmark', 'Wordmark'),
            @('identityName', 'IdentityName'),
            @('identityPublisher', 'IdentityPublisher'),
            @('supportEmail', 'SupportEmail'),
            @('privacyUrl', 'PrivacyUrl'))) {
        $value = Get-AstridBrandMember $windows $pair[0]
        if ($value) { $values[$pair[1]] = $value }
    }

    # The copyright holder: the operator the profile names, else whoever settled as publisher -
    # which is the brand name when the profile says nothing, astrid-web's operatorName rule
    # (lib/brand/config.ts). Never Graceful Tools LLC by default on a partner's listing: that is a
    # legal statement about someone else's company.
    $values.OperatorName = $values.PublisherDisplayName
    if ($operatorName) { $values.OperatorName = $operatorName }
    $windowsOperator = Get-AstridBrandMember $windows 'operatorName'
    if ($windowsOperator) { $values.OperatorName = $windowsOperator }

    $assets = Get-AstridBrandMember $windows 'assets'
    if ($assets) {
        # Relative to the profile, so a partner's tiles travel with the file that names them.
        $values.AssetsDirectory = [System.IO.Path]::GetFullPath(
            (Join-Path (Split-Path -Parent $file) $assets))
    }

    # Last, so it follows whichever of the three places above settled ShortName.
    $values.FileStem = Get-AstridBrandFileStem $values.ShortName

    Assert-AstridBrandProfile $values $file
    return [pscustomobject]$values
}

<#
    A short name reduced to something that can be a file name.

    DisplayName and ShortName are prose - "Acme Tasks", and nothing stops a partner using a comma
    or a colon - while this ends up in a path that makeappx writes and Partner Center reads. So
    letters, digits, hyphens and underscores survive; a space becomes a hyphen, because dropping it
    would run two words together; everything else goes. Astrid's own 'Astrid' is unchanged, which is
    the point: hosted Astrid keeps writing Astrid-<version>-arm64.msix.

    Deliberately NOT a fallback to 'Astrid' when nothing survives - that would put our name on a
    partner's upload, which is the failure this whole file exists to prevent. Assert-AstridBrandProfile
    rejects it instead.
#>
<#
    The sign-in providers a profile offers, as a comma list in the order they are shown.

    astrid-web's resolveAuthProviders (lib/brand/auth-providers.ts), ported rather than invented so
    one profile describes one deployment across platforms:

      - A stated NEXT_PUBLIC_BRAND_AUTH_PROVIDERS wins, in its own order. Unknown names and repeats
        are dropped, and a legacy switch turned off still removes its provider from the list - a
        deployment that turned Google off stays that way however the list is written.
      - With nothing stated, the three legacy switches decide, each ON unless turned off, in web's
        order: google, apple, passkey.
      - github and sso are reached only by listing them. They need credentials, and the "on unless
        switched off" convention would sprout buttons that fail.

    A profile that resolves to nothing at all is NOT an error here. It is a deployment with no way
    in, which astrid-web's boot preflight is the thing that refuses; a Windows package that would
    not build over it only makes that harder to diagnose. The card falls back to this brand's own
    methods, which is the least misleading sentence available.
#>
function Get-AstridBrandAuthProviders {
    param($Shared)

    $known = @('github', 'google', 'apple', 'passkey', 'sso')
    $legacy = [ordered]@{ google = $true; apple = $true; passkey = $true }
    foreach ($id in @($legacy.Keys)) {
        $switch = Get-AstridBrandMember $Shared "NEXT_PUBLIC_BRAND_ENABLE_AUTH_$($id.ToUpperInvariant())"
        if ($null -ne $switch) {
            # Web's `enabled`: anything but an explicit off is on.
            $legacy[$id] = @('false', '0', 'off', 'no') -notcontains "$switch".Trim().ToLowerInvariant()
        }
    }

    $stated = Get-AstridBrandMember $Shared 'NEXT_PUBLIC_BRAND_AUTH_PROVIDERS'
    $named = @()
    if ($stated) {
        $named = @("$stated".Split(',') | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
    }
    if ($named.Count -eq 0) {
        $named = @($legacy.Keys | Where-Object { $legacy[$_] })
    }

    $resolved = @()
    foreach ($id in $named) {
        if ($known -notcontains $id -or $resolved -contains $id) { continue }
        if ($legacy.Contains($id) -and -not $legacy[$id]) { continue }
        $resolved += $id
    }
    return ($resolved -join ',')
}

function Get-AstridBrandFileStem {
    param([string]$ShortName)

    $collapsed = ($ShortName -replace '\s+', '-')
    return ($collapsed -replace '[^A-Za-z0-9_-]', '')
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
    # Printed on the Tasks settings page as the address to mail a task to. A typo here is a line a
    # reader copies, mails, and never hears back from - and nothing else in the build would notice.
    if ($Values.InboundTaskEmail -notmatch '^[^@\s]+@[^@\s]+$') {
        throw "brand profile $File has an unusable inbound task address '$($Values.InboundTaskEmail)': one local part, one host, no spaces."
    }
    # A short name with nothing a file name can keep - "!!!" - would otherwise be a stem of '' and
    # a package called "-1.2.3-arm64.msix". Named here rather than inherited from Astrid: our name
    # on a partner's Store upload is worse than a build that stops.
    if ($Values.FileStem -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]*$') {
        throw "brand profile $File has a short name '$($Values.ShortName)' that leaves nothing usable as a file name: letters, digits, hyphens and underscores."
    }
    # Both pasted into Partner Center, and certification follows each one. Empty support is allowed
    # (the listing shows a hole for it); a malformed one is not.
    if ($Values.SupportEmail -and $Values.SupportEmail -notmatch '^[^@\s]+@[^@\s]+$') {
        throw "brand profile $File has an unusable support address '$($Values.SupportEmail)': one local part, one host, no spaces."
    }
    if ($Values.PrivacyUrl -notmatch '^https://[^/\s]+(/\S*)?$') {
        throw "brand profile $File has an unusable privacy policy URL '$($Values.PrivacyUrl)': https, a host and an optional path."
    }
    if (-not (Test-Path -LiteralPath $Values.AssetsDirectory)) {
        throw "brand profile $File names an assets directory that does not exist: $($Values.AssetsDirectory)"
    }
}
