#Requires -Version 7.0
<#
  Captures the store surfaces in every language WITHOUT restarting the app.

  Why not -Language: that path restarts, and a restart while real clients are running raises the
  leftover-processes dialog every single time -- which is correct app behaviour (a windowed client
  must be asked about) and fatal to an unattended sweep. v1.27 switches language live, so the
  picker is the right lever. It is also the feature whose own hint used to say "next time you open
  RoRoRo", which is how we found that copy was stale.

  Picker entries are NATIVE names (Français, Deutsch, Русский) and are literals in
  UiCulture.Candidates, so they do not move with the current UI culture.
#>
param(
    [string[]]$Cultures = @('en','fr','de','ru','pt-BR','es'),
    [string]$Theme = 'brand'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$NATIVE = @{ 'en'='English'; 'fr'='Français'; 'de'='Deutsch'; 'ru'='Русский'
             'pt-BR'='Português (Brasil)'; 'pl'='Polski'; 'es'='Español' }
$SURFACES = @('01','05','08','07','06','09','10')
$engine = Join-Path $PSScriptRoot 'capture-ui.ps1'

function Get-Root {
    $p = Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $p) { throw 'RoRoRo is not running with a main window.' }
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
    ,@($r.FindAll([System.Windows.Automation.TreeScope]::Children, $c))
}

function Find-ByAid {
    param($Scopes, [string]$Aid, [string]$Type)
    $ct = switch ($Type) {
        'Button'   { [System.Windows.Automation.ControlType]::Button }
        'ListItem' { [System.Windows.Automation.ControlType]::ListItem }
        'ComboBox' { [System.Windows.Automation.ControlType]::ComboBox }
        'Window'   { [System.Windows.Automation.ControlType]::Window }
    }
    $cond = New-Object System.Windows.Automation.AndCondition(@(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Aid)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct))))
    foreach ($s in $Scopes) {
        $hit = $s.FindFirst([System.Windows.Automation.TreeScope]::Subtree, $cond)
        if ($hit) { return $hit }
    }
    $null
}

function Switch-Language {
    param([string]$Culture)
    $native = $NATIVE[$Culture]

    $settings = Find-ByAid (Get-Root) 'ToolbarSettings' 'Button'
    if (-not $settings) { throw "toolbar Settings button not found (is this the retrofit build?)" }
    $settings.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2

    $nav = Find-ByAid (Get-Root) 'NavAppearance' 'ListItem'
    if (-not $nav) { throw 'Appearance nav item not found' }
    $nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 1

    $picker = Find-ByAid (Get-Root) 'LanguagePicker' 'ComboBox'
    if (-not $picker) { throw 'LanguagePicker not found' }
    $picker.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 700

    # The items' UIA Name is CultureOption's record ToString(), not its DisplayName:
    #   "CultureOption { CultureName = pl, DisplayName = Polski }"
    # DisplayMemberPath sets the VISIBLE text but not the automation name, so a screen reader
    # reads the record dump too. Filed as a separate accessibility fix; matched here on the
    # DisplayName fragment so the sweep is not blocked on it.
    $li = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $item = @($picker.FindAll([System.Windows.Automation.TreeScope]::Subtree, $li)) |
            Where-Object { $_.Current.Name -like "*DisplayName = $native*" } | Select-Object -First 1
    if (-not $item) { throw "language '$native' not offered by the picker" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 2

    # Close the shell so the capture starts from the main window, same as a route would.
    $shell = Find-ByAid (Get-Root) 'ShellWindow' 'Window'
    if ($shell) {
        $wp = $shell.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        $wp.Close(); Start-Sleep -Seconds 2
    }

    # Prove it took, rather than trusting the click.
    $probe = Find-ByAid (Get-Root) 'ToolbarSettings' 'Button'
    $label = $probe.Current.Name
    Write-Host "  language now: Settings button reads '$label'" -ForegroundColor Green
    if ($Culture -ne 'en' -and $label -eq 'Settings') { throw "still English after selecting '$native'" }
    if ($Culture -eq 'en' -and $label -ne 'Settings') { throw "expected English, got '$label'" }
}

foreach ($c in $Cultures) {
    Write-Host "`n=== $c ===" -ForegroundColor Cyan
    Switch-Language -Culture $c
    $out = Join-Path $PSScriptRoot "..\docs\store\screenshots\$c"
    & $engine -StoreFrame -Theme $Theme -OutDir $out -Surface $SURFACES
}
Write-Host "`nsweep complete." -ForegroundColor Cyan
