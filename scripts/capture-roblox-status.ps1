<#
.SYNOPSIS
  Saves one dated copy of Roblox's public Status.io answer under the v1.41 probes folder.

.DESCRIPTION
  One public GET, nothing sent but the request: no cookie, no account, no browser spoofing. The user
  agent is RORORO-capture/<app version>, the app's own naming. The body is written byte for byte as
  received. If Roblox is having an incident when this runs, the file is the first real Roblox incident
  answer and joins src/ROROROblox.Tests/Fixtures/RobloxStatus/ (with a PROVENANCE row) by hand.
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\docs\cycles\2026-11-26-roblox-feeds\probes')
)

$ErrorActionPreference = 'Stop'

$url = 'https://api.status.io/1.0/status/59db90dbcdeb2f04dadcf16d'
$csproj = Join-Path $PSScriptRoot '..\src\ROROROblox.App\ROROROblox.App.csproj'
$version = ([xml](Get-Content -LiteralPath $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

$client = [System.Net.Http.HttpClient]::new([System.Net.Http.HttpClientHandler]@{ UseCookies = $false; AllowAutoRedirect = $false })
$client.Timeout = [TimeSpan]::FromSeconds(15)
$client.DefaultRequestHeaders.UserAgent.ParseAdd("RORORO-capture/$version")
try {
    $response = $client.GetAsync($url).GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) { throw "HTTP $([int]$response.StatusCode)" }
    $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
}
finally {
    $client.Dispose()
}

$null = [System.Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json   # throws if the body is not JSON

$stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHHmmZ')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$path = Join-Path (Resolve-Path $OutDir) "status-roblox-capture-$stamp.json"
[System.IO.File]::WriteAllBytes($path, $bytes)
Write-Host "Saved $($bytes.Length) bytes to $path"
