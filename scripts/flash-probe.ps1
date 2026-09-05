# RoRoRo -- measures the white first-composite flash when the shell opens (2026-09-05).
# Drives the main window's Settings button via UIA, then burst-captures the CenterScreen
# 920x640 region (the shell's startup size -- adjust if that changes) at ~12ms intervals and
# reports per-frame average luminance. Navy reads ~25-36; a white flash reads >200. The worst
# frame is saved as a PNG beside -OutDir for eyeballing. This is the instrument that caught
# WS_EX_LAYERED being clobbered by WPF and proved the DWM-cloak fix flat.
param([int]$AppPid, [string]$OutDir)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$ae = [System.Windows.Automation.AutomationElement]
$root = $ae::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition($ae::ProcessIdProperty, $AppPid)
$btn = $null
foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)) {
    $tc = New-Object System.Windows.Automation.PropertyCondition($ae::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $nc = New-Object System.Windows.Automation.PropertyCondition($ae::NameProperty, 'Settings')
    $btn = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.AndCondition($tc, $nc)))
    if ($btn) { break }
}
if (-not $btn) { Write-Output 'FAIL: no Settings button'; exit 1 }

# The shell opens CenterScreen at 920x640 — sample that region of the primary screen.
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$w = 920; $h = 640
$x = $screen.X + [int](($screen.Width - $w) / 2)
$y = $screen.Y + [int](($screen.Height - $h) / 2)

$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

$series = @()
$worstAvg = 0.0; $worstIdx = -1; $worstBmp = $null
for ($i = 0; $i -lt 45; $i++) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $g.Dispose()
    $sum = 0.0; $n = 0
    for ($px = 8; $px -lt $w; $px += 32) {
        for ($py = 8; $py -lt $h; $py += 32) {
            $c = $bmp.GetPixel($px, $py)
            $sum += (0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B)
            $n++
        }
    }
    $avg = [math]::Round($sum / $n, 1)
    $series += $avg
    if ($avg -gt $worstAvg) {
        $worstAvg = $avg; $worstIdx = $i
        if ($worstBmp) { $worstBmp.Dispose() }
        $worstBmp = $bmp
    } else { $bmp.Dispose() }
    Start-Sleep -Milliseconds 12
}
Write-Output ("series: " + ($series -join ' '))
Write-Output ("worst frame #{0}: avg luminance {1} (navy ~25, white ~255)" -f $worstIdx, $worstAvg)
if ($worstBmp) { $worstBmp.Save((Join-Path $OutDir 'flash-worst-frame.png'), [System.Drawing.Imaging.ImageFormat]::Png); $worstBmp.Dispose() }
