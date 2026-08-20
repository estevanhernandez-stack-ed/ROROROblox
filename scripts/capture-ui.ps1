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
    # Store-listing mode: capture the WINDOW ONLY and composite it onto a flat brand-navy
    # canvas, which is what the shipped assets in docs/store/screenshots actually are -- they
    # look like desktop shots and are not.
    #
    # A REAL desktop capture was tried first and is unshippable from a working machine: the
    # 2026-08-12 attempt caught client and employer-tenant folder names, three real Roblox
    # account names, a dozen unrelated taskbar windows and the Windows Insider evaluation
    # watermark. PrintWindow reads the window's own device context, so none of that is cropped
    # out -- it is never captured.
    [switch]$StoreFrame,
    # 1920x1080 is what the Store checklist calls preferred; 4K is merely accepted. At this size
    # the window composites at NATIVE pixels. The shipped 4K assets upscaled it and lost detail.
    [int]$CanvasWidth  = 1920,
    [int]$CanvasHeight = 1080,
    # Restrict the theme rounds. The four-round loop is the glow campaign's contract -- a Store
    # listing wants one theme per shot, and the theme RANGE is told by -ThemePanel instead.
    [string[]]$Theme,
    # Tile one surface across every captured theme into a single 2x2 image, so the listing can
    # show the theme range in one screenshot rather than spending four carousel slots on it.
    [switch]$ThemePanel,
    # Capture in whatever theme is already active, without driving the picker.
    #
    # Setting a theme means opening Preferences, which needs the main window's Settings button.
    # Some states do not have one: COMPACT MODE hides the whole toolbar, so a theme round-trip
    # cannot run and the capture fails with "no [Button] with name='Settings'". Any transient
    # state -- compact, a modal already up, a mid-flow dialog -- has the same problem, and the
    # round-trip would disturb the very state being photographed even where it works.
    # Pair with -Theme to label the output; it is a LABEL here, not an instruction.
    [switch]$NoThemeSwitch,
    [switch]$DumpUia,
    [switch]$Watch,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

# A typed exception for deny-list hits, so callers can discriminate "this route step is pointed at
# something dangerous" from every other failure with `catch [DenyViolation]` instead of matching
# substrings against exception text. A prose match breaks silently the moment the wording drifts;
# a type cannot.
class DenyViolation : System.Exception {
    DenyViolation([string]$message) : base($message) { }
}

# A typed exception, not a message prefix. Task 4 learned this the hard way: detecting an abort
# condition with -like 'DENIED:*' couples a safety guard to human-readable prose, so a reworded
# message silently disarms it. Catch sites discriminate with catch [SecretExposure].
#
# Declared here alongside DenyViolation, not down in the Secrets region below: PowerShell resolves
# class names in catch filters at parse time, so a class defined below its first catch site would
# not bind.
class SecretExposure : System.Exception {
    SecretExposure([string]$message) : base($message) { }
}

#region Win32 interop

$win32 = @'
using System;
using System.Runtime.InteropServices;

public static class Win
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
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
$script:SW_MINIMIZE = 6
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

function New-ThemePanel {
    # One surface, every theme, tiled 2x2 on the brand navy. The Store listing gets ONE slot
    # that says "four built-in themes including an achromatic one" instead of four slots that
    # each say the same thing about the same window.
    #
    # Composed from the per-theme captures already on disk rather than by re-driving the app:
    # the theme rounds are the expensive part and they have already run.
    param(
        [Parameter(Mandatory)][string[]]$Paths,
        [Parameter(Mandatory)][string[]]$Labels,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height
    )

    if ($Paths.Count -lt 2) { throw "a theme panel needs at least two themes; got $($Paths.Count)." }

    $navy   = [System.Drawing.Color]::FromArgb(255, 15, 31, 49)
    $muted  = [System.Drawing.Color]::FromArgb(255, 154, 168, 184)   # MutedText, brand
    $cols   = 2
    $rows   = [math]::Ceiling($Paths.Count / $cols)
    $gutter = 32
    $label  = 34

    $cellW = [int](($Width  - ($gutter * ($cols + 1))) / $cols)
    $cellH = [int](($Height - ($gutter * ($rows + 1))) / $rows) - $label

    $canvas = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $font = New-Object System.Drawing.Font('Segoe UI', 15, [System.Drawing.FontStyle]::Bold)
    $brush = New-Object System.Drawing.SolidBrush($muted)
    try {
        $g.Clear($navy)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

        for ($i = 0; $i -lt $Paths.Count; $i++) {
            $img = [System.Drawing.Image]::FromFile($Paths[$i])
            try {
                $c = $i % $cols
                $r = [math]::Floor($i / $cols)
                $cx = $gutter + ($c * ($cellW + $gutter))
                $cy = $gutter + ($r * ($cellH + $label + $gutter))

                # Fit inside the cell, preserving aspect. DOWNSCALE ONLY -- blowing a window up
                # to fill a tile is how the old 4K assets lost their crispness.
                $scale = [math]::Min([math]::Min($cellW / $img.Width, $cellH / $img.Height), 1.0)
                $dw = [int]($img.Width * $scale)
                $dh = [int]($img.Height * $scale)
                $dx = $cx + [int](($cellW - $dw) / 2)
                $dy = $cy + [int](($cellH - $dh) / 2)

                $g.DrawImage($img, $dx, $dy, $dw, $dh)
                $g.DrawString($Labels[$i], $font, $brush, $cx, ($cy + $cellH + 6))
            }
            finally { $img.Dispose() }
        }
    }
    finally { $g.Dispose(); $font.Dispose(); $brush.Dispose() }
    return $canvas
}

