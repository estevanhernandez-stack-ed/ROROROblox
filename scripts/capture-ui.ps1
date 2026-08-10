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
    for ($y = 0; $y -lt $Bmp.Height; $y += 16) {
        for ($x = 0; $x -lt $Bmp.Width; $x += 16) {
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

    # A bitmap that is 94% one colour is under the threshold; 97% is over it.
    foreach ($case in @(@{ Pct = 0.94; Blank = $false }, @{ Pct = 0.97; Blank = $true })) {
        $bmp = New-Object System.Drawing.Bitmap(64, 64)
        $g2 = [System.Drawing.Graphics]::FromImage($bmp)
        $g2.Clear([System.Drawing.Color]::Black); $g2.Dispose()
        # Sampling strides by 16, so 4x4 = 16 sampled pixels. Repaint enough to cross the line.
        # Ceiling(0.94) and Ceiling(0.97) both round to 1 of 16 at this granularity, producing
        # byte-identical bitmaps for both cases -- Round is the fix; it's the closest integer
        # sample count to each percentage instead of always rounding away from the black count.
        $repaint = [math]::Round((1 - $case.Pct) * 16, [MidpointRounding]::AwayFromZero)
        for ($i = 0; $i -lt $repaint; $i++) {
            $bmp.SetPixel(($i % 4) * 16, [math]::Floor($i / 4) * 16, [System.Drawing.Color]::Red)
        }
        Assert-That ((Test-BlankFrame -Bmp $bmp) -eq $case.Blank) "blank threshold wrong at $($case.Pct)"
        $bmp.Dispose()
    }

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

Write-Host 'capture-ui.ps1: route engine lands in a later task.'
