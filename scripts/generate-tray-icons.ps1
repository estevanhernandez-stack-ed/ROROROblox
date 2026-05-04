# ROROROblox -- generate branded tray-icon ICOs.
# Output: src/ROROROblox.App/Tray/Resources/tray-{on,off,error}.ico
#         (multi-size: 16, 24, 32, 48, 64, 256 embedded as PNG-in-ICO)
#
# Run from repo root:
#   powershell -ExecutionPolicy Bypass -File scripts/generate-tray-icons.ps1
#
# Design (Direction C iso voxel stack, simplified for tray legibility):
#   - Navy disk fills the icon
#   - Three iso voxel blocks centered (cyan-bright top, magenta middle, cyan bottom)
#   - State-colored 2px ring around the disk:
#       ON      cyan       (mutex held -- multi-instance active)
#       OFF     slate      (mutex released -- single-instance default)
#       ERROR   magenta    (mutex lost -- watchdog tripped, see spec section 5.1)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resourcesDir = Join-Path $repoRoot 'src\ROROROblox.App\Tray\Resources'

if (-not (Test-Path $resourcesDir)) {
    New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null
}

# ----- Brand tokens -----------------------------------------------------------
$navy        = [System.Drawing.Color]::FromArgb(255, 25, 46, 68)
$navyDeep    = [System.Drawing.Color]::FromArgb(255, 15, 31, 49)
$cyan        = [System.Drawing.Color]::FromArgb(255, 23, 212, 250)
$cyanBright  = [System.Drawing.Color]::FromArgb(255, 92, 230, 255)
$cyanDim     = [System.Drawing.Color]::FromArgb(255, 15, 168, 201)
$cyanShadow  = [System.Drawing.Color]::FromArgb(255, 10, 122, 146)
$magenta     = [System.Drawing.Color]::FromArgb(255, 242, 47, 137)
$magentaDim  = [System.Drawing.Color]::FromArgb(255, 194, 31, 108)
$magentaShdw = [System.Drawing.Color]::FromArgb(255, 138, 21, 76)
$slate       = [System.Drawing.Color]::FromArgb(255, 90, 105, 130)

# ----- Iso voxel rendering (matches generate-store-assets.ps1 geometry) -------
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
    param(
        [System.Drawing.Graphics]$gfx,
        [single]$cx,
        [single]$cy,
        [single]$size
    )
    $bw = $size * 0.55           # slightly wider for tray legibility
    $bd = $bw * 0.375
    $bh = $bw * 0.28125
    $stackH = $bd + 3 * $bh
    $blockX = $cx - $bw / 2
    $stackTop = $cy - $stackH / 2

    Draw-Block $gfx $blockX ($stackTop + 2 * $bh) $bw $bd $bh $cyan $cyanShadow $cyanDim
    Draw-Block $gfx $blockX ($stackTop + $bh)     $bw $bd $bh $magenta $magentaShdw $magentaDim
    Draw-Block $gfx $blockX $stackTop             $bw $bd $bh $cyanBright $cyanShadow $cyan
}

# ----- ICO writer -------------------------------------------------------------
# Compose multi-size ICO using PNG-in-ICO format (Vista+ supported).
function Save-Ico {
    param(
        [string]$path,
        [hashtable]$bitmapsBySize  # size (int) -> Bitmap
    )

    $sortedSizes = $bitmapsBySize.Keys | Sort-Object
    $imageCount = $sortedSizes.Count

    # Pre-encode each bitmap as PNG into memory.
    $pngBytes = @{}
    foreach ($size in $sortedSizes) {
        $ms = New-Object System.IO.MemoryStream
        try {
            $bitmapsBySize[$size].Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $pngBytes[$size] = $ms.ToArray()
        } finally { $ms.Dispose() }
    }

    $headerSize = 6 + 16 * $imageCount
    $offset = $headerSize
    $directoryEntries = @()
    foreach ($size in $sortedSizes) {
        $bytes = $pngBytes[$size]
        # ICONDIRENTRY (16 bytes):
        #   1 byte  width  (0 = 256)
        #   1 byte  height (0 = 256)
        #   1 byte  color count (0)
        #   1 byte  reserved (0)
        #   2 bytes color planes (1)
        #   2 bytes bits per pixel (32)
        #   4 bytes image size (bytes)
        #   4 bytes image offset (from start of file)
        $w = if ($size -ge 256) { 0 } else { $size }
        $h = if ($size -ge 256) { 0 } else { $size }
        $entry = [byte[]]::new(16)
        $entry[0] = [byte]$w
        $entry[1] = [byte]$h
        $entry[2] = 0          # color count
        $entry[3] = 0          # reserved
        # color planes (1)
        $entry[4] = 1; $entry[5] = 0
        # bits per pixel (32)
        $entry[6] = 32; $entry[7] = 0
        # image size
        [Array]::Copy([BitConverter]::GetBytes([uint32]$bytes.Length), 0, $entry, 8, 4)
        # image offset
        [Array]::Copy([BitConverter]::GetBytes([uint32]$offset), 0, $entry, 12, 4)
        $directoryEntries += ,$entry
        $offset += $bytes.Length
    }

    $fs = [System.IO.File]::Create($path)
    try {
        # ICONDIR header (6 bytes): reserved(2)=0, type(2)=1, count(2)
        $hdr = [byte[]]::new(6)
        $hdr[0] = 0; $hdr[1] = 0           # reserved
        $hdr[2] = 1; $hdr[3] = 0           # type = 1 (icon)
        [Array]::Copy([BitConverter]::GetBytes([uint16]$imageCount), 0, $hdr, 4, 2)
        $fs.Write($hdr, 0, 6)
        foreach ($entry in $directoryEntries) { $fs.Write($entry, 0, 16) }
        foreach ($size in $sortedSizes) {
            $bytes = $pngBytes[$size]
            $fs.Write($bytes, 0, $bytes.Length)
        }
    } finally { $fs.Close() }
    $info = Get-Item $path
    Write-Host "[icon] $($info.Name) ($($imageCount) sizes, $($info.Length) bytes)"
}

