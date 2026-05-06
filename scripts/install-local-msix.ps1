# ROROROblox -- local MSIX install smoke (per Sanduhr Store playbook).
#
# Builds a sideload MSIX, installs it via Add-AppxPackage, and prints how to verify the
# install + run the app. Run this BEFORE every Partner Center submission to catch manifest
# errors and asset-bundling issues that Partner Center will reject for.
#
# Run from repo root (admin not required for sideload install if cert is trusted):
#   powershell -ExecutionPolicy Bypass -File scripts/install-local-msix.ps1
#
# Prerequisites:
#   - dev-cert.pfx generated via scripts/generate-dev-cert.ps1
#   - The cert's public .cer imported into Local Machine \ Trusted People (one-time per box)
#   - No existing ROROROblox install (this script will refuse if one is found, to avoid
#     accidentally clobbering live data)

[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$CertPath,
    [string]$CertPassword,
    [switch]$SkipBuild,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Resolve repo root in the BODY -- $PSScriptRoot in a param() default doesn't populate
# reliably under Windows PowerShell 5.1 (the default `powershell.exe`). pwsh 7 handles it
# fine but we don't get to assume the invocation shell.
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

# Resolve dev-cert defaults
if (-not $CertPath) {
    $CertPath = Join-Path $RepoRoot 'dev-cert.pfx'
}
if (-not $CertPassword) {
    $CertPassword = $env:ROROROBLOX_DEV_CERT_PASSWORD
    if (-not $CertPassword) {
        Write-Host '[install] No -CertPassword provided and ROROROBLOX_DEV_CERT_PASSWORD not set.' -ForegroundColor Yellow
        Write-Host '[install] Falling back to interactive prompt.' -ForegroundColor Yellow
        $secure = Read-Host -Prompt 'dev-cert.pfx password' -AsSecureString
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try { $CertPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
        finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    }
}

# Detect existing install -- refuse to overwrite without -Force.
$existing = Get-AppxPackage -Name '*ROROROblox*' -ErrorAction SilentlyContinue
if ($existing -and -not $Force) {
    Write-Host '[install] ROROROblox is already installed:' -ForegroundColor Yellow
    $existing | Format-Table Name, PackageFullName, InstallLocation -AutoSize
    Write-Host '[install] Pass -Force to remove the existing install before re-installing.' -ForegroundColor Yellow
    Write-Host '[install] Or run scripts/uninstall-local-msix.ps1 first.' -ForegroundColor Yellow
    exit 1
}
if ($existing -and $Force) {
    Write-Host '[install] -Force specified; removing existing install...' -ForegroundColor Yellow
    foreach ($pkg in $existing) {
        Remove-AppxPackage -Package $pkg.PackageFullName
        Write-Host "[install] Removed $($pkg.PackageFullName)"
    }
}

# Build the sideload MSIX.
$msixPath = Join-Path $RepoRoot 'dist\RORORO-Sideload.msix'
if (-not $SkipBuild) {
    Write-Host '[install] Building sideload MSIX...' -ForegroundColor Cyan
    $buildScript = Join-Path $RepoRoot 'scripts\build-msix.ps1'
    & powershell -ExecutionPolicy Bypass -File $buildScript -Sideload -CertPath $CertPath -CertPassword $CertPassword
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[install] Build failed (exit $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path $msixPath)) {
    Write-Host "[install] MSIX not found at $msixPath." -ForegroundColor Red
    exit 1
}

# Install.
Write-Host "[install] Installing $msixPath..." -ForegroundColor Cyan
try {
    Add-AppxPackage -Path $msixPath
} catch {
    Write-Host "[install] Add-AppxPackage failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host '[install] Common causes:' -ForegroundColor Yellow
    Write-Host '  - dev-cert public key not in Local Machine \ Trusted People' -ForegroundColor Yellow
    Write-Host '  - Manifest identity mismatch (publisher CN does not match cert subject)' -ForegroundColor Yellow
    Write-Host '  - Logo asset bundling -- confirm scripts/build-msix.ps1 -Verify passes' -ForegroundColor Yellow
    exit 1
}

# Inspect what got installed.
$installed = Get-AppxPackage -Name '*ROROROblox*' | Select-Object -First 1
if (-not $installed) {
    Write-Host '[install] Install reported success but Get-AppxPackage finds nothing. Aborting.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '[install] DONE.' -ForegroundColor Green
Write-Host "[install] Package         : $($installed.PackageFullName)"
Write-Host "[install] Install location : $($installed.InstallLocation)"
Write-Host "[install] Local state     : $($installed.InstallLocation -replace 'WindowsApps', 'Packages\<PFN>\LocalState')"
Write-Host ''
Write-Host '[install] Smoke checklist:' -ForegroundColor Cyan
Write-Host '  1. Launch from Start Menu (search "ROROROblox")'
Write-Host '  2. Tray icon appears (cyan ring = ON state)'
Write-Host '  3. MainWindow opens; account list renders'
Write-Host '  4. About button reveals AboutWindow with dark title bar'
Write-Host '  5. Diagnostics button opens DiagnosticsWindow with dark title bar'
Write-Host '  6. + Add Account opens the login WebView (do NOT actually sign in for this smoke)'
Write-Host ''
Write-Host '[install] When done smoking, uninstall with:'
Write-Host "  powershell -ExecutionPolicy Bypass -File scripts/uninstall-local-msix.ps1"
