<#
.SYNOPSIS
    Writes the Partner Center listing for a brand into dist/, without building a package.

.DESCRIPTION
    `scripts/package.ps1` writes the same file beside the .msixupload at the end of a publish. This
    is that one step on its own (task AWTD-1636), for when the listing is what changed - a partner
    filling in their profile, or a check of what a profile produces - and twenty minutes of
    publishing would buy nothing.

    The template is packaging/listing/store-listing.md; scripts/lib/store-listing.ps1 says how it is
    filled. With no -Brand the output is hosted Astrid's listing, byte for byte.

.PARAMETER Brand
    A brand profile name or a path to a *.brand.json file, as scripts/package.ps1 takes it. Empty
    means hosted Astrid.

.PARAMETER Version
    The package version the listing goes with. It names the file
    (<stem>-<version>.store-listing.md, beside <stem>-<version>.msixupload); empty names it
    <stem>.store-listing.md.

.PARAMETER OutDir
    Where to write it. Defaults to dist/, where the upload is.

.EXAMPLE
    powershell -File scripts/store-listing.ps1 -Brand acme -Version 1.2.3.0
#>
param(
    [string]$Brand = '',
    [string]$Version = '',
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot 'lib\brand-profile.ps1')
. (Join-Path $PSScriptRoot 'lib\store-listing.ps1')

if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'dist' }

$brandProfile = Get-AstridBrandProfile -Brand $Brand -RepoRoot $repoRoot
$path = Write-AstridStoreListing -BrandProfile $brandProfile -RepoRoot $repoRoot -OutDir $OutDir -Version $Version
Write-Host "[+] $path" -ForegroundColor Green