# ----- Render each tray state at multiple sizes -------------------------------
$states = @(
    @{ Name = 'tray-on.ico';    Ring = $cyan;    Label = 'ON' }
    @{ Name = 'tray-off.ico';   Ring = $slate;   Label = 'OFF' }
    @{ Name = 'tray-error.ico'; Ring = $magenta; Label = 'ERROR' }
)

# Sizes embedded in each ICO (covers 100%-300% scale Win11 tray).
$icoSizes = @(16, 24, 32, 48, 64, 256)

foreach ($state in $states) {
    $bitmaps = @{}
    foreach ($size in $icoSizes) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $gfx = [System.Drawing.Graphics]::FromImage($bmp)
        $gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $gfx.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        try {
            # 12% margin on every side so the disk + ring have breathing room even when
            # WPF-UI's title bar clips the icon slot to a shorter-than-icon height. Anything
            # smaller and downsampled output (e.g., 64 -> 24) leaves the disk touching the
            # canvas edge -- clipping reads as flat-bottomed.
            $diskInset = [Math]::Max(1.0, [Math]::Round($size * 0.12))
            $diskBox = $size - 2 * $diskInset

            # Navy filled disk.
            $navyBrush = New-Object System.Drawing.SolidBrush($navy)
            try { $gfx.FillEllipse($navyBrush, $diskInset, $diskInset, $diskBox, $diskBox) } finally { $navyBrush.Dispose() }

            # Voxel stack inset further to leave room for the ring (insets scale with size).
            $voxelInsetRatio = if ($size -le 24) { 0.18 } else { 0.20 }
            $voxelSize = $size * (1.0 - 2 * $voxelInsetRatio)
            Draw-VoxelStack $gfx ($size / 2.0) ($size / 2.0) $voxelSize

            # State-colored ring on the disk boundary. Pen-centered draw, so we shrink
            # the path bbox by half-pen on each side to keep the ring fully inside the disk.
            $ringWidth = [Math]::Max(1.0, $size / 16.0)
            $halfPen = $ringWidth / 2.0
            $ringPen = New-Object System.Drawing.Pen($state.Ring, $ringWidth)
            try {
                $gfx.DrawEllipse($ringPen,
                    $diskInset + $halfPen,
                    $diskInset + $halfPen,
                    $diskBox - $ringWidth,
                    $diskBox - $ringWidth)
            } finally { $ringPen.Dispose() }
        } finally {
            $gfx.Dispose()
        }
        $bitmaps[$size] = $bmp
    }
    Save-Ico (Join-Path $resourcesDir $state.Name) $bitmaps

    # Also emit a standalone 64x64 PNG of the same artwork for the WPF window title bar
    # (avoids ICO multi-size embed ambiguity that produced the "flat bottom" at 16x16).
    # WPF's Image / WPF-UI's ImageIcon will downsample 64->18 cleanly, no aliasing.
    $stem = $state.Name -replace '\.ico$', ''
    $bitmaps[64].Save((Join-Path $resourcesDir "${stem}-titlebar.png"), [System.Drawing.Imaging.ImageFormat]::Png)

    foreach ($bmp in $bitmaps.Values) { $bmp.Dispose() }
}

# Remove any old placeholder ICOs from previous runs.
$placeholders = @('tray-on.placeholder.ico', 'tray-off.placeholder.ico', 'tray-error.placeholder.ico')
foreach ($p in $placeholders) {
    $path = Join-Path $resourcesDir $p
    if (Test-Path $path) {
        Remove-Item $path -Force
        Write-Host "[clean] removed $p"
    }
}

Write-Host ''
Write-Host "[done] Tray icons generated (Direction C iso voxel stack, $($icoSizes.Count) sizes per ICO)." -ForegroundColor Green
