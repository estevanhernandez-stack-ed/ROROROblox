<#
.SYNOPSIS
  Fails when docs/store/plugins-catalog.json disagrees with each plugin repo's latest release.

.DESCRIPTION
  For every catalog entry the repo slug comes from installUrl
  (https://github.com/<owner>/<repo>/releases/...). Two live versions are read from the latest
  non-draft, non-prerelease release:

    Tag      - the release tag, leading 'v' stripped.
    Manifest - the "version" inside that release's manifest.json asset.

  Both must equal the catalog's latestVersion. The manifest is the one that matters most: it is
  what RoRoRo's MarketplacePlan compares against latestVersion, and it ships inside plugin.zip.
  Checking only the tag let rororo-ur-mcp v0.4.0 through on 2026-10-23 with a 0.3.0 manifest,
  which would have offered direct-download hosts an update that could never clear.

  Prints id / catalog / tag / manifest / status and exits 1 on any disagreement or lookup
  failure, 0 when all match. Runs under Windows PowerShell 5.1 and PowerShell 7.

  Public repos; in Actions the default GITHUB_TOKEN (GH_TOKEN) is enough.

.PARAMETER Catalog
  Path to the catalog JSON. Defaults to docs/store/plugins-catalog.json in this repo.

.PARAMETER Fixture
  Path to a JSON map of "<owner>/<repo>" to the live release, so the script runs offline and
  gh is not called. Each value is either a tag string ({"owner/repo": "v0.3.0"}, manifest
  assumed to match) or an object ({"owner/repo": {"tag": "v0.4.0", "manifest": "0.3.0"}}).
#>
[CmdletBinding()]
param(
    [string]$Catalog,
    [string]$Fixture
)

$ErrorActionPreference = 'Stop'

# Resolved here rather than in the param default: Windows PowerShell 5.1 leaves $PSScriptRoot
# empty while evaluating param defaults, which made this script crash before it read anything.
if (-not $Catalog) {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    $Catalog = Join-Path $here '..\docs\store\plugins-catalog.json'
}

if (-not (Test-Path -LiteralPath $Catalog)) { throw "Catalog not found: $Catalog" }
$entries = Get-Content -LiteralPath $Catalog -Raw -Encoding UTF8 | ConvertFrom-Json

$fixtureMap = $null
if ($Fixture) {
    if (-not (Test-Path -LiteralPath $Fixture)) { throw "Fixture not found: $Fixture" }
    $fixtureMap = Get-Content -LiteralPath $Fixture -Raw -Encoding UTF8 | ConvertFrom-Json
}

# Returns @{ Tag = 'v0.4.0'; Manifest = '0.4.0' } or $null. Manifest is $null when the release
# has no readable manifest.json.
function Get-LiveRelease([string]$slug) {
    if ($null -ne $fixtureMap) {
        $prop = $fixtureMap.PSObject.Properties[$slug]
        if ($null -eq $prop) { return $null }
        $v = $prop.Value
        if ($v -is [string]) { return @{ Tag = $v; Manifest = ($v -replace '^v', '') } }
        return @{ Tag = [string]$v.tag; Manifest = [string]$v.manifest }
    }

    $json = & gh release list --repo $slug --exclude-drafts --exclude-pre-releases --limit 1 --json tagName 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) { return $null }
    $rows = @($json | ConvertFrom-Json)
    if ($rows.Count -eq 0) { return $null }
    $tag = [string]$rows[0].tagName

    $manifest = $null
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("catalog-drift-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        & gh release download $tag --repo $slug --pattern 'manifest.json' --dir $tmp 2>$null | Out-Null
        $mf = Join-Path $tmp 'manifest.json'
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $mf)) {
            $manifest = [string]((Get-Content -LiteralPath $mf -Raw -Encoding UTF8 | ConvertFrom-Json).version)
        }
    }
    finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
    return @{ Tag = $tag; Manifest = $manifest }
}

$rows = foreach ($e in $entries) {
    $slug = $null
    if ($e.installUrl -match 'github\.com/([^/]+/[^/]+)/releases') { $slug = $Matches[1] }

    if (-not $slug) {
        [pscustomobject]@{ Id = $e.id; Catalog = $e.latestVersion; Tag = '-'; Manifest = '-'; Status = 'NO-SLUG' }
        continue
    }
    $live = Get-LiveRelease $slug
    if ($null -eq $live -or [string]::IsNullOrWhiteSpace($live.Tag)) {
        [pscustomobject]@{ Id = $e.id; Catalog = $e.latestVersion; Tag = '-'; Manifest = '-'; Status = 'NO-RELEASE' }
        continue
    }
    $tagVersion = $live.Tag -replace '^v', ''
    $status =
        if ([string]::IsNullOrWhiteSpace($live.Manifest)) { 'NO-MANIFEST' }
        elseif ($tagVersion -ne $e.latestVersion)        { 'DRIFT' }
        elseif ($live.Manifest -ne $e.latestVersion)     { 'MANIFEST-DRIFT' }
        else                                             { 'ok' }
    $shownManifest = if ($live.Manifest) { $live.Manifest } else { '-' }
    [pscustomobject]@{ Id = $e.id; Catalog = $e.latestVersion; Tag = $tagVersion; Manifest = $shownManifest; Status = $status }
}

$rows | Format-Table -AutoSize | Out-String | Write-Host

$bad = @($rows | Where-Object { $_.Status -ne 'ok' })
if ($bad.Count -gt 0) {
    Write-Host "Plugin catalog drift: $($bad.Count) of $(@($rows).Count) entries disagree with the live release."
    Write-Host "DRIFT: fix latestVersion in $Catalog. MANIFEST-DRIFT: the plugin's release shipped a manifest.json whose version differs from its tag; re-release the plugin. NO-MANIFEST: the release has no readable manifest.json."
    exit 1
}
Write-Host "Plugin catalog matches live releases, tags and manifests ($(@($rows).Count) entries)."
exit 0
