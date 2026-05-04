# ROROROblox -- MSIX build pipeline.
# Two flavors:
#   -Sideload : self-signed via dev-cert.pfx (clan distribution + manual SmartScreen click-through)
#   -Store    : unsigned (Partner Center signs after submission)
#
# Run from repo root:
#   powershell -ExecutionPolicy Bypass -File scripts/build-msix.ps1 -Sideload -CertPath dev-cert.pfx -CertPassword 'pwd'
#   powershell -ExecutionPolicy Bypass -File scripts/build-msix.ps1 -Store
#
# Logo-presence check fails fast if any of the required Store assets are missing or look like
# programmatic placeholders. Pattern (x) from SnipSnap retro: never ship a broken-looking tile.

[CmdletBinding(DefaultParameterSetName = 'Sideload')]
param(
    [Parameter(ParameterSetName = 'Sideload')]
    [switch]$Sideload,

    [Parameter(ParameterSetName = 'Sideload', Mandatory = $true)]
    [string]$CertPath,

    [Parameter(ParameterSetName = 'Sideload', Mandatory = $true)]
    [string]$CertPassword,

    [Parameter(ParameterSetName = 'Store')]
    [switch]$Store,

    [Parameter(ParameterSetName = 'Verify')]
    [switch]$Verify,

    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [switch]$AllowPlaceholders
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $repoRoot 'src/ROROROblox.App/ROROROblox.App.csproj'
$logosDir = Join-Path $repoRoot 'src/ROROROblox.App/Package/Logos'
$manifestPath = Join-Path $repoRoot 'src/ROROROblox.App/Package.appxmanifest'
$outDir = Join-Path $repoRoot 'dist'

$requiredLogos = @(
    @{ Name = 'StoreLogo.png';            ExpectedSize = '50x50' },
    @{ Name = 'Square150x150Logo.png';    ExpectedSize = '150x150' },
    @{ Name = 'Square44x44Logo.png';      ExpectedSize = '44x44' },
    @{ Name = 'Wide310x150Logo.png';      ExpectedSize = '310x150' },
    @{ Name = 'Square71x71Logo.png';      ExpectedSize = '71x71' },
    @{ Name = 'Square310x310Logo.png';    ExpectedSize = '310x310' },
    @{ Name = 'SplashScreen.png';         ExpectedSize = '620x300' }
)

function Test-LogosPresent {
    [CmdletBinding()]
    param([switch]$AllowPlaceholders)

    $missing = @()
    $suspicious = @()

    foreach ($logo in $requiredLogos) {
        $path = Join-Path $logosDir $logo.Name
        if (-not (Test-Path $path)) {
            $missing += $logo.Name
            continue
        }

        $size = (Get-Item $path).Length
        if ($size -lt 200) {
            # 200 bytes is below the floor of any honest PNG at any of our sizes.
            $suspicious += "$($logo.Name) (only $size bytes -- looks like a placeholder stub)"
            continue
        }
    }

    if ($missing.Count -gt 0) {
        Write-Host "[logos] MISSING:" -ForegroundColor Red
        $missing | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    }
    if ($suspicious.Count -gt 0) {
        Write-Host "[logos] SUSPICIOUS:" -ForegroundColor Yellow
        $suspicious | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    }

    if (($missing.Count -gt 0 -or $suspicious.Count -gt 0) -and -not $AllowPlaceholders) {
        Write-Host ''
        Write-Host '[logos] FAIL -- produce real assets via the 626labs-design skill before building.' -ForegroundColor Red
        Write-Host '[logos] See src/ROROROblox.App/Package/Logos/README.md for sizes + skill invocation.' -ForegroundColor Red
        Write-Host '[logos] If you really need to bypass (only for early-dev smoke), pass -AllowPlaceholders.' -ForegroundColor Red
        return $false
    }

    if ($AllowPlaceholders -and ($missing.Count -gt 0 -or $suspicious.Count -gt 0)) {
        Write-Host '[logos] WARN -- building with -AllowPlaceholders. DO NOT submit this to the Store.' -ForegroundColor Yellow
    } else {
        Write-Host "[logos] OK -- all $($requiredLogos.Count) required assets present." -ForegroundColor Green
    }
    return $true
}

function Get-MakeAppxPath {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22000.0\x64\makeappx.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    # Fallback: pick the newest 10.0.* version available.
    $sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $sdkRoot) {
        $newest = Get-ChildItem $sdkRoot -Directory -Filter '10.0.*' | Sort-Object Name -Descending | Select-Object -First 1
        if ($newest) {
            $candidate = Join-Path $newest.FullName 'x64\makeappx.exe'
            if (Test-Path $candidate) { return $candidate }
        }
    }
    throw 'makeappx.exe not found. Install the Windows 10/11 SDK (winget install Microsoft.WindowsSDK.10.0.22621).'
}

