# ROROROblox -- generate Store-bound logo assets from the 626Labs lockup + ROROROblox wordmark.
# Output: src/ROROROblox.App/Package/Logos/*.png at each MSIX-required size.
#
# Run from repo root:
#   powershell -ExecutionPolicy Bypass -File scripts/generate-store-assets.ps1
#
# Inputs:
#   - 626Labs lockup at $env:USERPROFILE\.claude\skills\626labs-design\assets\626Labs-logo.png
#     (the standard Claude Code skill install path; mark portion = top 70% of the 589x598 PNG;
#      we crop the wordmark off because we substitute "ROROROblox" instead.)
#   - Space Grotesk Bold TTF at src/ROROROblox.App/Package/Logos/.fonts/ for the wordmark.
#
# This script is reproducible -- re-run after design-skill iterations on the source lockup.

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$logosDir = Join-Path $RepoRoot 'src\ROROROblox.App\Package\Logos'
$fontDir = Join-Path $logosDir '.fonts'
$srcLogo = Join-Path $env:USERPROFILE '.claude\skills\626labs-design\assets\626Labs-logo.png'

if (-not (Test-Path $srcLogo)) {
    Write-Host "[fatal] 626Labs lockup not found at $srcLogo" -ForegroundColor Red
    Write-Host "[fatal] Install the 626labs-design Claude Code skill first." -ForegroundColor Red
    exit 1
}

# Brand tokens.
$navy = [System.Drawing.Color]::FromArgb(255, 15, 31, 49)         # #0F1F31
$cyan = [System.Drawing.Color]::FromArgb(255, 23, 212, 250)       # #17D4FA
$magenta = [System.Drawing.Color]::FromArgb(255, 242, 47, 137)    # #F22F89
$mutedText = [System.Drawing.Color]::FromArgb(255, 154, 168, 184) # #9AA8B8

# Load Space Grotesk Bold; fall back to Segoe UI if missing.
$pfc = New-Object System.Drawing.Text.PrivateFontCollection
$ttf = Get-ChildItem $fontDir -Filter '*.ttf' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($ttf) {
    $pfc.AddFontFile($ttf.FullName)
    $displayFamily = $pfc.Families[0]
    Write-Host "[font] using $($displayFamily.Name) from $($ttf.Name)"
} else {
    $displayFamily = New-Object System.Drawing.FontFamily('Segoe UI')
    Write-Host '[font] Space Grotesk not found, falling back to Segoe UI'
}

# Source-mark crop. The lockup is 589x598 with the hex+brain+swoosh artwork sitting ~y=20-430,
# x=70-520 (eyeballed). Crop tighter than the full top half to minimize the source-navy frame
# bleeding into our canvas at smaller sizes.
$source = [System.Drawing.Image]::FromFile($srcLogo)
$markRect = New-Object System.Drawing.Rectangle(70, 20, 450, 410)

function New-AssetCanvas([int]$width, [int]$height) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gfx.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gfx.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $brush = New-Object System.Drawing.SolidBrush($navy)
    $gfx.FillRectangle($brush, 0, 0, $width, $height)
    $brush.Dispose()
    return @{ Bitmap = $bmp; Graphics = $gfx }
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$path) {
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $info = Get-Item $path
    Write-Host "[icon] wrote $($info.Name) ($($info.Length) bytes, $($bmp.Width)x$($bmp.Height))"
}

function Add-Mark([System.Drawing.Graphics]$gfx, [int]$x, [int]$y, [int]$size) {
    $dst = New-Object System.Drawing.Rectangle($x, $y, $size, $size)
    $gfx.DrawImage($source, $dst, $markRect, [System.Drawing.GraphicsUnit]::Pixel)
}

