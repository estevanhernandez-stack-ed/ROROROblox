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
# Negative lookahead on '.' excludes PROPERTY-ELEMENT syntax -- <Button.Style>,
# <Button.ToolTip>, <Button.ContextMenu> -- which  happily matched. Nine of those exist,
# all in MainWindow.xaml, and counting them inflated this script's first baseline from 63
# to 72. A scanner that miscounts by a fixed amount is worse than one that fails: it is
# reproducible, so it looks right.
$opener = [regex] '<(?:ui:)?Button(?![.\w])'
$styled = [regex] 'Style\s*=\s*"\{(?:Static|Dynamic)Resource\s+[A-Za-z0-9_]*ButtonStyle\s*\}"'

# A button can also take a rank through a PROPERTY-ELEMENT style:
#     <Button ...>
#       <Button.Style>
#         <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
# Five buttons in MainWindow do exactly that, because they need local visibility
# triggers AND a rank, and XAML forbids setting Style twice. They ARE migrated, and
# an opening-tag-only scan called them offenders -- the count went UP when they were
# fixed, which is the clearest possible sign a definition is wrong.
#
# Note this is not the child-Style hazard the comment above warns about: <Button.Style>
# is a property of the button itself, not a nested control.
$basedOn = [regex] 'BasedOn\s*=\s*"\{(?:Static|Dynamic)Resource\s+[A-Za-z0-9_]*ButtonStyle\s*\}"'

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
            if ($styled.IsMatch($head)) { continue }

            # A local keyed style that itself BasedOn's a rank counts as migrated. GamesWindow's
            # InverseBoolToVisibilityStyle is exactly that: it adds visibility triggers on top of
            # SecondaryButtonStyle, and the two buttons using it were migrated before this cycle
            # started. Reading only the resource NAME called them offenders.
            $named = [regex]::Match($head, 'Style\s*=\s*"\{(?:Static|Dynamic)Resource\s+([A-Za-z0-9_]+)\s*\}"')
            if ($named.Success) {
                $localKey = $named.Groups[1].Value
                $decl = [regex]::Match($text, "<Style[^>]*x:Key=""$([regex]::Escape($localKey))""[^>]*>")
                if ($decl.Success -and $basedOn.IsMatch($decl.Value)) { continue }
            }

            # Look just past the opening tag for a <Button.Style> carrying a BasedOn to a rank.
            # 600 chars covers the property element and its <Style ...> line with room to spare.
            # Windows are generous on purpose. The first pass used 240/400 and miscounted
            # MainWindow's follow chip by one: its <Button.Style> sits 217 chars past the opening
            # tag and its BasedOn a further 300 past that, behind an explanatory comment. A
            # too-tight window does not fail, it just quietly reports an extra offender.
            $lookahead = $text.Substring($end + 1, [Math]::Min(1200, $text.Length - $end - 1))
            $bs = $lookahead.IndexOf('<Button.Style>')
            if ($bs -ge 0 -and $bs -lt 400 -and $basedOn.IsMatch($lookahead.Substring($bs, [Math]::Min(800, $lookahead.Length - $bs)))) {
                continue
            }

            $count++
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
