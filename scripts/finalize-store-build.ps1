# ROROROblox -- Microsoft Store final-mile build.
#
# Wraps scripts/build-msix.ps1 -Store with the Partner Center reservation values inlined
# into Package.appxmanifest in one shot. Saves you hand-editing XML attributes and getting
# a build rejection on identity mismatch.
#
# Run from repo root with the four values Partner Center gave you:
#
#   powershell -ExecutionPolicy Bypass -File scripts/finalize-store-build.ps1 `
#       -IdentityName       "626Labs.ROROROblox" `
#       -PublisherCN        "CN=YOUR-RESERVATION-CN" `
#       -PublisherDisplayName "626 Labs LLC" `
#       -Version            "1.1.0.0"
#
# What it does:
#   1. Reads src/ROROROblox.App/Package.appxmanifest, updates the four identity-related
#      attributes in place (with safety preview), writes back.
#   2. Runs scripts/build-msix.ps1 -Store -- unsigned package; Partner Center signs after
#      upload. Logo gate runs; build aborts if any required asset is missing.
#   3. Reports the .msix path. That's the file to upload at Partner Center.
#
# Pass -RestoreManifest to roll back the manifest after the build (useful for keeping the
# committed file as a placeholder while iterating with different reservation values during
# resubmission cycles).

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$IdentityName,

    [Parameter(Mandatory = $true)]
    [string]$PublisherCN,

    [Parameter(Mandatory = $true)]
    [string]$PublisherDisplayName,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$RepoRoot,
    [switch]$RestoreManifest,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# Resolve repo root in the BODY -- $PSScriptRoot in a param() default doesn't populate
# reliably under Windows PowerShell 5.1 (the default `powershell.exe`). pwsh 7 handles it
# fine but we don't get to assume the invocation shell.
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

# Sanity-check the inputs before we touch the manifest.
if ($IdentityName -notmatch '^[A-Za-z0-9.\-]+$') {
    throw "IdentityName '$IdentityName' looks malformed. Expected something like '626Labs.ROROROblox' (alnum + dots + dashes only)."
}
if ($PublisherCN -notmatch '^CN=') {
    throw "PublisherCN must start with 'CN='. Got: $PublisherCN"
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must be four-part (e.g. '1.1.0.0'). Got: $Version"
}

$manifestPath = Join-Path $RepoRoot 'src\ROROROblox.App\Package.appxmanifest'
if (-not (Test-Path $manifestPath)) {
    throw "Manifest not found at $manifestPath"
}

# Snapshot the manifest so -RestoreManifest can roll back later.
$backupPath = Join-Path $env:TEMP "ROROROblox.Package.appxmanifest.$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
Copy-Item $manifestPath $backupPath -Force
Write-Host "[finalize] Backed up manifest to $backupPath" -ForegroundColor Gray

# Load and patch.
[xml]$manifest = Get-Content $manifestPath -Raw

$identityNode = $manifest.Package.Identity
$beforeName = $identityNode.Name
$beforePublisher = $identityNode.Publisher
$beforeVersion = $identityNode.Version
$identityNode.Name = $IdentityName
$identityNode.Publisher = $PublisherCN
$identityNode.Version = $Version

$propertiesNode = $manifest.Package.Properties
$beforeDisplay = $propertiesNode.PublisherDisplayName
$propertiesNode.PublisherDisplayName = $PublisherDisplayName

Write-Host ''
Write-Host '[finalize] Identity diff:' -ForegroundColor Cyan
Write-Host "    Name                   : $beforeName -> $IdentityName"
Write-Host "    Publisher              : $beforePublisher -> $PublisherCN"
Write-Host "    Version                : $beforeVersion -> $Version"
Write-Host "    PublisherDisplayName   : $beforeDisplay -> $PublisherDisplayName"
Write-Host ''

if ($DryRun) {
    Write-Host '[finalize] -DryRun specified; manifest NOT written, build NOT run.' -ForegroundColor Yellow
    exit 0
}

# UTF-8 BOM-less write to match the existing manifest encoding (System.Xml's Save method
# writes BOM by default; we reformat to match).
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$writerSettings = New-Object System.Xml.XmlWriterSettings
$writerSettings.Indent = $true
$writerSettings.IndentChars = '    '
$writerSettings.Encoding = $utf8NoBom
$writerSettings.OmitXmlDeclaration = $false
$writer = [System.Xml.XmlWriter]::Create($manifestPath, $writerSettings)
try { $manifest.Save($writer) } finally { $writer.Close() }
Write-Host '[finalize] Manifest patched.' -ForegroundColor Green
Write-Host ''

# Run the Store build (unsigned -- Partner Center signs after upload).
$buildScript = Join-Path $RepoRoot 'scripts\build-msix.ps1'
Write-Host '[finalize] Running scripts/build-msix.ps1 -Store...' -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File $buildScript -Store
$buildExit = $LASTEXITCODE

# Optional rollback so the committed manifest stays as a placeholder.
if ($RestoreManifest) {
    Copy-Item $backupPath $manifestPath -Force
    Write-Host "[finalize] Manifest rolled back to pre-finalize state (backup: $backupPath)" -ForegroundColor Yellow
}

if ($buildExit -ne 0) {
    Write-Host "[finalize] Build failed (exit $buildExit). Manifest backup at: $backupPath" -ForegroundColor Red
    exit $buildExit
}

# Report the artefact.
$msixPath = Join-Path $RepoRoot 'dist\RORORO-Store.msix'
if (Test-Path $msixPath) {
    $info = Get-Item $msixPath
    Write-Host ''
    Write-Host '[finalize] DONE.' -ForegroundColor Green
    Write-Host "[finalize] MSIX: $($info.FullName) ($([Math]::Round($info.Length / 1MB, 2)) MB)" -ForegroundColor Green
    Write-Host '[finalize] Upload this file to Partner Center.' -ForegroundColor Green
    Write-Host ''
    Write-Host '[finalize] Reminder: also paste from docs/store/listing-copy.md, fill the' -ForegroundColor Cyan
    Write-Host '[finalize] age-rating questionnaire from docs/store/age-rating.md, point the' -ForegroundColor Cyan
    Write-Host '[finalize] privacy URL at the GitHub Pages site, and upload the screenshots.' -ForegroundColor Cyan
} else {
    Write-Host "[finalize] Build reported success but $msixPath does not exist. Investigate." -ForegroundColor Red
    exit 1
}