function Add-Wordmark([System.Drawing.Graphics]$gfx, [string]$text, [int]$x, [int]$y, [single]$pointSize, [bool]$centered = $false) {
    $font = New-Object System.Drawing.Font($displayFamily, $pointSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    if ($centered) {
        $sf = New-Object System.Drawing.StringFormat
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $gfx.DrawString($text, $font, $brush, $x, $y, $sf)
        $sf.Dispose()
    } else {
        $gfx.DrawString($text, $font, $brush, $x, $y)
    }
    $brush.Dispose()
    $font.Dispose()
}

function Add-AccentText([System.Drawing.Graphics]$gfx, [string]$text, [int]$x, [int]$y, [single]$pointSize, [System.Drawing.Color]$color, [bool]$centered = $false) {
    $font = New-Object System.Drawing.Font($displayFamily, $pointSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush($color)
    if ($centered) {
        $sf = New-Object System.Drawing.StringFormat
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $gfx.DrawString($text, $font, $brush, $x, $y, $sf)
        $sf.Dispose()
    } else {
        $gfx.DrawString($text, $font, $brush, $x, $y)
    }
    $brush.Dispose()
    $font.Dispose()
}

# ----- Generate each required asset. Sizes per src/ROROROblox.App/Package/Logos/README.md. -----

# Square150x150Logo: medium tile + Start menu.
# Mark on top 60% + "ROROROblox" wordmark below.
$c = New-AssetCanvas 150 150
$markSize = 90
Add-Mark $c.Graphics (([int]((150 - $markSize) / 2))) 8 $markSize
Add-Wordmark $c.Graphics 'ROROROblox' 75 105 17 $true
Save-Png $c.Bitmap (Join-Path $logosDir 'Square150x150Logo.png')
$c.Graphics.Dispose(); $c.Bitmap.Dispose()

# Square44x44Logo: taskbar + alt+tab. Mark only -- no room for text.
$c = New-AssetCanvas 44 44
Add-Mark $c.Graphics 2 2 40
Save-Png $c.Bitmap (Join-Path $logosDir 'Square44x44Logo.png')
$c.Graphics.Dispose(); $c.Bitmap.Dispose()

# Square71x71Logo: small tile. Mark only -- too small for text.
$c = New-AssetCanvas 71 71
Add-Mark $c.Graphics 4 4 63
Save-Png $c.Bitmap (Join-Path $logosDir 'Square71x71Logo.png')
$c.Graphics.Dispose(); $c.Bitmap.Dispose()

# Square310x310Logo: large tile. Mark on top 60% + wordmark + tagline.
$c = New-AssetCanvas 310 310
$markSize = 180
Add-Mark $c.Graphics (([int]((310 - $markSize) / 2))) 24 $markSize
Add-Wordmark $c.Graphics 'ROROROblox' 155 215 36 $true
Add-AccentText $c.Graphics 'Imagine Something Else.' 155 260 14 $cyan $true
Save-Png $c.Bitmap (Join-Path $logosDir 'Square310x310Logo.png')
$c.Graphics.Dispose(); $c.Bitmap.Dispose()

# Wide310x150Logo: wide tile. Mark on the left, wordmark + tagline on the right.
$c = New-AssetCanvas 310 150
Add-Mark $c.Graphics 12 8 134
Add-Wordmark $c.Graphics 'ROROROblox' 160 50 24 $false
Add-AccentText $c.Graphics 'Imagine Something Else.' 160 88 12 $cyan $false
Save-Png $c.Bitmap (Join-Path $logosDir 'Wide310x150Logo.png')
$c.Graphics.Dispose(); $c.Bitmap.Dispose()

# StoreLogo.png: 50x50 -- listing logo on the Store. Mark only.
$c = New-AssetCanvas 50 50
Add-Mark $c.Graphics 2 2 46
Save-Png $c.Bitmap (Join-Path $logosDir 'StoreLogo.png')
$c.Graphics.Dispose(); $c.Bitmap.Dispose()

# SplashScreen.png: 620x300 -- first-paint splash. Mark on the left, big wordmark + tagline on the right.
$c = New-AssetCanvas 620 300
Add-Mark $c.Graphics 24 32 236
Add-Wordmark $c.Graphics 'ROROROblox' 280 110 46 $false
Add-AccentText $c.Graphics 'Imagine Something Else.' 280 175 20 $cyan $false
Add-AccentText $c.Graphics 'A 626 Labs product.' 280 210 13 $mutedText $false
Save-Png $c.Bitmap (Join-Path $logosDir 'SplashScreen.png')
$c.Graphics.Dispose(); $c.Bitmap.Dispose()

# Cleanup.
$source.Dispose()
$pfc.Dispose()

Write-Host ''
Write-Host '[done] All Store-bound assets generated. Run scripts/build-msix.ps1 -Verify to confirm the gate passes.' -ForegroundColor Green
