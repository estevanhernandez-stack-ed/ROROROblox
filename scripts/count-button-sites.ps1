<#
.SYNOPSIS
    Counts un-migrated button declarations, per the definition in docs/spec.md §6.

.DESCRIPTION
    F-068's site count has been quoted three times under three different definitions and never
    reproduced: the register says 55, a scan during /scope said 72, and an earlier note said "63
    across 15 files" which reproduces at no commit. A row whose number cannot be re-derived cannot
    be used to size work, and this one has been used to size work three times.

    This script IS the definition. From docs/spec.md §6, verbatim:

        An un-migrated button site is an occurrence of `<Button` or `<ui:Button` in a .xaml file
        under src/ROROROblox.App/, excluding obj/ and bin/, whose OPENING TAG does not contain a
        Style="{StaticResource ...ButtonStyle}" or Style="{DynamicResource ...ButtonStyle}"
        reference. A button inside a ControlTemplate counts, because it is still a declaration
        someone maintains.

    Record the output alongside any count written into the findings register, so the next
    re-measure compares like with like.

.EXAMPLE
    pwsh -File scripts/count-button-sites.ps1
    pwsh -File scripts/count-button-sites.ps1 -Quiet   # totals only, for CI or a commit message
#>
[CmdletBinding()]
param(
    [string] $Root = 'src/ROROROblox.App',
    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'

# The opening tag only. Matching the whole element would let a Style set on a CHILD count as
# migrating the parent, which would undercount the debt and flatter the result.
$opener = [regex] '<(?:ui:)?Button\b'
$styled = [regex] 'Style\s*=\s*"\{(?:Static|Dynamic)Resource\s+[A-Za-z0-9_]*ButtonStyle\s*\}"'

if (-not (Test-Path $Root)) { throw "Root '$Root' not found. Run from the repository root." }

$total = 0
$unmigrated = 0
$perFile = [ordered] @{}

Get-ChildItem -Path $Root -Filter *.xaml -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
    Sort-Object FullName |
    ForEach-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw
        if (-not $text) { return }

        $count = 0
        foreach ($m in $opener.Matches($text)) {
            $end = $text.IndexOf('>', $m.Index)
            $head = if ($end -gt 0) { $text.Substring($m.Index, $end - $m.Index + 1) }
                    else { $text.Substring($m.Index) }

            $script:total++
            if (-not $styled.IsMatch($head)) { $count++ }
        }

        if ($count -gt 0) {
            $rel = Resolve-Path -LiteralPath $_.FullName -Relative
            $perFile[$rel.TrimStart('.', [char]0x5C, '/')] = $count
            $script:unmigrated += $count
        }
    }

Write-Output "TOTAL button declarations : $total"
Write-Output "UN-MIGRATED (spec.md §6)  : $unmigrated"
Write-Output "FILES with un-migrated    : $($perFile.Count)"

if (-not $Quiet -and $perFile.Count -gt 0) {
    Write-Output ''
    $perFile.GetEnumerator() |
        Sort-Object -Property Value -Descending |
        ForEach-Object { Write-Output ('{0,4}  {1}' -f $_.Value, $_.Key) }
}

# Non-zero exit when everything is migrated is NOT wanted -- this is a measuring instrument, not a
# gate. The gate is ButtonVocabularyFenceTests, and conflating the two would mean a green build
# depended on a count nobody had agreed was correct yet.
exit 0
