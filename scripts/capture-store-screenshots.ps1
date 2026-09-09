#Requires -Version 7.0
<#
.SYNOPSIS
    Produces the Store carousel set, in store filenames, for one language.

.DESCRIPTION
    capture-ui.ps1 is the engine; this is the Store's shot list on top of it. Two gaps it closes:

    1. The engine names files "{id}-{surface}--{theme}.png" ("05-about--brand.png"), but the
       carousel ships "03-about.png". The 2026-08-30 refresh bridged that by hand, which is fine
       once and untenable across seven languages.
    2. The engine's default is a FOUR-THEME round per surface. The carousel wants one theme per
       shot, plus a single 2x2 panel that shows the theme range in one slot.

    THREE OF THE TEN ARE NOT CAPTURABLE HERE, by policy rather than by omission:
    01-accounts-running, 09-compact and 10-multi-instance are running-state shots that need live
    Roblox clients on screen. Launching those is on capture-ui.ps1's deny list and on the macro
    wall, so an unattended run does not get to create that state. The 2026-08-30 refresh hit the
    same wall and kept all three from 2026-08-12. They are listed as SKIPPED at the end of every
    run rather than silently absent, so nobody mistakes seven files for a complete set.

.EXAMPLE
    pwsh -File scripts/capture-store-screenshots.ps1 -Language pl
    pwsh -File scripts/capture-store-screenshots.ps1 -AllLanguages
#>
[CmdletBinding()]
param(
    [string]$Language,
    [switch]$AllLanguages,
    [string]$Theme = 'brand',
    [switch]$AllowRealIdentities
)

$ErrorActionPreference = 'Stop'
$engine = Join-Path $PSScriptRoot 'capture-ui.ps1'
$root   = Join-Path $PSScriptRoot '..\docs\store\screenshots'

# harness surface id -> shipped carousel filename. Kept here rather than in ui-routes.json
# because it is a STORE concern: the routes describe the app, this describes the listing.
$MAP = [ordered]@{
    # 01 is capturable ONLY while real clients are running -- the harness may photograph that
    # state, it just may not create it (deny list + macro wall). Included conditionally below.
    '01' = '01-accounts-running.png'
    '05' = '03-about.png'
    '08' = '04-games.png'
    '07' = '05-diagnostics.png'
    '06' = '06-history.png'
    '09' = '07-plugins.png'
    '10' = '08-theme-builder.png'
}
$SKIPPED = @(
    '09-compact.png           (needs live Roblox clients AND compact mode)'
    '10-multi-instance.png    (needs eight live clients; the shipped one is a composite)'
)

# Drop 01 when nothing is running: capturing an idle roster under the filename
# "01-accounts-running" would ship a screenshot whose own caption ("Three accounts running at
# once, each with its own memory use") the frame does not support.
$liveClients = @(Get-Process -Name 'RobloxPlayerBeta' -ErrorAction SilentlyContinue).Count
if ($liveClients -lt 1) {
    $MAP.Remove('01')
    $SKIPPED = ,'01-accounts-running.png  (no live clients running - launch some and re-run)' + $SKIPPED
} else {
    Write-Host "$liveClients live Roblox client(s) - 01-accounts-running is capturable." -ForegroundColor Green
}

function Invoke-OneLanguage {
    param([string]$Culture)

    $out = Join-Path $root $Culture
    Write-Host "`n=== $Culture ===" -ForegroundColor Cyan

    # Invoked directly rather than through `pwsh -File`. -File cannot bind an ARRAY argument:
    # a space-separated list fails ("a positional parameter cannot be found that accepts
    # argument '10'") and a comma-joined string arrives as one literal surface id that matches
    # nothing. A direct call binds -Surface as the string[] it is declared to be.
    $common = @{ StoreFrame = $true; Theme = $Theme; OutDir = $out; Surface = @($MAP.Keys) }
    if ($Culture -ne 'en') { $common.Language = $Culture }
    if ($AllowRealIdentities) { $common.AllowRealIdentities = $true }

    # One pass for the six single-surface shots. -Language restarts the app, so it is passed
    # once here and NOT again for the panel below -- a second restart would be wasted minutes.
    & $engine @common

    # The theme panel is its own invocation: it tiles ONE surface across four themes, which is a
    # different contract from "one theme per surface" above.
    # Non-fatal: 02-themes is ONE carousel slot, and the six shots above are the set. Letting a
    # panel failure abort the run would also skip the rename, leaving engine-named files on disk
    # that look like a successful capture until someone reads them.
    $panel = @{ StoreFrame = $true; ThemePanel = $true; OutDir = $out; Surface = @('03') }
    if ($AllowRealIdentities) { $panel.AllowRealIdentities = $true }
    try { & $engine @panel }
    catch { Write-Warning "$Culture : theme panel failed ($($_.Exception.Message)); 02-themes.png not refreshed" }

    # Rename engine output to carousel names.
    foreach ($id in $MAP.Keys) {
        $src = Get-ChildItem $out -Filter "$id-*--$Theme.png" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($src) {
            Move-Item $src.FullName (Join-Path $out $MAP[$id]) -Force
            Write-Host ("  {0,-26} <- {1}" -f $MAP[$id], $src.Name)
        } else {
            Write-Warning ("  {0,-26} MISSING (engine produced no {1}-*--{2}.png)" -f $MAP[$id], $id, $Theme)
        }
    }
    $panel = Get-ChildItem $out -Filter "*--themes.png" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($panel) {
        Move-Item $panel.FullName (Join-Path $out '02-themes.png') -Force
        Write-Host ("  {0,-26} <- {1}" -f '02-themes.png', $panel.Name)
    }

    # The engine's per-run manifests describe engine names, which no longer exist after the
    # rename. Leaving them would document a set that is not on disk.
    Get-ChildItem $out -Filter 'run-*.json' -ErrorAction SilentlyContinue | Remove-Item -Force
}

$cultures = if ($AllLanguages) { @('en','fr','de','ru','pt-BR','pl','es') }
            elseif ($Language)  { @($Language) }
            else { throw "specify -Language <culture> or -AllLanguages" }

foreach ($c in $cultures) { Invoke-OneLanguage -Culture $c }

Write-Host "`nNOT captured in any language - these need live Roblox clients:" -ForegroundColor Yellow
$SKIPPED | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
Write-Host "Copy them per language from docs/store/screenshots/ once captured by hand."
