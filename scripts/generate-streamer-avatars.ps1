# ROROROblox -- generate INTERIM banked avatar tiles for streamer mode.
# Output: src/ROROROblox.App/StreamerMode/Avatars/{id}.png (256x256, 12 files)
#
# Run from repo root:
#   powershell -ExecutionPolicy Bypass -File scripts/generate-streamer-avatars.ps1
#
# STATUS: INTERIM ART. This satisfies Task 9 (banked avatar images must exist so the
# feature renders instead of showing broken-image boxes) with clean, on-brand,
# programmatically-generated tiles. Este does a final polish pass through the
# 626labs-design skill before the Store RC -- see docs/superpowers/sdd/task-9-report.md.
#
# Technique: GDI+ (System.Drawing) cannot render COLR/CPAL color-emoji layers -- it falls
# back to the font's monochrome outline glyph. That fallback is actually a clean, flat,
# icon-style silhouette (verified against Segoe UI Emoji on this box), so each avatar is
# built as: on-brand background fill + a colored accent border + the id's emoji rendered
# as a solid-color glyph on top. Distinct per id, playful, and unmistakably not a lorem
# placeholder.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outDir = Join-Path $repoRoot 'src\ROROROblox.App\StreamerMode\Avatars'

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

# ----- Brand tokens (see ~/.claude/skills/626labs-design) ---------------------
$navy    = [System.Drawing.Color]::FromArgb(255, 15, 31, 49)
$cyan    = [System.Drawing.Color]::FromArgb(255, 23, 212, 250)
$magenta = [System.Drawing.Color]::FromArgb(255, 242, 47, 137)

function Get-BrandColor([string]$name) {
    switch ($name) {
        'navy'    { return $navy }
        'cyan'    { return $cyan }
        'magenta' { return $magenta }
        default   { throw "Unknown brand color '$name'" }
    }
}

# ----- Per-id glyph + palette assignment ---------------------------------------
# Emoji codepoints match the mapping suggested in the Task 9 brief. Background is
# navy-dominant (matches the app's field color); cyan/magenta always appear as a
# paired glyph+border accent so no tile is same-color-on-same-color (e.g. no
# cyan glyph on a cyan background).
$avatars = @(
    @{ Id = 'noodle';    Codepoint = 0x1F35C; Bg = 'navy';    Glyph = 'cyan';    Border = 'magenta' } # steaming bowl
    @{ Id = 'duck';      Codepoint = 0x1F986; Bg = 'navy';    Glyph = 'magenta'; Border = 'cyan' }    # duck
    @{ Id = 'potato';    Codepoint = 0x1F954; Bg = 'cyan';    Glyph = 'navy';    Border = 'magenta' } # potato
    @{ Id = 'cabbage';   Codepoint = 0x1F96C; Bg = 'navy';    Glyph = 'cyan';    Border = 'magenta' } # leafy green
    @{ Id = 'muffin';    Codepoint = 0x1F9C1; Bg = 'magenta'; Glyph = 'navy';    Border = 'cyan' }    # cupcake
    @{ Id = 'artichoke'; Codepoint = 0x1F33F; Bg = 'navy';    Glyph = 'magenta'; Border = 'cyan' }    # herb sprig
    @{ Id = 'parsnip';   Codepoint = 0x1F955; Bg = 'navy';    Glyph = 'cyan';    Border = 'magenta' } # carrot
    @{ Id = 'pixel';     Codepoint = 0x1F7E6; Bg = 'cyan';    Glyph = 'navy';    Border = 'magenta' } # blue square
    @{ Id = 'bloxwell';  Codepoint = 0x1F3A9; Bg = 'navy';    Glyph = 'magenta'; Border = 'cyan' }    # top hat
    @{ Id = 'turnip';    Codepoint = 0x1F360; Bg = 'navy';    Glyph = 'cyan';    Border = 'magenta' } # sweet potato
    @{ Id = 'waffle';    Codepoint = 0x1F9C7; Bg = 'magenta'; Glyph = 'navy';    Border = 'cyan' }    # waffle
    @{ Id = 'pickle';    Codepoint = 0x1F952; Bg = 'navy';    Glyph = 'magenta'; Border = 'cyan' }    # cucumber
)

$size = 256
$borderWidth = 10
$glyphFontSize = 150

foreach ($a in $avatars) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList @($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gfx.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    try {
        $bgColor = Get-BrandColor $a.Bg
        $glyphColor = Get-BrandColor $a.Glyph
        $borderColor = Get-BrandColor $a.Border

        # Background fill.
        $bgBrush = New-Object System.Drawing.SolidBrush -ArgumentList @($bgColor)
        try { $gfx.FillRectangle($bgBrush, 0, 0, $size, $size) } finally { $bgBrush.Dispose() }

        # Accent border frame (pen-centered draw, inset by half-pen so it stays fully
        # inside the canvas -- same convention as generate-tray-icons.ps1's ring).
        $halfPen = $borderWidth / 2.0
        $borderPen = New-Object System.Drawing.Pen -ArgumentList @($borderColor, $borderWidth)
        try {
            $gfx.DrawRectangle($borderPen, $halfPen, $halfPen, ($size - $borderWidth), ($size - $borderWidth))
        } finally { $borderPen.Dispose() }

        # Centered glyph -- GDI+ renders the emoji font's monochrome fallback outline,
        # which reads as a clean flat icon (not a color emoji, not a tofu box).
        $chars = [char]::ConvertFromUtf32($a.Codepoint)
        $font = New-Object System.Drawing.Font -ArgumentList @('Segoe UI Emoji', $glyphFontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        try {
            $glyphBrush = New-Object System.Drawing.SolidBrush -ArgumentList @($glyphColor)
            try {
                $fmt = New-Object System.Drawing.StringFormat
                $fmt.Alignment = [System.Drawing.StringAlignment]::Center
                $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
                $rect = New-Object System.Drawing.RectangleF -ArgumentList @(0, 0, $size, $size)
                $gfx.DrawString($chars, $font, $glyphBrush, $rect, $fmt)
            } finally { $glyphBrush.Dispose() }
        } finally { $font.Dispose() }
    } finally {
        $gfx.Dispose()
    }

    $outPath = Join-Path $outDir "$($a.Id).png"
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $info = Get-Item $outPath
    Write-Host "[avatar] $($info.Name) ($($info.Length) bytes)"
}

Write-Host ''
Write-Host "[done] $($avatars.Count) interim streamer-mode avatar tiles generated." -ForegroundColor Green
Write-Host "[note] INTERIM art -- final polish pass through the 626labs-design skill still pending." -ForegroundColor Yellow
