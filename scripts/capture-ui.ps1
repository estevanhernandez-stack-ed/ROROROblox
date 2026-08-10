<#
.SYNOPSIS
    Captures RoRoRo's UI surfaces to per-theme evidence PNGs, driven by docs/ui-routes.json.

.DESCRIPTION
    Rebuild of the VibeGlow capture harness. The original was never committed, so every wave's
    evidence was produced by something nobody could run again.

    Element resolution requires a control TYPE and the UIA pattern each verb needs, because five
    elements in this app are named "Settings" and a name-only match binds the wrong one.

.NOTES
    .NET 10 moved Bitmap and Graphics behind System.Private.Windows.GdiPlus, so C# compiled through
    Add-Type cannot name them (CS0012). The compiled surface below is pure Win32 P/Invoke; every
    GDI+ call is made from PowerShell, where types resolve at runtime instead of compile time.
#>
[CmdletBinding()]
param(
    [string]$RoutesPath = (Join-Path $PSScriptRoot '..\docs\ui-routes.json'),
    [string]$OutDir     = (Join-Path $PSScriptRoot '..\docs\ui-evidence'),
    [string[]]$Surface,
    [switch]$Verify,
    [switch]$DumpUia,
    [switch]$Watch,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

#region Win32 interop

$win32 = @'
using System;
using System.Runtime.InteropServices;

public static class Win
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>PER_MONITOR_AWARE_V2. Without it a non-aware host gets virtualized coordinates
    /// and captures land cropped or scaled on a scaled display.</summary>
    public static void MakeDpiAware()
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
    }

    /// <summary>DWMWA_EXTENDED_FRAME_BOUNDS. GetWindowRect includes the invisible resize border
    /// on Win10+ and would bake dead margin into every capture.</summary>
    public static RECT FrameBounds(IntPtr hwnd)
    {
        RECT r;
        DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT)));
        return r;
    }
}
'@
Add-Type -TypeDefinition $win32

#endregion

#region Capture

$script:SW_RESTORE = 9
$script:PW_RENDERFULLCONTENT = 2

function Get-AppProcess {
    param([Parameter(Mandatory)][string]$ProcessName)
    $procs = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
               Where-Object { $_.MainWindowHandle -ne 0 })
    if ($procs.Count -eq 0) { throw "no running '$ProcessName' process with a main window. Start the app first." }
    if ($procs.Count -gt 1) { throw "$($procs.Count) '$ProcessName' processes are running; cannot choose. Close all but one." }
    return $procs[0]
}

function Assert-Capturable {
    param([Parameter(Mandatory)][IntPtr]$Hwnd)
    if ([Win]::IsIconic($Hwnd)) {
        [Win]::ShowWindow($Hwnd, $script:SW_RESTORE) | Out-Null
        Start-Sleep -Milliseconds 600
        if ([Win]::IsIconic($Hwnd)) { throw 'window stayed minimized after SW_RESTORE' }
    }
    $r = [Win]::FrameBounds($Hwnd)
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    # A minimized window reports the -32000 sentinel and captures as a black strip.
    if ($r.Left -le -30000 -or $w -le 0 -or $h -le 0) {
        throw "window is not in a capturable state (${w}x${h} at $($r.Left),$($r.Top))"
    }
    return $r
}

function Get-WindowBitmap {
    param([Parameter(Mandatory)][IntPtr]$Hwnd)
    $r = Assert-Capturable -Hwnd $Hwnd
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top

    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    try { $ok = [Win]::PrintWindow($Hwnd, $hdc, $script:PW_RENDERFULLCONTENT) }
    finally { $g.ReleaseHdc($hdc); $g.Dispose() }

    if (-not $ok) { $bmp.Dispose(); throw 'PrintWindow returned false' }
    return $bmp
}

function Get-RegionBitmap {
    param(
        [Parameter(Mandatory)][double]$X,
        [Parameter(Mandatory)][double]$Y,
        [Parameter(Mandatory)][double]$W,
        [Parameter(Mandatory)][double]$H
    )
    $iw = [int]$W; $ih = [int]$H
    if ($iw -le 0 -or $ih -le 0) { throw "region is empty (${iw}x${ih})" }
    $bmp = New-Object System.Drawing.Bitmap($iw, $ih)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try { $g.CopyFromScreen([int]$X, [int]$Y, 0, 0, (New-Object System.Drawing.Size($iw, $ih))) }
    finally { $g.Dispose() }
    return $bmp
}

