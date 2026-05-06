# ROROROblox -- generate Store-bound logo assets.
#
# Direction C: isometric voxel stack -- three blocks (cyan-bright top, magenta middle, cyan
# bottom) on navy. Reads "stack of clients" without infringing on Roblox marks. Programmatic
# rendering -- no external image source, reproducible from this script alone.
#
# Tagline: "Multi-Roblox Instant Generator." (the brand-corporate "Imagine Something Else."
# tagline is dropped from product surfaces; "A 626 Labs product." attribution is kept.)
#
# Run from repo root:
#   powershell -ExecutionPolicy Bypass -File scripts/generate-store-assets.ps1
#
# Output: src/ROROROblox.App/Package/Logos/*.png at all required scale variants
# (scale-100, scale-125, scale-150, scale-200, scale-400) plus targetsize variants for
# Square44x44Logo (16, 24, 32, 48, 256 -- both plated and altform-unplated dark theme).
#
# File count: 45 PNGs. Manifest references bare names (Square150x150Logo.png etc); Windows
# auto-resolves the appropriate scale-N variant at runtime.

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$logosDir = Join-Path $RepoRoot 'src\ROROROblox.App\Package\Logos'
$fontDir = Join-Path $logosDir '.fonts'

if (-not (Test-Path $logosDir)) {
    New-Item -ItemType Directory -Path $logosDir -Force | Out-Null
}

# ----- Brand tokens -----------------------------------------------------------
$navy        = [System.Drawing.Color]::FromArgb(255, 25, 46, 68)    # #192E44 (logo bg)
$navyDeep    = [System.Drawing.Color]::FromArgb(255, 15, 31, 49)    # #0F1F31 (page bg)
$cyan        = [System.Drawing.Color]::FromArgb(255, 23, 212, 250)  # #17D4FA
$cyanBright  = [System.Drawing.Color]::FromArgb(255, 92, 230, 255)  # #5CE6FF
$cyanDim     = [System.Drawing.Color]::FromArgb(255, 15, 168, 201)  # #0FA8C9
$cyanShadow  = [System.Drawing.Color]::FromArgb(255, 10, 122, 146)  # #0A7A92
$magenta     = [System.Drawing.Color]::FromArgb(255, 242, 47, 137)  # #F22F89
$magentaDim  = [System.Drawing.Color]::FromArgb(255, 194, 31, 108)  # #C21F6C
$magentaShdw = [System.Drawing.Color]::FromArgb(255, 138, 21, 76)   # #8A154C
$mutedText   = [System.Drawing.Color]::FromArgb(255, 154, 168, 184) # #9AA8B8

# ----- Font selection ---------------------------------------------------------
# Prefer Space Grotesk (bundled .fonts/), fall back to Inter (system) -> Segoe UI Variable.
$pfc = New-Object System.Drawing.Text.PrivateFontCollection
$displayFamily = $null
$ttf = Get-ChildItem $fontDir -Filter '*.ttf' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($ttf) {
    $pfc.AddFontFile($ttf.FullName)
    $displayFamily = $pfc.Families[0]
    Write-Host "[font] $($displayFamily.Name) from $($ttf.Name)"
} else {
    $candidates = @('Inter 18pt 18pt', 'Inter', 'Segoe UI Variable Display', 'Segoe UI')
    foreach ($name in $candidates) {
        try {
            $f = New-Object System.Drawing.FontFamily($name)
            $displayFamily = $f
            Write-Host "[font] system fallback: $name"
            break
        } catch { continue }
    }
}

# ----- Iso block geometry -----------------------------------------------------
# Reference proportions from directions.html mock (256x256):
#   block width = 0.5 * canvas, top-face depth = 0.375 * blockW,
#   front-face height = 0.28125 * blockW. Three blocks stacked vertically; each
#   block translates blockH down from the previous.