function New-StoreFrame {
    # Window bitmap onto a flat brand-navy canvas. #0F1F31 is the same navy
    # generate-store-assets.ps1 uses for every logo and tile, so the listing's screenshots and
    # its artwork sit on one ground.
    param(
        [Parameter(Mandatory)][System.Drawing.Bitmap]$Window,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height
    )

    if ($Window.Width -gt $Width -or $Window.Height -gt $Height) {
        throw ("captured window is {0}x{1}, larger than the {2}x{3} canvas. Resize the window or " +
               'raise -CanvasWidth/-CanvasHeight; upscaling a screenshot is not an option here.') -f `
              $Window.Width, $Window.Height, $Width, $Height
    }

    $navy = [System.Drawing.Color]::FromArgb(255, 15, 31, 49)
    $canvas = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $g.Clear($navy)
        $x = [int](($Width  - $Window.Width)  / 2)
        # Slightly above centre: a window sitting dead-centre reads as a floating dialog, and
        # the listing carousel crops from the bottom on narrow viewports.
        $y = [int](($Height - $Window.Height) * 0.42)

        # A soft offset shadow so the window reads as sitting ON the ground rather than pasted
        # against it. Drawn as three decreasing-alpha rectangles; a real blur would need a
        # convolution pass for a difference nobody would see at listing scale.
        for ($i = 3; $i -ge 1; $i--) {
            $a = 18 * $i
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($a, 0, 0, 0))
            $pad = $i * 6
            $g.FillRectangle($brush, $x - $pad, $y - $pad + 4, $Window.Width + ($pad * 2), $Window.Height + ($pad * 2))
            $brush.Dispose()
        }

        $g.DrawImageUnscaled($Window, $x, $y)
    }
    finally { $g.Dispose() }
    return $canvas
}

function Clear-Desktop {
    # Minimise every visible top-level window EXCEPT the app's own.
    #
    # The first version called Shell.MinimizeAll, on the reasoning that it is what Win+D does and
    # therefore behaves the way an operator expects. It minimised RoRoRo too -- and RoRoRo
    # minimises TO TRAY, which zeroes its MainWindowHandle, so the very next step failed with
    # 'no running process with a main window'. The tool cleared the desktop of the thing it was
    # there to photograph.
    param([Parameter(Mandatory)][int]$KeepPid)

    $cb = [Win+EnumProc]{
        param([IntPtr]$hwnd, [IntPtr]$lparam)
        if ([Win]::IsWindowVisible($hwnd)) {
            $wpid = 0
            [Win]::GetWindowThreadProcessId($hwnd, [ref]$wpid) | Out-Null
            if ($wpid -ne $KeepPid) { [Win]::ShowWindow($hwnd, $script:SW_MINIMIZE) | Out-Null }
        }
        return $true
    }
    [Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 700
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
        throw [DenyViolation]::new("DENIED: '$Name' is on the deny list. It stops Roblox clients, deletes accounts, or launches game sessions.")
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
        throw [DenyViolation]::new("DENIED: resolved to '$resolvedName', which is on the deny list. It stops Roblox clients, deletes accounts, or launches game sessions.")
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
    $everResolved = $false
    $lastBoundsKey = $null
    $lastError = $null
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        try {
            $el = Resolve-CaptureTarget -Scope $Scope -Spec $CaptureSpec
            $everResolved = $true
            $r = $el.Current.BoundingRectangle
            $key = '{0},{1},{2},{3}' -f $r.X, $r.Y, $r.Width, $r.Height
            $lastBoundsKey = $key
            if ($key -eq $last -and $r.Width -gt 0 -and $r.Height -gt 0) { return $el }
            $last = $key
        }
        catch [DenyViolation] {
            # A capture target resolving onto a denied control must not be swallowed into a polling
            # retry: the same shape as Close-Surface and Invoke-CaptureRound's catch ordering, so a
            # denied resolution can't hide behind an 8-second timeout that looks like ordinary drift.
            throw
        }
        catch { $lastError = $_.Exception.Message; $last = $null }
        Start-Sleep -Milliseconds 250
    }
    # Two distinct timeout causes, and they mean different things: never-resolved points at the
    # route file or the app not being at the state the caller assumed; resolved-but-never-settled
    # points at something still animating (or genuinely jittering) on screen.
    if (-not $everResolved) {
        throw "capture target did not stabilise within ${TimeoutMs}ms (never resolved: $lastError)"
    }
    throw "capture target did not stabilise within ${TimeoutMs}ms (resolved at least once but never settled; last bounds: $lastBoundsKey)"
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
        try {
            Invoke-Step -Scope $Scope -Step $step
        }
        catch [DenyViolation] {
            # A DENIED failure is different from an ordinary close failure: the route file resolved
            # a close step onto a denied control, which means the route file itself is wrong in a
            # dangerous direction, so that one must not be swallowed -- it stops the run.
            throw
        }
        catch {
            # Every other close step failing must not abort the run; the next surface re-opens from
            # a known state.
            $selector = if ($step.PSObject.Properties.Name -contains 'name') { "name='$($step.name)'" } else { "aid='$($step.aid)'" }
            Write-Warning "close step failed for surface $($Surface.id) ($($Surface.name)) [$($step.do) $selector]: $($_.Exception.Message)"
        }
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

    # Drives the UI. Static resolution cannot tell a renamed button from a closed dialog -- both
    # throw "no element matched" against a Subtree search -- which made this mode exit non-zero on
    # a healthy app while going blind to the exact drift it exists to catch. Opening each surface
    # for real is only what a normal capture run already does; safety comes from the deny list
    # (checked twice inside Resolve-UiaElement, and again as the hard stop in Close-Surface),
    # not from never clicking anything. A DenyViolation is not an ordinary per-surface failure: it
    # means a route resolved onto something that stops Roblox clients or deletes accounts, and it
    # aborts the whole run rather than being recorded and continued past.
    $problems = New-Object System.Collections.Generic.List[string]
    $checked = 0

    foreach ($surface in $Routes.surfaces) {
        if ($surface.PSObject.Properties.Name -contains 'skip') {
            Write-Host ("  SKIP {0,-4} {1}  ({2})" -f $surface.id, $surface.name, $surface.skip)
            continue
        }

        $checked++
        try {
            $el = Open-Surface -Scope $Scope -Surface $surface
            $r = $el.Current.BoundingRectangle
            Write-Host ("  OK   {0,-4} {1}  '{2}' ({3}x{4} at {5},{6})" -f `
                $surface.id, $surface.name, $el.Current.Name, [int]$r.Width, [int]$r.Height, [int]$r.X, [int]$r.Y) -ForegroundColor Green
        }
        catch [DenyViolation] {
            throw
        }
        catch {
            $problems.Add("$($surface.id) $($surface.name): $($_.Exception.Message)")
        }
        finally {
            # Always attempt the close, even after a failed open (deny violation included), so one
            # bad surface cannot leave a dialog on screen to poison every surface that runs after it.
            Close-Surface -Scope $Scope -Surface $surface
        }
    }

    if ($checked -eq 0) { throw 'verify checked zero surfaces; the route file resolved to nothing' }

    if ($problems.Count -gt 0) {
        Write-Host ''
        $problems | ForEach-Object { Write-Host "  FAIL $_" -ForegroundColor Red }
        Write-Host "$($problems.Count) of $checked surfaces failed." -ForegroundColor Red
        return $false
    }
    Write-Host "All $checked surfaces opened and closed cleanly." -ForegroundColor Green
    return $true
}