function Test-BlankFrame {
    param(
        [Parameter(Mandatory)][System.Drawing.Bitmap]$Bmp,
        [double]$Threshold = 0.95
    )
    $counts = @{}
    $total = 0
    # A fixed stride silently returns "blank" for anything smaller than the stride in both
    # dimensions, because only the (0,0) sample gets taken: $total=1, $max=1, ratio 1.0 regardless
    # of content. Get-RegionBitmap is the fallback capture path for popups and menus, which can be
    # exactly that small, so the stride adapts to bitmap size instead: target ~32 samples per
    # axis, never below 1, so every pixel of a sub-stride bitmap gets examined.
    $strideX = [math]::Max(1, [int][math]::Floor($Bmp.Width / 32))
    $strideY = [math]::Max(1, [int][math]::Floor($Bmp.Height / 32))
    for ($y = 0; $y -lt $Bmp.Height; $y += $strideY) {
        for ($x = 0; $x -lt $Bmp.Width; $x += $strideX) {
            $argb = $Bmp.GetPixel($x, $y).ToArgb()
            if ($counts.ContainsKey($argb)) { $counts[$argb]++ } else { $counts[$argb] = 1 }
            $total++
        }
    }
    if ($total -eq 0) { return $true }
    $max = ($counts.Values | Measure-Object -Maximum).Maximum
    return (($max / $total) -ge $Threshold)
}

#endregion

$script:DenyList = @()   # populated from the route file by the route engine

#region UIA

# Keep in lockstep with KnownTypes in src/ROROROblox.Tests/UiRoutesSchemaTests.cs.
$script:ControlTypes = @{
    'Button'   = [System.Windows.Automation.ControlType]::Button
    'MenuItem' = [System.Windows.Automation.ControlType]::MenuItem
    'ListItem' = [System.Windows.Automation.ControlType]::ListItem
    'Window'   = [System.Windows.Automation.ControlType]::Window
    'ComboBox' = [System.Windows.Automation.ControlType]::ComboBox
    'CheckBox' = [System.Windows.Automation.ControlType]::CheckBox
    'List'     = [System.Windows.Automation.ControlType]::List
    'Text'     = [System.Windows.Automation.ControlType]::Text
}

# The pattern each verb needs. Requiring it during resolution is what makes a name collision
# resolvable: of the five elements named "Settings", only the Button carries InvokePattern.
$script:VerbPattern = @{
    'invoke'       = [System.Windows.Automation.InvokePattern]::Pattern
    'select'       = [System.Windows.Automation.SelectionItemPattern]::Pattern
    'expand'       = [System.Windows.Automation.ExpandCollapsePattern]::Pattern
    'close-window' = [System.Windows.Automation.WindowPattern]::Pattern
}

function Get-AppRoot {
    param([Parameter(Mandatory)][int]$ProcessId)
    # Process-scoped, always. A desktop-root match on Name="Settings" has been observed binding a
    # Chrome window instead of this app's.
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId)
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if (-not $el) { throw "no top-level window owned by process $ProcessId" }
    return $el
}

