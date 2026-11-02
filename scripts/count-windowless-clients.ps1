<#
.SYNOPSIS
  Counts windowless RobloxPlayerBeta processes (the #222 measurement) and appends one row to the
  per-cycle smoke doc.

.DESCRIPTION
  Read-only against the system: it enumerates processes and never signals, stops or opens one for
  anything but reading Id and StartTime. It writes exactly one thing, a row appended to the smoke
  doc's #222 table. It prints and records a timestamp, the total client count, the windowless count
  and the process id plus StartTime of each windowless process. No account identity, command line,
  window title or cookie is read or written.

  Run it at the end of a day that had about ten launches. Spec section 7.3 reads the result.

.PARAMETER SmokeDoc
  Path of the smoke doc to append to. Defaults to docs/store/smoke-2026-11-01-account-library.md
  relative to this script's repo.
#>
[CmdletBinding()]
param(
    [string]$SmokeDoc = (Join-Path $PSScriptRoot '..\docs\store\smoke-2026-11-01-account-library.md')
)

$ErrorActionPreference = 'Stop'

$all = @(Get-Process -Name RobloxPlayerBeta -ErrorAction SilentlyContinue)
$windowless = @($all | Where-Object { $_.MainWindowHandle -eq 0 } | Select-Object Id, StartTime)

$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm zzz'
$detail = if ($windowless.Count -eq 0) {
    'none'
} else {
    ($windowless | ForEach-Object { 'pid {0} since {1:HH:mm:ss}' -f $_.Id, $_.StartTime }) -join '; '
}

$row = '| {0} | {1} | {2} | {3} |' -f $stamp, $all.Count, $windowless.Count, $detail

Write-Host "RobloxPlayerBeta total: $($all.Count)  windowless: $($windowless.Count)"
$windowless | ForEach-Object { Write-Host ('  pid {0}  started {1:yyyy-MM-dd HH:mm:ss}' -f $_.Id, $_.StartTime) }

if (-not (Test-Path -LiteralPath $SmokeDoc)) {
    throw "Smoke doc not found: $SmokeDoc"
}

Add-Content -LiteralPath $SmokeDoc -Value $row -Encoding utf8
Write-Host "Appended a row to $(Resolve-Path -LiteralPath $SmokeDoc)"