#endregion

#region Secrets

# Scanned before ANY png is written, on every surface rather than only the Alerts page: the Discord
# page carries a live status line that could render the same URL, and a .ROBLOSECURITY value in a
# PNG is strictly worse than a webhook.
#
# Limit, stated rather than hidden: UIA text is not the rendered pixels. A value could render
# without being exposed to UIA and this would miss it. The checklist's prose warning stays in place
# as backup; this guard does not retire it.
$script:SecretPatterns = @(
    # {20,} floor is safe margin, not a tight fit: real Discord webhook tokens run ~68 characters
    # (WebhookUrlMasker.cs:19-20), so a future editor tightening this number has room before it
    # risks clipping a real token and missing it.
    @{ Kind = 'discord-webhook'; Pattern = 'discord(app)?\.com/api/webhooks/\d+/[A-Za-z0-9_-]{20,}' }
    @{ Kind = 'roblosecurity';   Pattern = '_\|WARNING:-DO-NOT-SHARE-THIS' }
)

function Find-SecretsInText {
    param([string[]]$Texts)
    $found = New-Object System.Collections.Generic.List[psobject]
    foreach ($t in $Texts) {
        if ([string]::IsNullOrEmpty($t)) { continue }
        foreach ($p in $script:SecretPatterns) {
            if ($t -match $p.Pattern) {
                $found.Add([pscustomobject]@{ Kind = $p.Kind; Sample = $t })
            }
        }
    }
    # Comma-wrapped: `return $found.ToArray()` unrolls a zero- or one-element array to a bare value
    # ($null for zero, the single element for one) once it crosses the function's output stream,
    # regardless of the ToArray() type. Under Set-StrictMode that turns "0 hits" into a null
    # reference and ".Count" throws instead of comparing. The unary comma keeps the array a single
    # object on that stream.
    return ,($found.ToArray())
}

