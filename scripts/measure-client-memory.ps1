<#
.SYNOPSIS
    Measures what Roblox clients actually cost on THIS machine, and prints a block to paste back.

.DESCRIPTION
    Read-only. Samples every RobloxPlayerBeta process plus system memory on an interval and
    reports the per-client footprint, the aggregate, and how many clients this machine can
    actually hold.

    Why it exists: RoRoRo's memory warnings are calibrated from ONE machine (47 GB, Pet Sim 99,
    Roblox 733) where a client measured 2650 MB median / 3280 MB peak. Whether that generalises is
    unknown, and a warning tuned to the wrong number either cries wolf or never fires. This
    gathers the evidence to fix that.

    Needs no admin rights, installs nothing, writes one CSV, and never touches Roblox or RoRoRo.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\measure-client-memory.ps1

.EXAMPLE
    # Longer run, and note which game so the numbers mean something later.
    powershell -ExecutionPolicy Bypass -File scripts\measure-client-memory.ps1 -Minutes 60 -Note "Pet Sim 99, 10 alts"
#>
param(
    [int]$Minutes = 30,
    [int]$IntervalSec = 30,
    [string]$Note = "",
    [string]$Out = "$env:TEMP\rororo-client-memory.csv",
    # Overridable so the reporting path can be exercised against any process, and so this keeps
    # working if Roblox ever renames the player binary.
    [string]$ProcessName = "RobloxPlayerBeta"
)

$ErrorActionPreference = 'Stop'

# Anything under this is a helper/subprocess (crash handler, browser host), not a game client.
$GameClientFloorMb = 500

$total = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
Write-Host ""
Write-Host "RoRoRo client-memory measurement" -ForegroundColor Cyan
Write-Host ("  machine   : {0:N1} GB installed" -f ($total / 1GB))
Write-Host ("  sampling  : every {0}s for {1} min" -f $IntervalSec, $Minutes)
Write-Host ("  writing   : {0}" -f $Out)
if ($Note) { Write-Host ("  note      : {0}" -f $Note) }
Write-Host ""
Write-Host "Launch your alts now if they are not already running. Ctrl+C stops early and still reports." -ForegroundColor Yellow
Write-Host ""

"timestamp,pid,mb,availableMb" | Out-File -FilePath $Out -Encoding utf8
$deadline = (Get-Date).AddMinutes($Minutes)
$samples = @()

try {
    # do/while, not while: take at least one reading before checking the clock, so any duration
    # produces a report rather than an empty file. A run that collects nothing and says nothing is
    # indistinguishable from a broken script on somebody else's machine.
    do {
        $availableMb = [int]((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1KB)
        $t = (Get-Date).ToString("s")
        $clients = @(Get-Process $ProcessName -ErrorAction SilentlyContinue)

        foreach ($p in $clients) {
            $mb = [int]($p.PrivateMemorySize64 / 1MB)
            "$t,$($p.Id),$mb,$availableMb" | Out-File -FilePath $Out -Append -Encoding utf8
            if ($mb -ge $GameClientFloorMb) { $samples += [pscustomobject]@{ Pid = $p.Id; Mb = $mb } }
        }

        $live = @($clients | Where-Object { $_.PrivateMemorySize64 / 1MB -ge $GameClientFloorMb })
        Write-Host ("  {0}  {1,2} client(s)  {2,6} MB held  {3,6} MB free" -f `
            (Get-Date -Format "HH:mm:ss"), $live.Count,
            [int](($live | Measure-Object PrivateMemorySize64 -Sum).Sum / 1MB), $availableMb)

        if ((Get-Date) -ge $deadline) { break }
        Start-Sleep -Seconds $IntervalSec
    } while ($true)
}
finally {
    Write-Host ""
    if ($samples.Count -eq 0) {
        Write-Host "No game clients were running - nothing to report." -ForegroundColor Red
        Write-Host "Launch some alts and run this again." -ForegroundColor Red
    }
    else {

    # Per-client peak, then the median and max ACROSS clients - the same two figures RoRoRo's
    # thresholds are built from, so the numbers are directly comparable.
    $peaks = $samples | Group-Object Pid | ForEach-Object { ($_.Group | Measure-Object Mb -Maximum).Maximum } | Sort-Object
    $median = $peaks[[int]($peaks.Count / 2)]
    $max = $peaks[-1]
    $mostAtOnce = ($samples | Group-Object Pid).Count

    Write-Host "=== PASTE THIS BACK ===" -ForegroundColor Green
    Write-Host ""
    Write-Host ("installed RAM      : {0:N1} GB" -f ($total / 1GB))
    Write-Host ("clients seen       : {0}" -f $mostAtOnce)
    Write-Host ("per-client median  : {0} MB" -f $median)
    Write-Host ("per-client peak    : {0} MB" -f $max)
    Write-Host ("samples            : {0}" -f $samples.Count)
    if ($Note) { Write-Host ("note               : {0}" -f $Note) }
    Write-Host ""
    Write-Host ("RoRoRo currently assumes 2650 MB median / 3280 MB peak.")
    if ($median -gt 2650) {
        Write-Host ("  -> this machine runs HEAVIER by {0} MB per client" -f ($median - 2650)) -ForegroundColor Yellow
    } elseif ($median -lt 2650) {
        Write-Host ("  -> this machine runs LIGHTER by {0} MB per client" -f (2650 - $median)) -ForegroundColor Yellow
    } else {
        Write-Host "  -> matches"
    }
    Write-Host ""
    Write-Host ("raw CSV: {0}" -f $Out)
    Write-Host ""
    }
}
