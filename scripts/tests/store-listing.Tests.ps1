#Requires -Version 5.1
<#
.SYNOPSIS
    The Store listing is a template a brand profile fills, and hosted Astrid's is unchanged by it.
    Task AWTD-1636.

.DESCRIPTION
    `packaging/listing/store-listing.md` was 250 lines of Astrid - the product name, the support
    address, the privacy policy, the copyright line, the upload's file name, and a certification
    note telling a Microsoft tester to sign in at astrid.cc. A partner had no starting point but to
    rewrite it. It is now a template (`{PLACEHOLDER}`s for identifying values, `brand: astrid` /
    `brand: partner` blocks for prose that is ours) and scripts/lib/store-listing.ps1 fills it.

    THE ASSERTION THAT MATTERS: with no profile the output is byte for byte the listing hosted
    Astrid submitted before the template existed. It is held to that file's SHA-256 rather than to
    a copy of it, so there is no second 250-line file to drift. A DELIBERATE change to the hosted
    listing changes the hash, and updating `$HostedListingSha256` below in the same commit is how
    that change says it meant to.

    The other direction: a partner's output carries no Astrid value anywhere - not in a field, not
    in a note - and every value the profile does not give is a visible hole, never ours.

    Run, not read: "the output is identical" is the claim a scan of the template cannot make.

    This file is ASCII only, so it reads the same under any codepage Windows PowerShell 5.1 picks.
#>
param()

$ErrorActionPreference = 'Continue'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $RepoRoot 'scripts\lib\brand-profile.ps1')
. (Join-Path $RepoRoot 'scripts\lib\store-listing.ps1')

# packaging/listing/store-listing.md as submitted for hosted Astrid, before it became a template.
$HostedListingSha256 = '91E701107B14D535885ABE380D939206DD5F67876371F47E781E825260199904'

$TemplatePath = Join-Path $RepoRoot 'packaging\listing\store-listing.md'
$Template = [System.IO.File]::ReadAllText($TemplatePath, [System.Text.Encoding]::UTF8)

$script:Failures = 0
$script:Ran = 0
# Assertions MADE, not passed - see fixall-loop-guards.Tests.ps1 for why a case that exits early
# is the one outcome a failure count cannot see.
$script:Checks = 0

function Assert-Equal([string]$What, $Expected, $Actual) {
    $script:Checks++
    if ([string]$Expected -ceq [string]$Actual) { return }
    Write-Host "    FAIL $What" -ForegroundColor Red
    Write-Host "      expected: [$Expected]" -ForegroundColor Red
    Write-Host "      actual:   [$Actual]" -ForegroundColor Red
    $script:Failures++
}

function Assert-True([string]$What, $Actual) { Assert-Equal $What 'True' ([bool]$Actual) }

function Assert-Throws([string]$What, [string]$Pattern, [scriptblock]$Body) {
    $script:Checks++
    try { & $Body }
    catch {
        if ($_.Exception.Message -match $Pattern) { return }
        Write-Host "    FAIL $What - threw the wrong thing: $($_.Exception.Message)" -ForegroundColor Red
        $script:Failures++
        return
    }
    Write-Host "    FAIL $What - did not throw" -ForegroundColor Red
    $script:Failures++
}

function Invoke-Case([string]$Name, [scriptblock]$Body) {
    $script:Ran++
    Write-Host "  - $Name"
    $before = $script:Checks
    try { & $Body }
    catch {
        Write-Host "    FAIL the case threw: $($_.Exception.Message)" -ForegroundColor Red
        $script:Failures++
    }
    finally {
        if ($script:Checks -eq $before) {
            Write-Host '    FAIL the case made no assertions - it exited early' -ForegroundColor Red
            $script:Failures++
        }
    }
}

function Get-Sha256([byte[]]$Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return (($sha.ComputeHash($Bytes) | ForEach-Object { $_.ToString('X2') }) -join '') }
    finally { $sha.Dispose() }
}