function Draw-Block {
    param(
        [System.Drawing.Graphics]$gfx,
        [single]$x, [single]$y,
        [single]$w, [single]$d, [single]$h,
        [System.Drawing.Color]$top,
        [System.Drawing.Color]$left,
        [System.Drawing.Color]$right
    )

    $topPts = @(
        [System.Drawing.PointF]::new($x,           $y + $d / 2.0)
        [System.Drawing.PointF]::new($x + $w / 2,  $y)
        [System.Drawing.PointF]::new($x + $w,      $y + $d / 2.0)
        [System.Drawing.PointF]::new($x + $w / 2,  $y + $d)
    )
    $leftPts = @(
        [System.Drawing.PointF]::new($x,           $y + $d / 2.0)
        [System.Drawing.PointF]::new($x + $w / 2,  $y + $d)
        [System.Drawing.PointF]::new($x + $w / 2,  $y + $d + $h)
        [System.Drawing.PointF]::new($x,           $y + $d / 2.0 + $h)
    )
    $rightPts = @(
        [System.Drawing.PointF]::new($x + $w / 2,  $y + $d)
        [System.Drawing.PointF]::new($x + $w,      $y + $d / 2.0)
        [System.Drawing.PointF]::new($x + $w,      $y + $d / 2.0 + $h)
        [System.Drawing.PointF]::new($x + $w / 2,  $y + $d + $h)
    )

    $bTop = New-Object System.Drawing.SolidBrush($top)
    $bLeft = New-Object System.Drawing.SolidBrush($left)
    $bRight = New-Object System.Drawing.SolidBrush($right)
    try {
        $gfx.FillPolygon($bTop,   $topPts)
        $gfx.FillPolygon($bLeft,  $leftPts)
        $gfx.FillPolygon($bRight, $rightPts)
    } finally {
        $bTop.Dispose(); $bLeft.Dispose(); $bRight.Dispose()
    }
}

function Draw-VoxelStack {
    # Renders the three-block iso stack centered at (cx, cy) with bounding box width = $size.
    param(
        [System.Drawing.Graphics]$gfx,
        [single]$cx,
        [single]$cy,
        [single]$size
    )

    $bw = $size * 0.5
    $bd = $bw * 0.375
    $bh = $bw * 0.28125
    $stackH = $bd + 3 * $bh
    $blockX = $cx - $bw / 2
    $stackTop = $cy - $stackH / 2

    # Bottom block first (drawn first = painter's algorithm, lowest z behind).
    Draw-Block $gfx $blockX ($stackTop + 2 * $bh) $bw $bd $bh $cyan $cyanShadow $cyanDim
    # Middle (magenta).
    Draw-Block $gfx $blockX ($stackTop + $bh)     $bw $bd $bh $magenta $magentaShdw $magentaDim
    # Top (bright cyan -- the "active" block).
    Draw-Block $gfx $blockX $stackTop             $bw $bd $bh $cyanBright $cyanShadow $cyan
}

# ----- Asset canvas helpers ---------------------------------------------------
function New-Canvas {
    param([int]$width, [int]$height, [System.Drawing.Color]$bg = $navy)
    $bmp = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gfx.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gfx.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $brush = New-Object System.Drawing.SolidBrush($bg)
    try { $gfx.FillRectangle($brush, 0, 0, $width, $height) } finally { $brush.Dispose() }
    return @{ Bitmap = $bmp; Graphics = $gfx }
}

function Save-Png {
    param([System.Drawing.Bitmap]$bmp, [string]$path)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $info = Get-Item $path
    Write-Host "[icon] $($info.Name) ($($bmp.Width)x$($bmp.Height), $($info.Length) bytes)"
}

