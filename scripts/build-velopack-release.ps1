# ROROROblox -- build a Velopack Setup.exe + delta package for GitHub Releases.
#
# Produces a self-contained, no-cert, no-runtime-install installer that the Pet Sim 99
# clan can download from the Releases page. SmartScreen will warn ("More info -> Run
# anyway") on first install -- that's the v1 trade for shipping without a code-sign cert.
#
# Output (under dist/release/):
#   - rororo-win-Setup.exe        installer the clan downloads
#   - rororo-<v>-win-full.nupkg   full package (auto-updater consumes this)
#   - rororo-<v>-win-delta.nupkg  delta vs prior release (only after release #2)
#   - releases.win.json           manifest the Velopack auto-updater pings
#
# Run from repo root:
#   pwsh scripts/build-velopack-release.ps1 -Version 1.1.2.0
#
# Requires:
#   - .NET 10 SDK (`dotnet --list-sdks` shows 10.x)
#   - vpk  (install: `dotnet tool install -g vpk`)
#   - Square44x44Logo.targetsize-{16,24,32,48,256}.png present under
#     src/ROROROblox.App/Package/Logos/. Run scripts/generate-store-assets.ps1 first
#     if missing.
#
# Upload flow (manual, v1):
#   1. Run this script with the target version.
#   2. git tag v<version> && git push origin v<version>
#   3. github.com -> Releases -> Draft a new release from the tag.
#   4. Upload EVERY file from dist/release/ as a Release asset. The auto-updater needs
#      releases.win.json + the *-full.nupkg, not just Setup.exe.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,

    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$ReleaseNotes,
    [switch]$SkipPublish,
    [switch]$SkipIco,
    # CI: leave dist/release/ contents in place (prior nupkgs from `vpk download github`)
    # so vpk pack can compute a delta against the previous release.
    [switch]$NoClean
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile = Join-Path $repoRoot 'src\ROROROblox.App\ROROROblox.App.csproj'
$publishDir = Join-Path $repoRoot 'dist\publish'
$releaseDir = Join-Path $repoRoot 'dist\release'
$logosDir = Join-Path $repoRoot 'src\ROROROblox.App\Package\Logos'
$icoOutPath = Join-Path $logosDir 'AppIcon.ico'

Write-Host "[velopack] RORORO v$Version ($Runtime, $Configuration)" -ForegroundColor Cyan

# ----- 1. Pre-flight ----------------------------------------------------------

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk not found on PATH. Install with: dotnet tool install -g vpk"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found on PATH."
}
if (-not (Test-Path $projectFile)) {
    throw "Project file not found: $projectFile"
}

# ----- 2. AppIcon.ico (multi-size, PNG-encoded) -------------------------------
# Modern Windows accepts PNG-encoded ICO directories at any size. We embed 16, 24, 32,
# 48, and 256 so the Setup.exe shows correctly in shell, taskbar, and high-DPI surfaces.

if (-not $SkipIco) {
    $sizes = @(16, 24, 32, 48, 256)
    $sources = $sizes | ForEach-Object {
        Join-Path $logosDir "Square44x44Logo.targetsize-$_.png"
    }
    foreach ($s in $sources) {
        if (-not (Test-Path $s)) {
            throw "Missing icon source: $s. Run scripts/generate-store-assets.ps1 first."
        }
    }

    $stream = [System.IO.File]::Open($icoOutPath, [System.IO.FileMode]::Create)
    try {
        $bw = New-Object System.IO.BinaryWriter($stream)
        # ICONDIR header
        $bw.Write([uint16]0)              # Reserved
        $bw.Write([uint16]1)              # Type: 1 = ICO
        $bw.Write([uint16]$sizes.Count)   # Image count

        $pngBytes = @($sources | ForEach-Object { ,([System.IO.File]::ReadAllBytes($_)) })
        $offset = 6 + (16 * $sizes.Count)
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $sz = $sizes[$i]
            $b = $pngBytes[$i]
            # ICONDIRENTRY (16 bytes)
            $bw.Write([byte]($sz -band 0xFF))   # Width  (256 wraps to 0 -- spec)
            $bw.Write([byte]($sz -band 0xFF))   # Height (256 wraps to 0)
            $bw.Write([byte]0)                  # ColorCount (0 for >=256 colors)
            $bw.Write([byte]0)                  # Reserved
            $bw.Write([uint16]1)                # Planes
            $bw.Write([uint16]32)               # BitCount
            $bw.Write([uint32]$b.Length)        # BytesInRes
            $bw.Write([uint32]$offset)          # ImageOffset
            $offset += $b.Length
        }
        # Image data, in same order as directory
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $bw.Write($pngBytes[$i])
        }
        $bw.Flush()
    } finally {
        $stream.Dispose()
    }
    Write-Host "[ico] Wrote $icoOutPath ($($sizes -join ',') px)" -ForegroundColor Cyan
}

# ----- 3. dotnet publish ------------------------------------------------------
# Self-contained so clan members don't need to install .NET 10 runtime first.
# Single-file off so Velopack can hash + delta-patch individual files.

if (-not $SkipPublish) {
    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }
    Write-Host "[publish] dotnet publish ($Runtime, self-contained)..." -ForegroundColor Cyan
    & dotnet publish $projectFile `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $publishDir `
        /p:Version=$Version `
        /p:PublishSingleFile=false `
        /p:DebugType=embedded
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit $LASTEXITCODE)"
    }
}

if (-not (Test-Path (Join-Path $publishDir 'ROROROblox.App.exe'))) {
    throw "Publish output missing ROROROblox.App.exe at $publishDir. Re-run without -SkipPublish."
}

# ----- 4. vpk pack ------------------------------------------------------------

if (-not $NoClean -and (Test-Path $releaseDir)) {
    Remove-Item -Recurse -Force $releaseDir
}
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

$vpkArgs = @(
    'pack'
    '--packId',      'RORORO'
    '--packVersion', $Version
    '--packDir',     $publishDir
    '--mainExe',     'ROROROblox.App.exe'
    '--packTitle',   'RORORO'
    '--packAuthors', '626 Labs'
    '--icon',        $icoOutPath
    '--outputDir',   $releaseDir
    '--delta',       'BestSpeed'
)
if ($ReleaseNotes -and (Test-Path $ReleaseNotes)) {
    $vpkArgs += @('--releaseNotes', (Resolve-Path $ReleaseNotes).Path)
}

Write-Host "[vpk] vpk $($vpkArgs -join ' ')" -ForegroundColor Cyan
& vpk @vpkArgs
if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed (exit $LASTEXITCODE)"
}

# ----- 5. Summary -------------------------------------------------------------

$artifacts = Get-ChildItem -Path $releaseDir -File | Sort-Object Name
Write-Host ""
Write-Host "Built v$Version -> $releaseDir" -ForegroundColor Green
foreach ($a in $artifacts) {
    $size = [math]::Round($a.Length / 1MB, 1)
    Write-Host ("  {0,-50} {1,8} MB" -f $a.Name, $size)
}
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. git tag v$Version && git push origin v$Version"
Write-Host "  2. github.com -> Releases -> Draft from the tag"
Write-Host "  3. Upload EVERY file from dist/release/ as a Release asset."
Write-Host "     (Setup.exe is the install entry point; releases.win.json + *-full.nupkg"
Write-Host "      are what the in-app auto-updater pings.)"