function Assert-NoSecrets {
    param([Parameter(Mandatory)]$Element, [Parameter(Mandatory)][string]$SurfaceName)
    $hits = Find-SecretsInText -Texts (Get-SubtreeText -Element $Element)
    if ($hits.Count -gt 0) {
        $kinds = ($hits | ForEach-Object { $_.Kind } | Sort-Object -Unique) -join ', '
        throw [SecretExposure]::new(@"
ABORTED on surface '$SurfaceName': a credential is rendered on screen ($kinds).
Nothing was written. Clear or mask the field, then re-run.
A Discord webhook URL is a bearer credential: the token segment is the entire auth.
"@)
    }
}

#endregion

#region Evidence

function Save-SurfaceCapture {
    param(
        [Parameter(Mandatory)]$Element,
        [Parameter(Mandatory)][string]$Path
    )
    $hwnd = [IntPtr]$Element.Current.NativeWindowHandle
    # What the blank-frame guard should judge is the CAPTURE, not the frame built around it.
    # Store-frame mode composites the window onto a large flat navy canvas, and a sparse dark
    # window -- Diagnostics is mostly background with a little text -- pushes the COMBINED image
    # over the 95% single-colour threshold even though the capture is perfect. The guard refused
    # a good asset rather than writing a bad one, which is the right way for it to be wrong.
    $blankCheckTarget = $null
    if ($script:StoreFrameMode -and $hwnd -ne [IntPtr]::Zero) {
        $shot = Get-WindowBitmap -Hwnd $hwnd
        try {
            if (Test-BlankFrame -Bmp $shot) {
                throw "window capture came back blank ($($shot.Width)x$($shot.Height)); refusing to write it as evidence"
            }
            $bmp = New-StoreFrame -Window $shot -Width $script:CanvasW -Height $script:CanvasH
            $blankCheckTarget = 'already-checked'
        }
        finally { $shot.Dispose() }
    }
    elseif ($hwnd -ne [IntPtr]::Zero) {
        $bmp = Get-WindowBitmap -Hwnd $hwnd
    }
    else {
        # Popups and menus have no window handle of their own. CopyFromScreen reads the composited
        # desktop, so the element must be on top and unobstructed.
        $r = $Element.Current.BoundingRectangle
        $bmp = Get-RegionBitmap -X $r.X -Y $r.Y -W $r.Width -H $r.Height
    }

    try {
        if (-not $blankCheckTarget -and (Test-BlankFrame -Bmp $bmp)) {
            throw "capture came back blank ($($bmp.Width)x$($bmp.Height)); refusing to write it as evidence"
        }
        $dir = Split-Path -Parent $Path
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        return [pscustomobject]@{ Path = $Path; Width = $bmp.Width; Height = $bmp.Height }
    }
    finally { $bmp.Dispose() }
}

function Write-RunManifest {
    param(
        [Parameter(Mandatory)][string]$OutDir,
        [Parameter(Mandatory)][string]$ThemeId,
        [Parameter(Mandatory)]$Results,
        [Parameter(Mandatory)]$Proc
    )
    # Evidence that cannot say which build produced it is how the findings register drifted six
    # rows out of date.
    #
    # DPI comes from GetDpiForWindow, not from comparing pixel dimensions. Once the process is
    # DPI-aware both sides of any such ratio are already physical pixels, so it would report 1.0 on
    # every machine and quietly tell you nothing.
    $dpi = [Win]::GetDpiForWindow([IntPtr]$Proc.MainWindowHandle)
    $manifest = [ordered]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        theme       = $ThemeId
        appVersion  = $Proc.MainModule.FileVersionInfo.FileVersion
        appPath     = $Proc.MainModule.FileName
        dpi         = [int]$dpi
        dpiScale    = [math]::Round(($dpi / 96.0), 3)
        surfaces    = @($Results)
    }
    $path = Join-Path $OutDir "run-$ThemeId.json"
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $path -Encoding utf8
    return $path
}

