# ROROROblox -- local MSIX uninstall + cleanup verification.
#
# Companion to scripts/install-local-msix.ps1. Removes the installed package and verifies
# that the LocalState folder (where the DPAPI-encrypted accounts.dat lives) is also gone.
#
# This addresses the Sanduhr playbook gotcha:
#   "Windows Credential Manager data doesn't auto-clear on MSIX uninstall."
# We use LocalAppData (DPAPI-encrypted accounts.dat), not Credential Manager. For
# MSIX-packaged apps, LocalAppData is virtualized to the package's LocalState folder, which
# IS auto-removed on uninstall. This script verifies that. If it isn't gone, that's a
# defect we have to fix before Store submission.
#
# Run from repo root:
#   powershell -ExecutionPolicy Bypass -File scripts/uninstall-local-msix.ps1

[CmdletBinding()]
param(
    [switch]$KeepUnpackagedData
)

$ErrorActionPreference = 'Stop'

# Find the installed package.
$installed = Get-AppxPackage -Name '*ROROROblox*' -ErrorAction SilentlyContinue
if (-not $installed) {
    Write-Host '[uninstall] No ROROROblox MSIX install found.' -ForegroundColor Yellow
    Write-Host '[uninstall] (If you ran via "dotnet run" -- that is unpackaged, not MSIX. See bottom of this script.)' -ForegroundColor Yellow
}

foreach ($pkg in $installed) {
    $pfn = $pkg.PackageFullName
    $packageRoot = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)"

    Write-Host "[uninstall] Removing $pfn..." -ForegroundColor Cyan
    Remove-AppxPackage -Package $pfn

    # Verify the package is gone from the registry.
    $stillThere = Get-AppxPackage -Name $pkg.Name -ErrorAction SilentlyContinue | Where-Object { $_.PackageFullName -eq $pfn }
    if ($stillThere) {
        Write-Host "[uninstall] Remove-AppxPackage reported success but package is still listed. Investigate." -ForegroundColor Red
        exit 1
    }
    Write-Host "[uninstall] Package removed from registry." -ForegroundColor Green

    # Verify LocalState (where accounts.dat lives) is gone.
    if (Test-Path $packageRoot) {
        Write-Host "[uninstall] WARNING: $packageRoot still exists after uninstall." -ForegroundColor Yellow
        $localState = Join-Path $packageRoot 'LocalState'
        if (Test-Path $localState) {
            Write-Host "[uninstall]          LocalState present -- DPAPI-encrypted secrets may persist." -ForegroundColor Yellow
            $files = Get-ChildItem $localState -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 10
            if ($files) {
                Write-Host '[uninstall]          Top-level contents:' -ForegroundColor Yellow
                $files | ForEach-Object { Write-Host "            - $($_.FullName)" -ForegroundColor Yellow }
            }
        }
        Write-Host '[uninstall]          On Win11, MSIX should auto-remove this folder. If it persists,' -ForegroundColor Yellow
        Write-Host '[uninstall]          the package may have been pinned by another app or kept by an' -ForegroundColor Yellow
        Write-Host '[uninstall]          orphaned process. Remove manually and re-test.' -ForegroundColor Yellow
    } else {
        Write-Host "[uninstall] LocalState gone -- DPAPI-encrypted vault was removed cleanly." -ForegroundColor Green
    }
}

# Also offer to clean unpackaged-mode data (if the user ran "dotnet run" before installing the MSIX).
$unpackagedData = Join-Path $env:LOCALAPPDATA 'ROROROblox'
if ((Test-Path $unpackagedData) -and -not $KeepUnpackagedData) {
    Write-Host ''
    Write-Host "[uninstall] Detected legacy unpackaged data at:" -ForegroundColor Cyan
    Write-Host "  $unpackagedData"
    Write-Host '[uninstall] This is leftover from "dotnet run" sessions, NOT from MSIX installs.' -ForegroundColor Cyan
    Write-Host '[uninstall] Pass -KeepUnpackagedData to retain. Otherwise it will be removed.' -ForegroundColor Cyan
    $answer = Read-Host '[uninstall] Remove now? (y/N)'
    if ($answer -match '^(y|yes)$') {
        Remove-Item $unpackagedData -Recurse -Force
        Write-Host "[uninstall] Removed $unpackagedData" -ForegroundColor Green
    } else {
        Write-Host "[uninstall] Left $unpackagedData in place." -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '[uninstall] DONE.' -ForegroundColor Green
