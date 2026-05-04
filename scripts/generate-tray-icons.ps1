# ROROROblox -- generate branded tray-icon ICOs.
# Output: src/ROROROblox.App/Tray/Resources/tray-{on,off,error}.ico (32x32 each).
#
# Run from repo root:
#   powershell -ExecutionPolicy Bypass -File scripts/generate-tray-icons.ps1
#
# Inputs: 626Labs lockup at $env:USERPROFILE/.claude/skills/626labs-design/assets/626Labs-logo.png
#
# Design: navy field, 626Labs hex+brain mark in the center, state-colored ring around the edge.
# - ON    cyan #17D4FA ring (mutex held -> multi-instance active)
# - OFF   slate #5A6982 ring (mutex released -> single-instance default)
# - ERROR magenta #F22F89 ring (mutex lost -> see spec section 5.1 watchdog)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resourcesDir = Join-Path $repoRoot 'src\ROROROblox.App\Tray\Resources'

# Source resolution -- prefer the labs-hub transparent icon, fall back to design-skill lockup.
$sourceCandidates = @(
    'E:\626Labs-Workspace\repos\626Labs-LLC.github.io\assets\brand\icon-transparent-1024.png',
    (Join-Path $env:USERPROFILE 'Projects\626Labs-LLC.github.io\assets\brand\icon-transparent-1024.png'),
    (Join-Path $env:USERPROFILE '.claude\skills\626labs-design\assets\626Labs-logo.png')
)
$srcLogo = $sourceCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $srcLogo) {
    Write-Host '[fatal] No source logo found.' -ForegroundColor Red
    exit 1
}
$useTransparentSource = $srcLogo -like '*icon-transparent-*.png'
Write-Host "[source] $srcLogo (transparent: $useTransparentSource)" -ForegroundColor Cyan

# Brand tokens.
$navy = [System.Drawing.Color]::FromArgb(255, 15, 31, 49)         # #0F1F31
$cyan = [System.Drawing.Color]::FromArgb(255, 23, 212, 250)       # #17D4FA
$slate = [System.Drawing.Color]::FromArgb(255, 90, 105, 130)      # #5A6982
$magenta = [System.Drawing.Color]::FromArgb(255, 242, 47, 137)    # #F22F89

$source = [System.Drawing.Image]::FromFile($srcLogo)
if ($useTransparentSource) {
    $markRect = New-Object System.Drawing.Rectangle(0, 0, $source.Width, $source.Height)
} else {
    $markRect = New-Object System.Drawing.Rectangle(70, 20, 450, 410)
}

$states = @(
    @{ Name = 'tray-on.ico';    Ring = $cyan },
    @{ Name = 'tray-off.ico';   Ring = $slate },
    @{ Name = 'tray-error.ico'; Ring = $magenta }
)

foreach ($state in $states) {
    $bmp = New-Object System.Drawing.Bitmap(32, 32, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $gfx.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        # Filled navy disk fills the icon.
        $navyBrush = New-Object System.Drawing.SolidBrush($navy)
        $gfx.FillEllipse($navyBrush, 1, 1, 30, 30)
        $navyBrush.Dispose()

        # Mark drawn inside, slightly inset so the ring shows around it.
        $markDest = New-Object System.Drawing.Rectangle(4, 4, 24, 24)
        $gfx.DrawImage($source, $markDest, $markRect, [System.Drawing.GraphicsUnit]::Pixel)

        # State-colored 2px ring around the outside.
        $ringPen = New-Object System.Drawing.Pen($state.Ring, 2)
        $gfx.DrawEllipse($ringPen, 1, 1, 30, 30)
        $ringPen.Dispose()
    }
    finally {
        $gfx.Dispose()
    }

    $iconHandle = $bmp.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
    $outPath = Join-Path $resourcesDir $state.Name
    $fs = [System.IO.File]::Create($outPath)
    try { $icon.Save($fs) } finally { $fs.Close() }
    $info = Get-Item $outPath
    Write-Host "[icon] wrote $($info.Name) ($($info.Length) bytes)"
    $bmp.Dispose()
}

$source.Dispose()

# Remove the old placeholder ICOs so we don't ship two sets.
$placeholders = @(
    'tray-on.placeholder.ico',
    'tray-off.placeholder.ico',
    'tray-error.placeholder.ico'
)
foreach ($p in $placeholders) {
    $path = Join-Path $resourcesDir $p
    if (Test-Path $path) {
        Remove-Item $path -Force
        Write-Host "[clean] removed $p"
    }
}

Write-Host ''
Write-Host '[done] Tray icons generated. Update TrayService.cs to load tray-{on,off,error}.ico.' -ForegroundColor Green