#endregion

#region Themes

function Open-AppearancePage {
    param([Parameter(Mandatory)]$Scope)
    Resolve-UiaElement -Scope $Scope -Type 'Button' -Name 'Settings' -Verb 'invoke' |
        ForEach-Object { $_.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Milliseconds 800
    Resolve-UiaElement -Scope $Scope -Type 'ListItem' -Name 'Appearance' -Verb 'select' -Within 'SettingsNav' |
        ForEach-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
    Start-Sleep -Milliseconds 500
}

function Close-Preferences {
    param([Parameter(Mandatory)]$Scope)
    try {
        Resolve-UiaElement -Scope $Scope -Type 'Window' -Name 'Settings' -Verb 'close-window' |
            ForEach-Object { $_.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
        Start-Sleep -Milliseconds 400
    }
    catch { Write-Warning "could not close Preferences: $($_.Exception.Message)" }
}

function Get-AvailableThemes {
    param([Parameter(Mandatory)]$Scope)
    # Enumerated at runtime, never hardcoded. The checklist schedules a 'flatline' round for a theme
    # that does not ship; reading the picker means it joins the rotation the day it does.
    $picker = Resolve-UiaElement -Scope $Scope -Type 'ComboBox' -Aid 'ThemePicker' -Verb 'expand'
    $exp = $picker.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $exp.Expand()
    Start-Sleep -Milliseconds 700
    $ids = @()
    try {
        $items = $picker.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem)))
        foreach ($i in $items) {
            # The picker sets DisplayMemberPath="Name", which is visual only, so UIA exposes the
            # Theme record's ToString(). Matching the id substring is stable against that.
            if ($i.Current.Name -match 'Id = ([A-Za-z0-9_-]+),') { $ids += $Matches[1] }
        }
    }
    finally {
        # Structural, not positional: a fault inside FindAll/the match loop must not skip this.
        # A ComboBox left expanded when the Settings window later closes permanently ghosts that
        # window's automation subtree (measured live -- see Set-ActiveTheme).
        $exp.Collapse()
        Start-Sleep -Milliseconds 300
    }
    if ($ids.Count -eq 0) { throw 'ThemePicker exposed no themes; the UIA name format may have changed' }
    return $ids
}

function Set-ActiveTheme {
    param([Parameter(Mandatory)]$Scope, [Parameter(Mandatory)][string]$ThemeId)
    $picker = Resolve-UiaElement -Scope $Scope -Type 'ComboBox' -Aid 'ThemePicker' -Verb 'expand'
    $exp = $picker.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $exp.Expand()
    Start-Sleep -Milliseconds 700
    $found = $false
    try {
        $items = $picker.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem)))
        foreach ($i in $items) {
            if ($i.Current.Name -match "Id = $([regex]::Escape($ThemeId)),") {
                $i.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                Start-Sleep -Milliseconds 900
                $found = $true
                break
            }
        }
    }
    finally {
        # Collapse unconditionally -- found, not-found, or a thrown fault from FindAll/Select() all
        # land here. Measured live: closing the Settings window while this ComboBox's dropdown is
        # still expanded leaves the window's automation subtree as a permanent ghost -- SettingsNav
        # keeps matching it forever after, and every later -Within lookup against the main window's
        # Subtree becomes ambiguous by one more element per leaked cycle. A `finally` closes that
        # off regardless of which path got here, rather than only the success path.
        $exp.Collapse()
        Start-Sleep -Milliseconds 300
    }
    if (-not $found) { throw "theme '$ThemeId' not found in the picker" }
}