function Resolve-UiaElement {
    param(
        [Parameter(Mandatory)]$Scope,
        [Parameter(Mandatory)][string]$Type,
        [string]$Name,
        [string]$Aid,
        [string]$Verb,
        [string]$Within
    )

    if ($Name -and $script:DenyList -contains $Name) {
        throw "DENIED: '$Name' is on the deny list. It stops Roblox clients, deletes accounts, or launches game sessions."
    }
    if (-not $script:ControlTypes.ContainsKey($Type)) { throw "unknown control type '$Type'" }
    if ([string]::IsNullOrEmpty($Name) -eq [string]::IsNullOrEmpty($Aid)) {
        throw "specify exactly one of -Name or -Aid (got name='$Name' aid='$Aid')"
    }

    $searchRoot = $Scope
    if ($Within) {
        # FindAll, not FindFirst: a mis-scoped -Within silently searches the wrong ancestor's
        # subtree and can still return a clean single hit from the wrong part of the tree, which
        # is worse than failing outright. Same exactly-one contract as the element match below.
        $ancestorHits = $Scope.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Within)))
        if ($ancestorHits.Count -eq 0) { throw "within-scope AutomationId '$Within' not found" }
        if ($ancestorHits.Count -gt 1) {
            throw "$($ancestorHits.Count) elements matched within-scope AutomationId '$Within'. Ambiguous; use a more specific scope."
        }
        $searchRoot = $ancestorHits[0]
    }

    $conds = New-Object System.Collections.Generic.List[System.Windows.Automation.Condition]
    $conds.Add((New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $script:ControlTypes[$Type])))
    if ($Name) {
        $conds.Add((New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $Name)))
    }
    if ($Aid) {
        $conds.Add((New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Aid)))
    }
    $cond = New-Object System.Windows.Automation.AndCondition($conds.ToArray())

    # Subtree, not Descendants: the main window must be resolvable as its own capture target.
    $found = $searchRoot.FindAll([System.Windows.Automation.TreeScope]::Subtree, $cond)

    $required = $null
    if ($Verb) {
        if (-not $script:VerbPattern.ContainsKey($Verb)) { throw "unknown verb '$Verb'" }
        $required = $script:VerbPattern[$Verb]
    }

    # NOTE: not $matches. That is the automatic variable populated by -match.
    $hits = @()
    foreach ($e in $found) {
        if ($required) {
            $p = $null
            if (-not $e.TryGetCurrentPattern($required, [ref]$p)) { continue }
        }
        $hits += $e
    }

    $label = if ($Name) { "name='$Name'" } else { "aid='$Aid'" }
    if ($hits.Count -eq 0) {
        throw "no [$Type] with $label$(if ($Verb) { " supporting '$Verb'" })"
    }
    if ($hits.Count -gt 1) {
        throw "$($hits.Count) elements matched [$Type] $label$(if ($Verb) { " supporting '$Verb'" }). Ambiguous; add a 'within' scope."
    }

    # Deny again, on the RESOLVED element's own name. The pre-check above only sees the name the
    # caller asked for, so a step selecting a denied control by AutomationId would walk straight
    # past it. Checking after resolution closes that regardless of which selector was used.
    $resolvedName = $hits[0].Current.Name
    if ($resolvedName -and $script:DenyList -contains $resolvedName) {
        throw "DENIED: resolved to '$resolvedName', which is on the deny list. It stops Roblox clients, deletes accounts, or launches game sessions."
    }

    return $hits[0]
}

function Get-SubtreeText {
    param([Parameter(Mandatory)]$Element)
    $texts = New-Object System.Collections.Generic.List[string]
    $texts.Add($Element.Current.Name)
    $all = $Element.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
        $texts.Add($e.Current.Name)
        $vp = $null
        if ($e.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) {
            $texts.Add($vp.Current.Value)
        }
    }
    return $texts.ToArray()
}

#endregion

#region Routes

function Read-Routes {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path $Path)) { throw "route file not found at $Path" }
    $routes = Get-Content -Raw -Path $Path | ConvertFrom-Json
    $script:DenyList = @($routes.deny)
    if ($script:DenyList.Count -eq 0) { throw 'route file declares an empty deny list' }
    return $routes
}