function Draw-Text {
    param(
        [System.Drawing.Graphics]$gfx,
        [string]$text, [single]$x, [single]$y, [single]$pointSize,
        [System.Drawing.Color]$color, [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Bold,
        [bool]$centered = $false
    )
    $font = New-Object System.Drawing.Font($displayFamily, $pointSize, $style, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush($color)
    try {
        if ($centered) {
            $sf = New-Object System.Drawing.StringFormat
            $sf.Alignment = [System.Drawing.StringAlignment]::Center
            try { $gfx.DrawString($text, $font, $brush, $x, $y, $sf) } finally { $sf.Dispose() }
        } else {
            $gfx.DrawString($text, $font, $brush, $x, $y)
        }
    } finally {
        $brush.Dispose(); $font.Dispose()
    }
}

# ----- Asset renderers --------------------------------------------------------
# Each renderer takes target dimensions and draws the appropriate composition.

function Render-Square {
    # Square iso-stack, no wordmark. Used for taskbar / Start-pin / 44, 71, 150, 310 sizes.
    param([int]$size)
    $c = New-Canvas $size $size
    Draw-VoxelStack $c.Graphics ($size / 2.0) ($size / 2.0) $size
    return $c
}

function Render-StoreLogo {
    # 50x50 base. Iso stack centered. No text.
    param([int]$size)
    return (Render-Square $size)
}

function Render-Wide {
    # Voxel stack on left ~32% of width, wordmark + tagline on right.
    param([int]$width, [int]$height)
    $c = New-Canvas $width $height
    $stackSize = [single]($height * 1.05)
    $stackCx = $width * 0.18
    $stackCy = $height * 0.50
    Draw-VoxelStack $c.Graphics $stackCx $stackCy $stackSize

    $textX = $width * 0.40
    $wordmarkSize  = [single]($height * 0.18)
    $taglineSize   = [single]($height * 0.075)
    $attribSize    = [single]($height * 0.06)
    $wordmarkY = $height * 0.32 - $wordmarkSize / 2
    $taglineY  = $height * 0.55
    $attribY   = $height * 0.74
    Draw-Text $c.Graphics 'RORORO' $textX $wordmarkY $wordmarkSize ([System.Drawing.Color]::White) ([System.Drawing.FontStyle]::Bold) $false
    Draw-Text $c.Graphics 'Multi-launcher for Windows.' $textX $taglineY $taglineSize $cyan ([System.Drawing.FontStyle]::Regular) $false
    Draw-Text $c.Graphics 'A 626 Labs product' $textX $attribY $attribSize $mutedText ([System.Drawing.FontStyle]::Regular) $false
    return $c
}

function Render-BoxArt {
    # 1:1 Box art for Partner Center 'Store logos' (Xbox display surface). Voxel stack
    # centered in the upper ~60% of the canvas, wordmark + tagline + attribution below.
    param([int]$size)
    $c = New-Canvas $size $size $navy
    $stackSize = [single]($size * 0.55)
    $stackCx = $size * 0.50
    $stackCy = $size * 0.40
    Draw-VoxelStack $c.Graphics $stackCx $stackCy $stackSize

    $cx = $size * 0.50
    $wordmarkSize = [single]($size * 0.090)
    $taglineSize  = [single]($size * 0.032)
    $attribSize   = [single]($size * 0.024)
    Draw-Text $c.Graphics 'RORORO' $cx ($size * 0.70) $wordmarkSize ([System.Drawing.Color]::White) ([System.Drawing.FontStyle]::Bold) $true
    Draw-Text $c.Graphics 'Multi-launcher for Windows.' $cx ($size * 0.81) $taglineSize $cyan ([System.Drawing.FontStyle]::Regular) $true
    Draw-Text $c.Graphics 'A 626 Labs product' $cx ($size * 0.88) $attribSize $mutedText ([System.Drawing.FontStyle]::Regular) $true
    return $c
}

function Render-Poster {
    # 9:16 Poster art for Partner Center 'Store logos' (Xbox display surface). Vertical
    # composition: voxel stack in upper third, wordmark + tagline + attribution stacked below.
    param([int]$width, [int]$height)
    $c = New-Canvas $width $height $navy
    $stackSize = [single]($width * 0.55)
    $stackCx = $width * 0.50
    $stackCy = $height * 0.32
    Draw-VoxelStack $c.Graphics $stackCx $stackCy $stackSize

    $cx = $width * 0.50
    $wordmarkSize = [single]($width * 0.10)
    $taglineSize  = [single]($width * 0.035)
    $attribSize   = [single]($width * 0.025)
    Draw-Text $c.Graphics 'RORORO' $cx ($height * 0.62) $wordmarkSize ([System.Drawing.Color]::White) ([System.Drawing.FontStyle]::Bold) $true
    Draw-Text $c.Graphics 'Multi-launcher for Windows.' $cx ($height * 0.74) $taglineSize $cyan ([System.Drawing.FontStyle]::Regular) $true
    Draw-Text $c.Graphics 'A 626 Labs product' $cx ($height * 0.81) $attribSize $mutedText ([System.Drawing.FontStyle]::Regular) $true
    return $c
}

function Render-Hero {
    # 16:9 hero image for Partner Center listing top banner. Voxel stack on left ~20% of
    # width, wordmark + tagline + attribution stacked on the right ~60%. Same DNA as the
    # wide tile but tuned for 1920x1080 / 3840x2160 viewports (more vertical breathing
    # room around the stack, larger text, attribution gets its own line).
    param([int]$width, [int]$height)
    $c = New-Canvas $width $height $navy
    $stackSize = [single]($height * 0.62)
    $stackCx = $width * 0.20
    $stackCy = $height * 0.50
    Draw-VoxelStack $c.Graphics $stackCx $stackCy $stackSize

    $textX = $width * 0.40
    $wordmarkSize = [single]($height * 0.13)
    $taglineSize  = [single]($height * 0.045)
    $attribSize   = [single]($height * 0.032)
    Draw-Text $c.Graphics 'RORORO' $textX ($height * 0.34) $wordmarkSize ([System.Drawing.Color]::White) ([System.Drawing.FontStyle]::Bold) $false
    Draw-Text $c.Graphics 'Multi-launcher for Windows.' $textX ($height * 0.56) $taglineSize $cyan ([System.Drawing.FontStyle]::Regular) $false
    Draw-Text $c.Graphics 'A 626 Labs product' $textX ($height * 0.66) $attribSize $mutedText ([System.Drawing.FontStyle]::Regular) $false
    return $c
}

function Render-Splash {
    # 620x300 base. Voxel stack on left (taller), wordmark + tagline + attribution on right.
    param([int]$width, [int]$height)
    $c = New-Canvas $width $height $navy
    $stackSize = [single]($height * 0.78)
    $stackCx = $width * 0.22
    $stackCy = $height * 0.50
    Draw-VoxelStack $c.Graphics $stackCx $stackCy $stackSize

    $textX = $width * 0.42
    $wordmarkSize  = [single]($height * 0.18)
    $taglineSize   = [single]($height * 0.065)
    $attribSize    = [single]($height * 0.045)
    $wordmarkY = $height * 0.32
    $taglineY  = $height * 0.56
    $attribY   = $height * 0.70
    Draw-Text $c.Graphics 'RORORO' $textX $wordmarkY $wordmarkSize ([System.Drawing.Color]::White) ([System.Drawing.FontStyle]::Bold) $false
    Draw-Text $c.Graphics 'Multi-launcher for Windows.' $textX $taglineY $taglineSize $cyan ([System.Drawing.FontStyle]::Regular) $false
    Draw-Text $c.Graphics 'A 626 Labs product' $textX $attribY $attribSize $mutedText ([System.Drawing.FontStyle]::Regular) $false
    return $c
}

# ----- Asset matrix -----------------------------------------------------------
# Per Microsoft Learn (app-icon-construction): each MSIX asset has scale-100, 125, 150, 200, 400
# variants. Square44x44Logo additionally has targetsize-{16,24,32,48,256} both plated and
# altform-unplated (dark theme). The unplated variants are REQUIRED to avoid the system platelet.

$scales = @{
    'scale-100' = 1.00
    'scale-125' = 1.25
    'scale-150' = 1.50
    'scale-200' = 2.00
    'scale-400' = 4.00
}

# Clear previous output to avoid stale variant files.
Get-ChildItem $logosDir -Filter '*.png' -ErrorAction SilentlyContinue | Remove-Item -Force

# Track the bare-name fallback files we need to write alongside scale variants.
# build-msix.ps1's logo gate looks for these bare names; Windows resolution treats them as
# scale-100 default fallback when no scale-N variant matches the current display.
function Write-BareName {
    param([System.Drawing.Bitmap]$bmp, [string]$bareName)
    $bmp.Save((Join-Path $logosDir $bareName), [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "[icon] $bareName ($($bmp.Width)x$($bmp.Height), bare-name fallback)"
}

# --- Square44x44Logo (taskbar / alt-tab / list views) ---
foreach ($s in $scales.GetEnumerator()) {
    $size = [int][Math]::Round(44 * $s.Value)
    $c = Render-Square $size
    Save-Png $c.Bitmap (Join-Path $logosDir "Square44x44Logo.$($s.Key).png")
    if ($s.Key -eq 'scale-100') { Write-BareName $c.Bitmap 'Square44x44Logo.png' }
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}
# Targetsize variants (Win10/11 system list rendering).
$targetsizes = @(16, 24, 32, 48, 256)
foreach ($t in $targetsizes) {
    $c = Render-Square $t
    Save-Png $c.Bitmap (Join-Path $logosDir "Square44x44Logo.targetsize-$t.png")
    Save-Png $c.Bitmap (Join-Path $logosDir "Square44x44Logo.targetsize-${t}_altform-unplated.png")
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# --- Square71x71Logo (small tile) ---
foreach ($s in $scales.GetEnumerator()) {
    $size = [int][Math]::Round(71 * $s.Value)
    $c = Render-Square $size
    Save-Png $c.Bitmap (Join-Path $logosDir "Square71x71Logo.$($s.Key).png")
    if ($s.Key -eq 'scale-100') { Write-BareName $c.Bitmap 'Square71x71Logo.png' }
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# --- Square150x150Logo (medium tile -- REQUIRED for Store) ---
foreach ($s in $scales.GetEnumerator()) {
    $size = [int][Math]::Round(150 * $s.Value)
    $c = Render-Square $size
    Save-Png $c.Bitmap (Join-Path $logosDir "Square150x150Logo.$($s.Key).png")
    if ($s.Key -eq 'scale-100') { Write-BareName $c.Bitmap 'Square150x150Logo.png' }
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# --- Square310x310Logo (large tile) ---
foreach ($s in $scales.GetEnumerator()) {
    $size = [int][Math]::Round(310 * $s.Value)
    $c = Render-Square $size
    Save-Png $c.Bitmap (Join-Path $logosDir "Square310x310Logo.$($s.Key).png")
    if ($s.Key -eq 'scale-100') { Write-BareName $c.Bitmap 'Square310x310Logo.png' }
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# --- Wide310x150Logo (wide tile -- with wordmark + tagline) ---
foreach ($s in $scales.GetEnumerator()) {
    $w = [int][Math]::Round(310 * $s.Value)
    $h = [int][Math]::Round(150 * $s.Value)
    $c = Render-Wide $w $h
    Save-Png $c.Bitmap (Join-Path $logosDir "Wide310x150Logo.$($s.Key).png")
    if ($s.Key -eq 'scale-100') { Write-BareName $c.Bitmap 'Wide310x150Logo.png' }
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# --- StoreLogo (50x50 base -- Microsoft Store listing) ---
foreach ($s in $scales.GetEnumerator()) {
    $size = [int][Math]::Round(50 * $s.Value)
    $c = Render-StoreLogo $size
    Save-Png $c.Bitmap (Join-Path $logosDir "StoreLogo.$($s.Key).png")
    if ($s.Key -eq 'scale-100') { Write-BareName $c.Bitmap 'StoreLogo.png' }
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# --- SplashScreen (620x300 base) ---
foreach ($s in $scales.GetEnumerator()) {
    $w = [int][Math]::Round(620 * $s.Value)
    $h = [int][Math]::Round(300 * $s.Value)
    $c = Render-Splash $w $h
    Save-Png $c.Bitmap (Join-Path $logosDir "SplashScreen.$($s.Key).png")
    if ($s.Key -eq 'scale-100') { Write-BareName $c.Bitmap 'SplashScreen.png' }
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# ----- Partner Center listing graphics ----------------------------------------
# These live OUTSIDE the MSIX -- they're uploaded directly to Partner Center as
# 'Store logos' (Xbox box art + 9:16 poster) and 'Store display images' (Win10/11
# 1:1 tile icons at 300/150/71). Output to docs/store/graphics/ so they're committed
# alongside the rest of the Store-prep notes and discoverable when filling in the
# Partner Center listing.

$listingDir = Join-Path $RepoRoot 'docs\store\graphics'
if (-not (Test-Path $listingDir)) {
    New-Item -ItemType Directory -Path $listingDir -Force | Out-Null
}

# Store display images (Win10/11 customer-facing listing card graphics)
foreach ($size in @(71, 150, 300)) {
    $c = Render-Square $size
    Save-Png $c.Bitmap (Join-Path $listingDir "store-display-${size}x${size}.png")
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# Store logos (Xbox display surface) -- 1:1 Box art + 9:16 Poster art at both scale variants
$boxArtSizes = @(1080, 2160)
foreach ($size in $boxArtSizes) {
    $c = Render-BoxArt $size
    Save-Png $c.Bitmap (Join-Path $listingDir "store-boxart-${size}x${size}.png")
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

$posterSizes = @(@(720, 1080), @(1440, 2160))
foreach ($pair in $posterSizes) {
    $w = $pair[0]
    $h = $pair[1]
    $c = Render-Poster $w $h
    Save-Png $c.Bitmap (Join-Path $listingDir "store-poster-${w}x${h}.png")
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# 16:9 Hero image (Partner Center listing top banner). 1920x1080 base + 3840x2160 retina.
$heroSizes = @(@(1920, 1080), @(3840, 2160))
foreach ($pair in $heroSizes) {
    $w = $pair[0]
    $h = $pair[1]
    $c = Render-Hero $w $h
    Save-Png $c.Bitmap (Join-Path $listingDir "store-hero-${w}x${h}.png")
    $c.Graphics.Dispose(); $c.Bitmap.Dispose()
}

# Cleanup font collection.
$pfc.Dispose()

Write-Host ''
Write-Host '[done] Store-bound assets generated. Run scripts/build-msix.ps1 -Verify to confirm packaging.' -ForegroundColor Green
$count = (Get-ChildItem $logosDir -Filter '*.png').Count
Write-Host "[count] $count PNG files written to $logosDir" -ForegroundColor Green
$listingCount = (Get-ChildItem $listingDir -Filter '*.png').Count
Write-Host "[count] $listingCount Partner Center listing graphics written to $listingDir" -ForegroundColor Green
