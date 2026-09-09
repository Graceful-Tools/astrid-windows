<#
.SYNOPSIS
    Generate every icon the app and its package need, from the one master image.

.DESCRIPTION
    The package shipped three flat-colour placeholders of 111 to 301 bytes, and the executable had
    no icon at all — it wore the generic .NET one on the taskbar and in Explorer. Board task
    70ffe6c3: use the icon the Mac and the web already use.

    The master is astrid-web's `public/icons/icon-4096x4096.png`, which is the same artwork every
    other client draws. It is resampled here rather than copied at each size so the set cannot
    drift: regenerating is one command, and a new master reaches every size at once.

    Two kinds of output, for two different consumers:

      packaging/Assets/    the MSIX tiles the Store and the shell read, including the scale- and
                           targetsize- variants Windows picks between by DPI and by surface
      app/Astrid.App/Assets/Astrid.ico
                           the executable's own icon, which is what the taskbar, Alt-Tab and
                           Explorer use for the UNPACKAGED build

    The .ico is written by hand because System.Drawing can only save a single image as an icon,
    which produces a 32x32 that Windows then scales badly to 256. The format is a small header, one
    directory entry per size, and the PNG bytes — PNG-compressed entries are what every Windows
    since Vista reads, and they keep a 256px icon from being 256 KB of bitmap.

.EXAMPLE
    powershell -File scripts/make-icons.ps1
#>
[CmdletBinding()]
param(
    # Where the shared artwork lives. A sibling checkout by default, because that is how the three
    # repositories sit on a machine that works on more than one of them.
    [string]$Master
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Folded to an absolute path before it is used: Test-Path refuses a string carrying more ".."
# segments than it can resolve against the drive root, and the default reaches up two levels into
# the sibling astrid-web checkout.
if (-not $Master) {
    $Master = [System.IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot '..\..\astrid-web\public\icons\icon-4096x4096.png'))
}

if (-not (Test-Path $Master)) {
    Write-Error "Master icon not found at $Master — pass -Master with the path to icon-4096x4096.png"
}

$repo = Resolve-Path "$PSScriptRoot\.."
$packageAssets = Join-Path $repo 'packaging\Assets'
$appAssets = Join-Path $repo 'app\Astrid.App\Assets'
New-Item -ItemType Directory -Force $packageAssets | Out-Null
New-Item -ItemType Directory -Force $appAssets | Out-Null

$source = [System.Drawing.Image]::FromFile((Resolve-Path $Master))
Write-Host "master: $($source.Width)x$($source.Height)" -ForegroundColor Cyan

<#
    Resample the master into a square of the given side.

    HighQualityBicubic with a half-pixel offset: the default wrap mode samples past the edge and
    leaves a faint border on a transparent PNG, which is visible on a 44px tile.
#>
function Resize-Square {
    param([int]$Side)

    $bitmap = New-Object System.Drawing.Bitmap($Side, $Side)
    $bitmap.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $attributes = New-Object System.Drawing.Imaging.ImageAttributes
        $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $rect = New-Object System.Drawing.Rectangle(0, 0, $Side, $Side)
        $graphics.DrawImage($source, $rect, 0, 0, $source.Width, $source.Height,
            [System.Drawing.GraphicsUnit]::Pixel, $attributes)
    }
    finally { $graphics.Dispose() }
    return $bitmap
}

<#
    A wide tile: the square logo centred on a transparent canvas.

    Not a stretch. The artwork is square, and a 310x150 stretch of it is the app's icon looking
    like it was sat on.
#>
function New-WideTile {
    param([int]$Width, [int]$Height)

    $side = [int]($Height * 0.75)
    $logo = Resize-Square -Side $side
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $bitmap.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($logo, [int](($Width - $side) / 2), [int](($Height - $side) / 2), $side, $side)
    }
    finally { $graphics.Dispose(); $logo.Dispose() }
    return $bitmap
}