function Invoke-Step {
    param([Parameter(Mandatory)]$Scope, [Parameter(Mandatory)]$Step)

    $name = if ($Step.PSObject.Properties.Name -contains 'name') { $Step.name } else { $null }
    $aid  = if ($Step.PSObject.Properties.Name -contains 'aid')  { $Step.aid }  else { $null }
    $within = if ($Step.PSObject.Properties.Name -contains 'within') { $Step.within } else { $null }

    $el = Resolve-UiaElement -Scope $Scope -Type $Step.type -Name $name -Aid $aid `
                             -Verb $Step.do -Within $within

    switch ($Step.do) {
        'invoke' {
            $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        }
        'select' {
            $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        }
        'expand' {
            $el.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        }
        'close-window' {
            $el.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        }
        default { throw "unknown verb '$($Step.do)'" }
    }
    Start-Sleep -Milliseconds 250
}

function Resolve-CaptureTarget {
    param([Parameter(Mandatory)]$Scope, [Parameter(Mandatory)]$Spec)
    $name = if ($Spec.PSObject.Properties.Name -contains 'name') { $Spec.name } else { $null }
    $aid  = if ($Spec.PSObject.Properties.Name -contains 'aid')  { $Spec.aid }  else { $null }
    return Resolve-UiaElement -Scope $Scope -Type $Spec.type -Name $name -Aid $aid
}

function Wait-ForStable {
    param(
        [Parameter(Mandatory)]$Scope,
        [Parameter(Mandatory)]$CaptureSpec,
        [int]$TimeoutMs = 8000
    )
    # Poll until the target exists AND its bounds are unchanged across two reads. Fixed sleeps are
    # how a harness like this becomes flaky on a slower machine.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $last = $null
    $lastError = 'never resolved'
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        try {
            $el = Resolve-CaptureTarget -Scope $Scope -Spec $CaptureSpec
            $r = $el.Current.BoundingRectangle
            $key = '{0},{1},{2},{3}' -f $r.X, $r.Y, $r.Width, $r.Height
            if ($key -eq $last -and $r.Width -gt 0) { return $el }
            $last = $key
        }
        catch { $lastError = $_.Exception.Message; $last = $null }
        Start-Sleep -Milliseconds 250
    }
    throw "capture target did not stabilise within ${TimeoutMs}ms (last: $lastError)"
}

function Open-Surface {
    param([Parameter(Mandatory)]$Scope, [Parameter(Mandatory)]$Surface)
    if ($Surface.PSObject.Properties.Name -contains 'open') {
        foreach ($step in $Surface.open) { Invoke-Step -Scope $Scope -Step $step }
    }
    return (Wait-ForStable -Scope $Scope -CaptureSpec $Surface.capture)
}

function Close-Surface {
    param([Parameter(Mandatory)]$Scope, [Parameter(Mandatory)]$Surface)
    if ($Surface.PSObject.Properties.Name -notcontains 'close') { return }
    foreach ($step in $Surface.close) {
        # A close step failing must not abort the run; the next surface re-opens from a known state.
        try { Invoke-Step -Scope $Scope -Step $step }
        catch { Write-Warning "close step failed for $($Surface.name): $($_.Exception.Message)" }
    }
}

function Get-CapturedSurfaces {
    param([Parameter(Mandatory)]$Routes, [string[]]$Only)
    $list = @($Routes.surfaces | Where-Object { $_.PSObject.Properties.Name -notcontains 'skip' })
    if ($Only) { $list = @($list | Where-Object { $Only -contains $_.name -or $Only -contains $_.id }) }
    return $list
}

function Invoke-VerifyMode {
    param([Parameter(Mandatory)]$Scope, [Parameter(Mandatory)]$Routes)

    # Read-only. Resolves every step and every capture target without invoking anything, so a copy
    # change fails here instead of silently capturing the wrong window.
    $problems = New-Object System.Collections.Generic.List[string]
    $checked = 0

    foreach ($surface in $Routes.surfaces) {
        if ($surface.PSObject.Properties.Name -contains 'skip') {
            Write-Host ("  SKIP {0,-4} {1}  ({2})" -f $surface.id, $surface.name, $surface.skip)
            continue
        }
        foreach ($key in 'open', 'close') {
            if ($surface.PSObject.Properties.Name -notcontains $key) { continue }
            foreach ($step in $surface.$key) {
                $checked++
                $name = if ($step.PSObject.Properties.Name -contains 'name') { $step.name } else { $null }
                $aid  = if ($step.PSObject.Properties.Name -contains 'aid')  { $step.aid }  else { $null }
                $within = if ($step.PSObject.Properties.Name -contains 'within') { $step.within } else { $null }
                try {
                    Resolve-UiaElement -Scope $Scope -Type $step.type -Name $name -Aid $aid `
                                       -Verb $step.do -Within $within | Out-Null
                }
                catch {
                    $problems.Add("$($surface.id) [$key] $($step.do): $($_.Exception.Message)")
                }
            }
        }
    }

    if ($checked -eq 0) { throw 'verify checked zero steps; the route file resolved to nothing' }

    if ($problems.Count -gt 0) {
        Write-Host ''
        $problems | ForEach-Object { Write-Host "  FAIL $_" -ForegroundColor Red }
        Write-Host "$($problems.Count) of $checked steps failed to resolve." -ForegroundColor Red
        return $false
    }
    Write-Host "All $checked steps resolved." -ForegroundColor Green
    return $true
}

#endregion

#region Self-test