#endregion

#region Rounds

function Invoke-CaptureRound {
    param(
        [Parameter(Mandatory)]$Scope,
        [Parameter(Mandatory)]$Routes,
        [Parameter(Mandatory)][string]$ThemeId,
        [Parameter(Mandatory)][string]$OutDir,
        [Parameter(Mandatory)]$Proc,
        [string[]]$Only,
        [switch]$DumpUia
    )
    $results = New-Object System.Collections.Generic.List[psobject]

    foreach ($surface in (Get-CapturedSurfaces -Routes $Routes -Only $Only)) {
        $file = Join-Path $OutDir ("{0}-{1}--{2}.png" -f $surface.id, $surface.name, $ThemeId)
        try {
            $target = Open-Surface -Scope $Scope -Surface $surface

            # Before any bytes are written.
            Assert-NoSecrets -Element $target -SurfaceName $surface.name

            $saved = Save-SurfaceCapture -Element $target -Path $file

            if ($DumpUia) {
                $dump = [IO.Path]::ChangeExtension($file, '.uia.txt')
                Get-SubtreeText -Element $target | Set-Content -Path $dump -Encoding utf8
            }

            $results.Add([pscustomobject]@{
                id = $surface.id; name = $surface.name; status = 'ok'
                theme = $ThemeId
                file = Split-Path -Leaf $saved.Path
                width = $saved.Width; height = $saved.Height
            })
            Write-Host ("  ok   {0,-4} {1}" -f $surface.id, $surface.name) -ForegroundColor Green
        }
        catch [SecretExposure] {
            # A credential on screen aborts the whole run. Never demote this to one failed surface:
            # the next surface would be captured while the same credential is still rendered.
            throw
        }
        catch [DenyViolation] {
            # A step resolving onto a denied control means the route file is wrong in a direction
            # that stops Roblox clients or deletes accounts. Stop, do not drive 12 more surfaces.
            throw
        }
        catch {
            $results.Add([pscustomobject]@{
                id = $surface.id; name = $surface.name; status = 'failed'
                error = $_.Exception.Message
            })
            Write-Host ("  FAIL {0,-4} {1}: {2}" -f $surface.id, $surface.name, $_.Exception.Message) -ForegroundColor Red
        }
        finally {
            Close-Surface -Scope $Scope -Surface $surface
        }
    }
    return $results.ToArray()
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

    # Secret scan: positive cases must be caught, negative cases must not fire.
    $webhook = 'https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789'
    Assert-That ((Find-SecretsInText -Texts @($webhook)).Count -eq 1) 'live webhook URL should be caught'

    $webhookAlt = 'https://discordapp.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789'
    Assert-That ((Find-SecretsInText -Texts @($webhookAlt)).Count -eq 1) 'discordapp.com webhook should be caught'

    $cookie = '_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you'
    Assert-That ((Find-SecretsInText -Texts @($cookie)).Count -eq 1) 'ROBLOSECURITY prefix should be caught'

    # The masked form F-076 ships must NOT trip the scan, or the guard blocks every legitimate run.
    $masked = 'https://discord.com/api/webhooks/123456789012345678/****'
    Assert-That ((Find-SecretsInText -Texts @($masked)).Count -eq 0) 'masked webhook must not trip the scan'

    # The `****` case above is a valid negative, but it is not the string the app actually renders.
    # WebhookUrlMasker.Mask (src/ROROROblox.Core/Discord/WebhookUrlMasker.cs) drops the /api/webhooks/
    # path entirely and renders scheme://host/ + an ellipsis + the trailing 5 characters of the
    # original URL -- this is what a real masked webhook looks like on the Alerts page after F-076's
    # fix. It must produce zero hits too, or the guard fires on every legitimate run the day someone
    # actually opens that page. Built from the same fixture webhook as the positive assertion above,
    # by the documented algorithm, rather than a hand-typed literal, so it can't drift from the real
    # shape. [char]0x2026 rather than a literal ellipsis glyph so a mangled save can't turn it into
    # three periods and silently stop testing anything.
    $ellipsis = [char]0x2026
    $realMasked = 'https://discord.com/' + $ellipsis + $webhook.Substring($webhook.Length - 5)
    Assert-That ((Find-SecretsInText -Texts @($realMasked)).Count -eq 0) `
        'the real on-screen mask (WebhookUrlMasker.Mask shape) must not trip the scan'

    Assert-That ((Find-SecretsInText -Texts @('Alerts & memory', '', 'Idle threshold')).Count -eq 0) `
        'ordinary UI copy must not trip the scan'
    Assert-That ((Find-SecretsInText -Texts @('https://discord.com/api/webhooks/')).Count -eq 0) `
        'a webhook URL with no id or token must not trip the scan'

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
# No desktop sweep is needed: PrintWindow never reads the screen, so nothing behind the app is
# capturable in the first place. That is the whole reason this mode exists.
$appRoot = Get-AppRoot -ProcessId $proc.Id
Write-Host "Attached to $($routes.processName) (pid $($proc.Id))."

if ($Verify) {
    $ok = Invoke-VerifyMode -Scope $appRoot -Routes $routes
    exit ($(if ($ok) { 0 } else { 1 }))
}

if ($Watch) {
    Write-Host 'Watch mode: bring the surface you want on screen, then press Enter. Ctrl+C to stop.'
    $n = 0
    while ($true) {
        $label = Read-Host 'Surface label (blank to quit)'
        if ([string]::IsNullOrWhiteSpace($label)) { break }
        Start-Sleep -Seconds 2

        # Foreground window at the moment of capture, not the app's main-window handle. Every
        # remaining -Watch candidate (the tray menu, future one-offs) is its own top-level HWND
        # once the five Tools destinations became routed surfaces, so MainWindowHandle would
        # reliably capture whatever sits BEHIND the thing the operator is trying to catch.
        $hwnd = [Win]::GetForegroundWindow()
        $fgElement = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        $fgName = $fgElement.Current.Name
        $fgPid = $fgElement.Current.ProcessId
        Write-Host "  foreground window: '$fgName' (pid $fgPid)"

        if ($fgPid -ne $proc.Id) {
            # Same class of mistake as a desktop-root name match once binding a Chrome window in
            # this project: refuse rather than silently screenshot an unrelated app.
            Write-Warning ("foreground window belongs to pid $fgPid, not $($routes.processName) " +
                "(pid $($proc.Id)). Refusing to capture -- bring the RoRoRo surface you want to " +
                "the front, then press Enter again.")
            continue
        }

        # Before any bytes are written. -Watch was exempt from every other capture path's guard --
        # an operator toggling "Show" on the Alerts page and pressing Enter here would otherwise
        # bake the raw webhook into a PNG with nothing to stop it. $fgElement is already resolved
        # above; no separate lookup needed. SecretExposure propagates uncaught -- it must abort the
        # session, not fall back to a warning.
        Assert-NoSecrets -Element $fgElement -SurfaceName "watch:$label"

        $bmp = Get-WindowBitmap -Hwnd $hwnd
        try {
            if (Test-BlankFrame -Bmp $bmp) {
                throw "capture came back blank ($($bmp.Width)x$($bmp.Height)); refusing to write it as evidence"
            }
            $path = Join-Path $OutDir ("watch-{0}-{1}.png" -f (++$n), $label)
            $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "  wrote $path (captured '$fgName')"
        }
        finally { $bmp.Dispose() }
    }
    exit 0
}

$script:StoreFrameMode = [bool]$StoreFrame
$script:CanvasW = $CanvasWidth
$script:CanvasH = $CanvasHeight
if ($StoreFrame) {
    # Default the output somewhere that will not be mistaken for design evidence. The caller can
    # still override -OutDir; this only moves the default.
    if (-not $PSBoundParameters.ContainsKey('OutDir')) {
        $OutDir = Join-Path $PSScriptRoot '..\docs\store\screenshots'
    }
    Write-Host "Store-frame mode: window composited onto ${CanvasWidth}x${CanvasHeight} navy, to $OutDir"
}

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

# Vacuity floor, checked before any app interaction or manifest write. A filter that matches
# nothing (a typo'd -Surface value, for example) must not silently run the theme loop: with zero
# expected surfaces, $ok -lt ($expected * 0.5) is 0 -lt 0, which is false, so the floor further
# down never fires -- the run would exit 0 while cycling the app through every theme and
# Set-Content-ing an empty `surfaces` array over each round's already-good run-<theme>.json.
# Invoke-VerifyMode has had this same guard from the start; the capture path never got it.
$expectedSurfaceCount = @(Get-CapturedSurfaces -Routes $routes -Only $Surface).Count
if ($expectedSurfaceCount -eq 0) {
    $filterDesc = if ($Surface) { "-Surface $($Surface -join ', ')" } else { 'the default filter' }
    throw "no captured surfaces matched $filterDesc; the route file resolved to nothing. Refusing to run the theme loop or touch any manifest."
}

if ($NoThemeSwitch) {
    # Enumerating themes also opens Preferences, so that is skipped too.
    # Comma-wrapped: PowerShell unrolls a single-element array returned from an if-block, so
    # $themes became the STRING "brand" and $themes[0] printed "b" in the message below.
    $themes = if ($Theme) { ,@($Theme) } else { ,@('current') }
    Write-Host "No-theme-switch mode: capturing the active theme as-is, labelled '$($themes[0])'."
}
else {
Open-AppearancePage -Scope $appRoot
$themes = Get-AvailableThemes -Scope $appRoot
if ($Theme) {
    $wanted = @($Theme)
    $missing = $wanted | Where-Object { $_ -notin $themes }
    if ($missing) { throw "theme(s) not available in this build: $($missing -join ', '). Available: $($themes -join ', ')" }
    $themes = $themes | Where-Object { $_ -in $wanted }
}
Close-Preferences -Scope $appRoot
}
Write-Host "Themes: $($themes -join ', ')"

$all = @()
foreach ($themeId in $themes) {
    Write-Host ''
    Write-Host "Round: $themeId" -ForegroundColor Cyan
    if (-not $NoThemeSwitch) {
        Open-AppearancePage -Scope $appRoot
        Set-ActiveTheme -Scope $appRoot -ThemeId $themeId
        Close-Preferences -Scope $appRoot
    }

    $results = Invoke-CaptureRound -Scope $appRoot -Routes $routes -ThemeId $themeId `
                                   -OutDir $OutDir -Proc $proc -Only $Surface -DumpUia:$DumpUia
    $manifest = Write-RunManifest -OutDir $OutDir -ThemeId $themeId -Results $results -Proc $proc
    Write-Host "  manifest: $manifest"
    $all += $results
}