function Save-Png {
    param([System.Drawing.Bitmap]$Bitmap, [string]$Path)
    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $Bitmap.Dispose()
}

# ── The package tiles ───────────────────────────────────────────────────────────────────────
#
# Windows picks between these by DPI (scale-) and by surface (targetsize-). The unplated variants
# are the ones the taskbar and Start use, where the tile's background must not show.
$squares = @{
    'Square44x44Logo.png'                            = 44
    'Square44x44Logo.scale-200.png'                  = 88
    'Square44x44Logo.targetsize-16.png'              = 16
    'Square44x44Logo.targetsize-24.png'              = 24
    'Square44x44Logo.targetsize-32.png'              = 32
    'Square44x44Logo.targetsize-48.png'              = 48
    'Square44x44Logo.targetsize-256.png'             = 256
    'Square44x44Logo.altform-unplated_targetsize-16.png'  = 16
    'Square44x44Logo.altform-unplated_targetsize-24.png'  = 24
    'Square44x44Logo.altform-unplated_targetsize-32.png'  = 32
    'Square44x44Logo.altform-unplated_targetsize-48.png'  = 48
    'Square44x44Logo.altform-unplated_targetsize-256.png' = 256
    'Square71x71Logo.png'                            = 71
    'Square71x71Logo.scale-200.png'                  = 142
    'Square150x150Logo.png'                          = 150
    'Square150x150Logo.scale-200.png'                = 300
    'Square310x310Logo.png'                          = 310
    'StoreLogo.png'                                  = 50
    'StoreLogo.scale-200.png'                        = 100
}

foreach ($name in $squares.Keys | Sort-Object) {
    Save-Png -Bitmap (Resize-Square -Side $squares[$name]) -Path (Join-Path $packageAssets $name)
}
Save-Png -Bitmap (New-WideTile -Width 310 -Height 150) -Path (Join-Path $packageAssets 'Wide310x150Logo.png')
Save-Png -Bitmap (New-WideTile -Width 620 -Height 300) -Path (Join-Path $packageAssets 'Wide310x150Logo.scale-200.png')
Save-Png -Bitmap (New-WideTile -Width 620 -Height 300) -Path (Join-Path $packageAssets 'SplashScreen.png')
Write-Host "wrote $($squares.Count + 3) package assets to packaging\Assets" -ForegroundColor Green

# ── The executable's icon ───────────────────────────────────────────────────────────────────
$icoSizes = @(16, 24, 32, 48, 64, 128, 256)
$pngBytes = foreach ($side in $icoSizes) {
    $bitmap = Resize-Square -Side $side
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

$icoPath = Join-Path $appAssets 'Astrid.ico'
$file = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0)                     # reserved
    $writer.Write([uint16]1)                     # type: icon
    $writer.Write([uint16]$icoSizes.Count)

    # Directory entries come first, so every offset is known before any pixels are written.
    $offset = 6 + (16 * $icoSizes.Count)
    for ($i = 0; $i -lt $icoSizes.Count; $i++) {
        $side = $icoSizes[$i]
        # 256 is written as 0 — the field is one byte, and 256 does not fit in it.
        $writer.Write([byte]($(if ($side -ge 256) { 0 } else { $side })))
        $writer.Write([byte]($(if ($side -ge 256) { 0 } else { $side })))
        $writer.Write([byte]0)                   # palette count: none, it is truecolour
        $writer.Write([byte]0)                   # reserved
        $writer.Write([uint16]1)                 # colour planes
        $writer.Write([uint16]32)                # bits per pixel
        $writer.Write([uint32]$pngBytes[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $pngBytes[$i].Length
    }
    foreach ($bytes in $pngBytes) { $writer.Write($bytes) }
}
finally { $writer.Dispose(); $file.Dispose() }

$source.Dispose()
Write-Host "wrote $icoPath ($($icoSizes -join ', ') px)" -ForegroundColor Green