function Invoke-SelfTest {
    $failures = New-Object System.Collections.Generic.List[string]

    function Assert-That {
        param([bool]$Condition, [string]$Message)
        if (-not $Condition) { $failures.Add($Message) }
    }

    # Blank-frame detection: a uniform bitmap is blank, a varied one is not.
    $blank = New-Object System.Drawing.Bitmap(64, 64)
    $g = [System.Drawing.Graphics]::FromImage($blank)
    $g.Clear([System.Drawing.Color]::Black); $g.Dispose()
    Assert-That (Test-BlankFrame -Bmp $blank) 'all-black bitmap should be detected as blank'
    $blank.Dispose()

    $varied = New-Object System.Drawing.Bitmap(64, 64)
    for ($y = 0; $y -lt 64; $y++) {
        for ($x = 0; $x -lt 64; $x++) {
            $varied.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, ($x * 4) % 256, ($y * 4) % 256, 128))
        }
    }
    Assert-That (-not (Test-BlankFrame -Bmp $varied)) 'gradient bitmap should not be detected as blank'
    $varied.Dispose()

    # A bitmap that is 94% one colour is under the threshold; 97% is over it. Derive the sample
    # count from the same adaptive-stride formula Test-BlankFrame uses, rather than hardcoding 16:
    # for a 64x64 bitmap that is stride 2 (floor(64/32)), so 32x32 = 1024 samples. At that
    # resolution 3% is ~31 sampled pixels -- genuinely expressible, and genuinely distinct between
    # the two cases -- instead of both collapsing to the same single repainted pixel (round 1's
    # bug) or the 97% case collapsing to 100% black (round 1's fix, still a granularity trap).
    foreach ($case in @(@{ Pct = 0.94; Blank = $false }, @{ Pct = 0.97; Blank = $true })) {
        $w = 64; $h = 64
        $bmp = New-Object System.Drawing.Bitmap($w, $h)
        $g2 = [System.Drawing.Graphics]::FromImage($bmp)
        $g2.Clear([System.Drawing.Color]::Black); $g2.Dispose()

        $strideX = [math]::Max(1, [int][math]::Floor($w / 32))
        $strideY = [math]::Max(1, [int][math]::Floor($h / 32))
        $samplesPerRow = [math]::Ceiling($w / $strideX)
        $samples = $samplesPerRow * [math]::Ceiling($h / $strideY)

        $repaint = [math]::Round((1 - $case.Pct) * $samples, [MidpointRounding]::AwayFromZero)
        for ($i = 0; $i -lt $repaint; $i++) {
            $sx = ($i % $samplesPerRow) * $strideX
            $sy = [math]::Floor($i / $samplesPerRow) * $strideY
            $bmp.SetPixel($sx, $sy, [System.Drawing.Color]::Red)
        }
        Assert-That ((Test-BlankFrame -Bmp $bmp) -eq $case.Blank) "blank threshold wrong at $($case.Pct)"
        $bmp.Dispose()
    }

    # Regression case: a fixed 16px stride only ever samples (0,0) for a bitmap smaller than the
    # stride in both dimensions, so it reports "blank" unconditionally regardless of content.
    # Get-RegionBitmap is the fallback path for popups and menus, which can be this small, so an
    # 8x8 bitmap that is half black / half red -- nowhere near uniform -- must NOT be blank.
    $small = New-Object System.Drawing.Bitmap(8, 8)
    for ($y = 0; $y -lt 8; $y++) {
        for ($x = 0; $x -lt 8; $x++) {
            $color = if ($x -lt 4) { [System.Drawing.Color]::Black } else { [System.Drawing.Color]::Red }
            $small.SetPixel($x, $y, $color)
        }
    }
    Assert-That (-not (Test-BlankFrame -Bmp $small)) 'half-black/half-red 8x8 bitmap should not be detected as blank (sub-stride regression)'
    $small.Dispose()

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
        Write-Host "$($failures.Count) self-test failure(s)." -ForegroundColor Red
        exit 1
    }
    Write-Host 'Self-test passed.' -ForegroundColor Green
    exit 0
}

#endregion

[Win]::MakeDpiAware()

if ($SelfTest) { Invoke-SelfTest }

$routes = Read-Routes -Path $RoutesPath
$proc = Get-AppProcess -ProcessName $routes.processName
$appRoot = Get-AppRoot -ProcessId $proc.Id
Write-Host "Attached to $($routes.processName) (pid $($proc.Id))."

if ($Verify) {
    $ok = Invoke-VerifyMode -Scope $appRoot -Routes $routes
    exit ($(if ($ok) { 0 } else { 1 }))
}

Write-Host 'capture-ui.ps1: capture loop lands in a later task.'