function Get-SignToolPath {
    $makeAppxPath = Get-MakeAppxPath
    $candidate = Join-Path (Split-Path $makeAppxPath -Parent) 'signtool.exe'
    if (Test-Path $candidate) { return $candidate }
    throw 'signtool.exe not found alongside makeappx.exe.'
}

# === Verify mode: just run the logo check + manifest sanity. ===
if ($Verify) {
    Write-Host '[verify] Checking logo assets...' -ForegroundColor Cyan
    $ok = Test-LogosPresent
    if (-not $ok) { exit 1 }
    Write-Host '[verify] Checking manifest...' -ForegroundColor Cyan
    if (-not (Test-Path $manifestPath)) { Write-Host "[verify] FAIL -- $manifestPath missing." -ForegroundColor Red; exit 1 }
    Write-Host '[verify] OK.' -ForegroundColor Green
    exit 0
}

# === Logo gate. ===
Write-Host '[build-msix] Logo-presence check...' -ForegroundColor Cyan
if (-not (Test-LogosPresent -AllowPlaceholders:$AllowPlaceholders)) { exit 1 }

# === Publish the App. ===
Write-Host "[build-msix] Publishing $appProject ($Configuration / $Runtime)..." -ForegroundColor Cyan
$publishDir = Join-Path $outDir 'publish'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

& "$env:USERPROFILE\.dotnet\dotnet.exe" publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -o $publishDir `
    /p:PublishReadyToRun=true `
    /p:DebugType=None `
    /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { Write-Host '[build-msix] dotnet publish FAILED.' -ForegroundColor Red; exit $LASTEXITCODE }

# === Stage assets for makeappx. ===
$stagingDir = Join-Path $outDir 'staging'
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# Copy publish output (executable + DLLs).
Copy-Item -Path "$publishDir\*" -Destination $stagingDir -Recurse -Force

# Copy manifest + logos into the staging tree at the paths the manifest references.
Copy-Item -Path $manifestPath -Destination (Join-Path $stagingDir 'AppxManifest.xml') -Force
$stagingLogosDir = Join-Path $stagingDir 'Package\Logos'
New-Item -ItemType Directory -Path $stagingLogosDir -Force | Out-Null
Copy-Item -Path "$logosDir\*" -Destination $stagingLogosDir -Force

# === Pack. ===
$flavor = if ($Sideload) { 'Sideload' } else { 'Store' }
$msixPath = Join-Path $outDir "ROROROblox-$flavor.msix"
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }

$makeAppx = Get-MakeAppxPath
Write-Host "[build-msix] Packing $msixPath..." -ForegroundColor Cyan
& $makeAppx pack /v /d $stagingDir /p $msixPath
if ($LASTEXITCODE -ne 0) { Write-Host '[build-msix] makeappx pack FAILED.' -ForegroundColor Red; exit $LASTEXITCODE }

# === Sign (sideload only). ===
if ($Sideload) {
    if (-not (Test-Path $CertPath)) {
        Write-Host "[build-msix] Cert file not found: $CertPath" -ForegroundColor Red
        Write-Host "[build-msix] Generate it via: powershell -ExecutionPolicy Bypass -File scripts/generate-dev-cert.ps1 -Password '...'" -ForegroundColor Red
        exit 1
    }
    $signTool = Get-SignToolPath
    Write-Host "[build-msix] Signing $msixPath..." -ForegroundColor Cyan
    & $signTool sign /v /f $CertPath /p $CertPassword /td sha256 /fd sha256 $msixPath
    if ($LASTEXITCODE -ne 0) { Write-Host '[build-msix] signtool FAILED.' -ForegroundColor Red; exit $LASTEXITCODE }
}

Write-Host ''
Write-Host "[build-msix] DONE: $msixPath" -ForegroundColor Green
if ($Sideload) {
    Write-Host '[build-msix] Sideload distribution: ship the .msix + dev-cert.cer to your testers.' -ForegroundColor Cyan
    Write-Host '[build-msix] Testers import the .cer into Local Machine \\ Trusted People before the install.' -ForegroundColor Cyan
} else {
    Write-Host '[build-msix] Store submission: upload via Partner Center. Re-sign with Store cert is automatic.' -ForegroundColor Cyan
}
