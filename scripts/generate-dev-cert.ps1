# ROROROblox -- generate a self-signed sideload code-signing certificate.
# Output: dev-cert.pfx (gitignored) + dev-cert.cer (the public half, for distribution to clan
# testers who need to trust it on their machines).
#
# Run from repo root:
#   powershell -ExecutionPolicy Bypass -File scripts/generate-dev-cert.ps1 -Password 'pick-a-password'
#
# This cert is for SIDELOAD ONLY. The Microsoft Store path uses a different cert issued by
# Partner Center -- never reuse this key for Store-bound builds.

param(
    [Parameter(Mandatory = $true)]
    [string]$Password,

    [string]$Subject = 'CN=Estevan Hernandez',

    [string]$OutDir,

    [int]$ValidityYears = 3
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot isn't populated when used in param defaults on some PS5 invocations,
# so resolve OutDir here instead. Defaults to the repo root (scripts/..).
if ([string]::IsNullOrEmpty($OutDir)) {
    $OutDir = Split-Path -Parent $PSScriptRoot
}

$pfxPath = Join-Path $OutDir 'dev-cert.pfx'
$cerPath = Join-Path $OutDir 'dev-cert.cer'

if (Test-Path $pfxPath) {
    Write-Host "[cert] $pfxPath already exists -- refusing to overwrite. Delete it first if you really want a new cert." -ForegroundColor Yellow
    exit 1
}

Write-Host "[cert] Generating self-signed cert for $Subject (valid $ValidityYears year(s))..." -ForegroundColor Cyan

$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -FriendlyName 'ROROROblox sideload' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') `
    -NotAfter (Get-Date).AddYears($ValidityYears)

$securePassword = ConvertTo-SecureString -String $Password -Force -AsPlainText

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

# Remove from local cert store now that we have the .pfx -- keeps the Personal store tidy.
Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "[cert] Wrote $pfxPath (private; gitignored)" -ForegroundColor Green
Write-Host "[cert] Wrote $cerPath (public; safe to distribute)" -ForegroundColor Green
Write-Host "[cert] Thumbprint: $($cert.Thumbprint)"
Write-Host ''
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Build sideload MSIX:  powershell -ExecutionPolicy Bypass -File scripts/build-msix.ps1 -Sideload -CertPath dev-cert.pfx -CertPassword '$Password'"
Write-Host "  2. Distribute dev-cert.cer to anyone who'll install the sideload -- they import it"
Write-Host "     into Local Machine \\ Trusted People before the MSIX install will succeed."
