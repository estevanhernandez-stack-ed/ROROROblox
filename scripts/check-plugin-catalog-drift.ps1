<#
.SYNOPSIS
  Fails when docs/store/plugins-catalog.json disagrees with each plugin repo's latest release.

.DESCRIPTION
  For every catalog entry the repo slug comes from installUrl
  (https://github.com/<owner>/<repo>/releases/...). The live version is the tag of the latest
  non-draft, non-prerelease release (leading 'v' stripped). Prints id / catalog / live / status
  and exits 1 on any disagreement or lookup failure, 0 when all match.

  Public repos; in Actions the default GITHUB_TOKEN (GH_TOKEN) is enough.

.PARAMETER Catalog
  Path to the catalog JSON. Defaults to docs/store/plugins-catalog.json in this repo.

.PARAMETER Fixture
  Path to a JSON map of "<owner>/<repo>" to live tag (for example {"owner/repo": "v0.3.0"}).
  When given, gh is not called, so the script can run offline.
#>
[CmdletBinding()]
param(
    [string]$Catalog = (Join-Path $PSScriptRoot '..\docs\store\plugins-catalog.json'),
    [string]$Fixture
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Catalog)) { throw "Catalog not found: $Catalog" }
$entries = Get-Content -LiteralPath $Catalog -Raw -Encoding UTF8 | ConvertFrom-Json

$fixtureMap = $null
if ($Fixture) {
    if (-not (Test-Path -LiteralPath $Fixture)) { throw "Fixture not found: $Fixture" }
    $fixtureMap = Get-Content -LiteralPath $Fixture -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-LiveTag([string]$slug) {
    if ($null -ne $fixtureMap) {
        $prop = $fixtureMap.PSObject.Properties[$slug]
        if ($null -eq $prop) { return $null }
        return [string]$prop.Value
    }
    $json = & gh release list --repo $slug --exclude-drafts --exclude-pre-releases --limit 1 --json tagName 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) { return $null }
    $rows = @($json | ConvertFrom-Json)
    if ($rows.Count -eq 0) { return $null }
    return [string]$rows[0].tagName
}

$rows = foreach ($e in $entries) {
    $slug = $null
    if ($e.installUrl -match 'github\.com/([^/]+/[^/]+)/releases') { $slug = $Matches[1] }

    if (-not $slug) {
        [pscustomobject]@{ Id = $e.id; Catalog = $e.latestVersion; Live = '-'; Status = 'NO-SLUG' }
        continue
    }
    $tag = Get-LiveTag $slug
    if ([string]::IsNullOrWhiteSpace($tag)) {
        [pscustomobject]@{ Id = $e.id; Catalog = $e.latestVersion; Live = '-'; Status = 'NO-RELEASE' }
        continue
    }
    $live = $tag -replace '^v', ''
    $status = if ($live -eq $e.latestVersion) { 'ok' } else { 'DRIFT' }
    [pscustomobject]@{ Id = $e.id; Catalog = $e.latestVersion; Live = $live; Status = $status }
}

$rows | Format-Table -AutoSize | Out-String | Write-Host

$bad = @($rows | Where-Object { $_.Status -ne 'ok' })
if ($bad.Count -gt 0) {
    Write-Host "Plugin catalog drift: $($bad.Count) of $(@($rows).Count) entries disagree with the live release. Fix latestVersion in $Catalog before tagging."
    exit 1
}
Write-Host "Plugin catalog matches live releases ($(@($rows).Count) entries)."
exit 0