function New-TempDirectory {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("store-listing-test-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    return $path
}

Write-Host 'store listing template (task AWTD-1636)'

Invoke-Case 'no profile writes the hosted listing byte for byte' {
    $out = New-TempDirectory
    try {
        $path = Write-AstridStoreListing -BrandProfile (Get-AstridBrandProfile -RepoRoot $RepoRoot) `
            -RepoRoot $RepoRoot -OutDir $out -Version '1.2.3.0' -WarningAction SilentlyContinue
        Assert-Equal 'it is named after the upload it describes' 'Astrid-1.2.3.0.store-listing.md' (Split-Path -Leaf $path)
        $bytes = [System.IO.File]::ReadAllBytes($path)
        Assert-True 'it has no byte order mark' (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF))
        Assert-Equal 'its SHA-256 is the hosted listing''s (a deliberate edit updates $HostedListingSha256)' `
            $HostedListingSha256 (Get-Sha256 $bytes)
    }
    finally { Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue }
}

Invoke-Case 'the astrid profile writes the same listing as no profile' {
    $unbranded = ConvertTo-AstridStoreListing -Template $Template -BrandProfile (Get-AstridBrandProfile -RepoRoot $RepoRoot)
    $astrid = ConvertTo-AstridStoreListing -Template $Template -BrandProfile (Get-AstridBrandProfile -RepoRoot $RepoRoot -Brand astrid)
    Assert-True 'identical' ($unbranded -ceq $astrid)
}

Invoke-Case 'a partner listing carries no Astrid value' {
    $acme = ConvertTo-AstridStoreListing -Template $Template -BrandProfile (Get-AstridBrandProfile -RepoRoot $RepoRoot -Brand acme)
    # Case-sensitive "Astrid": ASTRID_LANGUAGE, an environment variable the app reads, is a name
    # in code and stays. Everything a reader would take for the product is caught.
    foreach ($leak in @('Astrid', 'astrid.cc', 'Graceful Tools', 'Jon', 'grace@')) {
        Assert-True "no '$leak'" (-not $acme.Contains($leak))
    }
    Assert-True 'no placeholder left' (-not ($acme -match '\{[A-Z_]+\}'))
    Assert-True 'no marker left' (-not ($acme -match '<!--'))
    Assert-True 'the product name is the brand''s' $acme.Contains('# Microsoft Store listing ' + [char]0x2014 + ' Acme Tasks')
    Assert-True 'the upload is the brand''s' $acme.Contains('dist/Acme-<version>.msixupload')
    Assert-True 'the copyright is the publisher''s' $acme.Contains([char]0x00A9 + ' 2026 Acme Corporation')
    Assert-True 'the website is the brand''s' $acme.Contains("### Website`n`nhttps://tasks.acme.example`n")
    Assert-True 'the privacy policy is the deployment''s own page' $acme.Contains('https://tasks.acme.example/privacy')
    Assert-True 'the certification note names the brand''s server' $acme.Contains('default browser at tasks.acme.example')
    # acme names no support address, so the listing must say so rather than borrow one.
    Assert-True 'the missing support address is a hole' $acme.Contains('[TO FILL: the support address')
    Assert-True 'the marketing copy is left for the partner' $acme.Contains('[TO WRITE: your description')
}

Invoke-Case 'a profile that names the listing values gets them' {
    $dir = New-TempDirectory
    try {
        $file = Join-Path $dir 'beta.brand.json'
        $json = '{ "name": "beta", "env": { "NEXT_PUBLIC_BRAND_NAME": "Beta", "NEXT_PUBLIC_BRAND_DOMAIN": "beta.example",' +
        ' "NEXT_PUBLIC_BRAND_SUPPORT_EMAIL": "help@beta.example", "NEXT_PUBLIC_BRAND_PRIVACY_URL": "https://legal.beta.example/privacy",' +
        ' "NEXT_PUBLIC_BRAND_OPERATOR_NAME": "Beta GmbH" }, "windows": { "assets": "assets" } }'
        New-Item -ItemType Directory -Force -Path (Join-Path $dir 'assets') | Out-Null
        [System.IO.File]::WriteAllText($file, $json)
        $listing = ConvertTo-AstridStoreListing -Template $Template -BrandProfile (Get-AstridBrandProfile -RepoRoot $RepoRoot -Brand $file)
        Assert-True 'support' $listing.Contains('| Support contact | help@beta.example |')
        Assert-True 'privacy' $listing.Contains('| Privacy policy URL | https://legal.beta.example/privacy |')
        Assert-True 'copyright is the operator, not the publisher' $listing.Contains([char]0x00A9 + ' 2026 Beta GmbH')
        Assert-True 'no TO FILL hole left' (-not $listing.Contains('[TO FILL:'))
    }
    finally { Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue }
}

Invoke-Case 'every placeholder in the template has a value, and every value is used' {
    $known = @((Get-AstridStoreListingValues (Get-AstridBrandProfile -RepoRoot $RepoRoot)).Keys)
    # The template note talks ABOUT {PLACEHOLDER}s; it is not filled, so it is not scanned.
    $body = [regex]::Replace($Template, '(?ms)^<!-- template:.*?-->[ \t]*\r?\n', '')
    $used = @([regex]::Matches($body, '\{([A-Z_]+)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    Assert-True "the scan still finds placeholders (found $($used.Count))" ($used.Count -ge 8)
    $unknown = @($used | Where-Object { $known -notcontains $_ })
    Assert-Equal 'placeholders with no value' '' ($unknown -join ', ')
    $unused = @($known | Where-Object { $used -notcontains $_ })
    Assert-Equal 'values no placeholder uses' '' ($unused -join ', ')
}

Invoke-Case 'a broken template is named, not pasted' {
    $acmeProfile = Get-AstridBrandProfile -RepoRoot $RepoRoot -Brand acme
    Assert-Throws 'an unknown placeholder' 'no value for \{NOPE\}' {
        ConvertTo-AstridStoreListing -Template "x {NOPE}`n" -BrandProfile $acmeProfile
    }
    Assert-Throws 'a block missing its partner half' 'unpaired marker' {
        ConvertTo-AstridStoreListing -Template "<!-- brand: astrid -->`nours`n<!-- brand: end -->`n" -BrandProfile $acmeProfile
    }
}

Invoke-Case 'package.ps1 writes the listing beside the upload' {
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $RepoRoot 'scripts\package.ps1'), [ref]$null, [ref]$errors)
    $calls = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                $node.GetCommandName() -eq 'Write-AstridStoreListing'
            }, $true))
    Assert-Equal 'one call to Write-AstridStoreListing' 1 $calls.Count
    $standalone = Join-Path $RepoRoot 'scripts\store-listing.ps1'
    Assert-True 'scripts/store-listing.ps1 exists for writing it without a publish' (Test-Path $standalone)
}

Write-Host ''
if ($script:Checks -lt 20) {
    Write-Host "FAIL: only $($script:Checks) assertion(s) ran - this file is meant to make more" -ForegroundColor Red
    $script:Failures++
}
if ($script:Failures -gt 0) {
    Write-Host "RESULT: FAILED - $($script:Failures) failure(s) in $($script:Ran) case(s)" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: OK - $($script:Ran) case(s), $($script:Checks) assertion(s)" -ForegroundColor Green
exit 0