if ($ThemePanel) {
    Write-Host ''
    Write-Host 'Theme panel' -ForegroundColor Cyan
    # One panel per captured surface, from the PNGs the rounds just wrote.
    $bySurface = $all | Where-Object { $_.status -eq 'ok' } | Group-Object name
    foreach ($grp in $bySurface) {
        $ordered = @()
        $labels  = @()
        foreach ($t in $themes) {
            $hit = $grp.Group | Where-Object { $_.theme -eq $t } | Select-Object -First 1
            if ($hit) {
                $full = Join-Path $OutDir $hit.file
                if (Test-Path $full) { $ordered += $full; $labels += $t }
            }
        }
        if ($ordered.Count -lt 2) {
            Write-Warning "  $($grp.Name): only $($ordered.Count) theme capture(s); skipping panel."
            continue
        }
        $panel = New-ThemePanel -Paths $ordered -Labels $labels -Width $CanvasWidth -Height $CanvasHeight
        try {
            $out = Join-Path $OutDir ("{0}--themes.png" -f $grp.Name)
            $panel.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "  wrote $out ($($ordered.Count) themes)"
        }
        finally { $panel.Dispose() }
    }
}

$ok = @($all | Where-Object { $_.status -eq 'ok' }).Count
$failed = @($all | Where-Object { $_.status -eq 'failed' }).Count
# Reuses $expectedSurfaceCount (checked non-zero above) rather than recomputing it, so the two
# can't drift. @() around @($themes): PowerShell unwraps a single-element array on return, and a
# bare .Count on a scalar PSCustomObject is 1 rather than an error, so the floor would silently
# compute from garbage.
$expected = $expectedSurfaceCount * @($themes).Count

Write-Host ''
Write-Host "Captured $ok of $expected, $failed failed."

# Vacuity floor: a run that captured a handful of surfaces must not exit looking like success.
if ($ok -lt ($expected * 0.5)) {
    Write-Host "Captured fewer than half the expected surfaces. Treating the run as failed." -ForegroundColor Red
    exit 1
}
exit ($(if ($failed -gt 0) { 1 } else { 0 }))
