# ROROROblox -- pack a self-contained dev build to a portable folder.
#
# Produces a no-install-required folder you can copy to a flash drive (or zip + send)
# and run on any Windows 11 PC for cross-machine smoke testing of the v1.2 Discord
# clan-coordination feature. Everything bundles: .NET 10 runtime, WPF, Lachee, all
# transitive deps, embedded assets, and our appsettings.json with the Discord App ID.
#
# Output:
#   dist/rororo-portable/             folder to copy (~150 MB)
#   dist/rororo-portable.zip          zipped form for sharing
#
# Run from repo root:
#   pwsh scripts/pack-portable-dev.ps1
#
# Optional flags:
#   -Configuration Release            (default Debug — Debug ships diagnostic logs
#                                      we want during smoke; Release strips them)
#   -SkipZip                          (skip the .zip; folder only)
#
# Target machine prerequisites:
#   - Windows 11 (or Windows 10 1809+).
#   - WebView2 Runtime installed (preinstalled on Win11; download from
#     https://go.microsoft.com/fwlink/p/?LinkId=2124703 if missing — Edge auto-installs it).
#   - Discord desktop app running for rich presence to display.
#
# What does NOT carry over:
#   - %LOCALAPPDATA%\ROROROblox\accounts.dat — DPAPI per-user, per-machine. The other
#     PC starts with no saved accounts. For pure Discord IPC + URI scheme registration
#     test (clanmate clicks Join on YOUR profile), no accounts needed; just run the
#     app and toggle rich presence ON in Preferences.
#   - %LOCALAPPDATA%\ROROROblox\discord-config.json — fresh start, all-off defaults.
#     Configure via Preferences -> Discord integration on the target PC.

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot 'src\ROROROblox.App\ROROROblox.App.csproj'
$outDir = Join-Path $repoRoot 'dist\rororo-portable'
$zipPath = Join-Path $repoRoot 'dist\rororo-portable.zip'

Write-Host ""
Write-Host "ROROROblox portable dev pack" -ForegroundColor Cyan
Write-Host "============================" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Output folder: $outDir"
Write-Host ""

# Clean previous output so we don't ship stale binaries.
if (Test-Path $outDir) {
    Write-Host "Cleaning previous $outDir ..."
    Remove-Item $outDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Stop any currently-running dev .exe (the publish would lock-fail otherwise).
$running = Get-Process | Where-Object { $_.ProcessName -eq 'ROROROblox.App' }
if ($running) {
    Write-Host "Stopping running dev process(es) for clean publish..." -ForegroundColor Yellow
    $running | ForEach-Object { Stop-Process -Id $_.Id -Force }
    Start-Sleep -Seconds 2
}

Write-Host ""
Write-Host "dotnet publish (self-contained, win-x64)..." -ForegroundColor Cyan

# Self-contained = bundles the .NET runtime so target PC needs no SDK install.
# Single-file is intentionally NOT used — WPF + WebView2 + Lachee historically have
# rough edges with single-file publish; folder form ships ~140 MB but works reliably.
& dotnet publish $csproj `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=embedded `
    --output $outDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit $LASTEXITCODE)"
}

# Drop a one-page README into the folder so the recipient has install instructions.
$readme = @'
# ROROROblox -- portable dev build

Cross-machine smoke build for the v1.2 Discord clan-coordination feature.

## Run

Double-click `ROROROblox.App.exe`. SmartScreen may warn -- click "More info"
then "Run anyway". The dev build is unsigned by design.

## Enable Discord rich presence

1. Right-click the system-tray icon -> Preferences -> scroll to "Discord integration".
2. Toggle ON: "Show RORORO in your Discord status."
3. Open Discord -> User Settings -> Activity Privacy -> Registered Games ->
   click "Add it!" -> pick "RORORO" from the dropdown -> "Add Game".
   (One-time per Discord install. Discord doesn't auto-detect rich-presence apps.)

After both, your Discord profile shows "Playing RoRoRoBlox". Clanmates with
this build also installed will see a clickable Join button when you launch
into a private server.

## What's bundled

The .NET 10 runtime, WPF, Microsoft.Web.WebView2, the Lachee Discord IPC
client, and the Discord Application ID for ROROROblox. No SDK or runtime
install needed on the target PC.

## What's NOT bundled

- Saved Roblox accounts. The on-disk store is DPAPI-encrypted per-user/per-machine
  (intentional security design); accounts.dat from another PC won't decrypt.
  Add accounts fresh if you want to launch alts on this PC.
- Discord webhook URL. Configure via Preferences if you want clan-channel posts.
- Microsoft Edge WebView2 runtime. Preinstalled on Windows 11; on Windows 10 you
  may need to install it: https://go.microsoft.com/fwlink/p/?LinkId=2124703

## Where state lives on this PC

- %LOCALAPPDATA%\ROROROblox\accounts.dat  -- saved accounts (DPAPI-encrypted)
- %LOCALAPPDATA%\ROROROblox\discord-config.json  -- Discord opt-ins
- %LOCALAPPDATA%\ROROROblox\logs\  -- daily-rolling logs (Discord lifecycle traces here)

## Cleanup

Delete the folder when done. Also delete %LOCALAPPDATA%\ROROROblox\ to remove
the per-user data. The Discord URI scheme registry entry under
HKEY_CURRENT_USER\Software\Classes\discord-1501748116985221272\ can stay or
be removed -- it just maps Discord Join clicks to launch this exe.
'@
Set-Content -Path (Join-Path $outDir 'PORTABLE-README.txt') -Value $readme -Encoding UTF8

# Size + file count summary.
$folderInfo = Get-ChildItem $outDir -Recurse | Measure-Object -Property Length -Sum
$mb = [math]::Round($folderInfo.Sum / 1MB, 1)
Write-Host ""
Write-Host "Published $($folderInfo.Count) files, $mb MB" -ForegroundColor Green

if (-not $SkipZip) {
    Write-Host ""
    Write-Host "Compressing to $zipPath ..." -ForegroundColor Cyan
    Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
    $zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "Zip: $zipMb MB" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Folder: $outDir"
if (-not $SkipZip) {
    Write-Host "  Zip:    $zipPath"
}
Write-Host ""
Write-Host "Copy the folder (or zip) to a flash drive. Recipient runs"
Write-Host "ROROROblox.App.exe and follows PORTABLE-README.txt for setup." -ForegroundColor DarkGray
