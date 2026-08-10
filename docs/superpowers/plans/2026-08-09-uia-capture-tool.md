# UIA Capture Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the VibeGlow UI capture harness as a committed, re-runnable tool: `scripts/capture-ui.ps1` driving the live app through UI Automation and writing per-theme evidence PNGs.

**Architecture:** One PowerShell script holds the whole tool (resolver, capture, guards, theme loop, reporting), reading route data from `docs/ui-routes.json`. Element resolution requires a control type plus the UIA pattern each verb needs, so a name collision cannot silently bind the wrong element. Capture uses `PrintWindow` with `PW_RENDERFULLCONTENT`, falling back to `CopyFromScreen` only for elements with no window handle.

**Tech Stack:** PowerShell 7.6.3 on .NET 10, `UIAutomationClient` / `UIAutomationTypes`, Win32 P/Invoke via `Add-Type`, `System.Drawing` from PowerShell, xUnit 2.9.3 for the route-file schema test.

## Global Constraints

- **`Add-Type` compiles pure Win32 P/Invoke only.** .NET 10 moved `Bitmap` and `Graphics` behind `System.Private.Windows.GdiPlus` / `System.Private.Windows.Core`. C# naming either type fails with CS0012 no matter what is referenced. The compiled surface returns primitives plus one `RECT` struct; every GDI+ call happens in PowerShell.
- **Build with `dotnet build ROROROblox.slnx`, test with `dotnet test ROROROblox.slnx`.** Never bare `dotnet build`: a gitignored legacy `ROROROblox.sln` stray causes MSB1011.
- **Never assign to `$matches`.** It is the automatic variable populated by `-match`, so a local named `$matches` collides with it. Use `$hits` for match collections. Reading `$Matches[1]` immediately after a successful `-match` is correct and expected.
- **Window lookup is always process-scoped.** A desktop-root match on `Name="Settings"` has been observed binding a Chrome window.
- **`PrintWindow` flag is `2` (`PW_RENDERFULLCONTENT`).** Measured: flags=0 returns 13.3% pure-black pixels on this app.
- **DWM bounds attribute is `9` (`DWMWA_EXTENDED_FRAME_BOUNDS`).** `GetWindowRect` includes the invisible resize border and bakes dead margin into captures.
- **Evidence path:** `docs/ui-evidence/NN-<surface>--<theme>.png`, gitignored at `.gitignore:83`.
- **Commits:** conventional commits (`feat` / `fix` / `docs` / `test` / `chore` / `build`).
- No emoji in code, commits, or output. Em-dashes minimal in comments; commas, periods, colons by default.

## File Structure

| Path | Responsibility |
| --- | --- |
| `docs/ui-routes.json` (rewrite) | Route data: deny list plus one entry per surface. Data only, no logic. |
| `src/ROROROblox.Tests/UiRoutesSchemaTests.cs` (create) | Build-time schema and safety validation of the route file. |
| `scripts/capture-ui.ps1` (create) | The whole tool. Regions: Win32 interop, capture, UIA resolver, route engine, secret scan, theme loop, modes, self-test. |

**Reference implementation:** a verified-working capture core lives at
`C:\Users\estev\AppData\Local\Temp\claude\c--Users-estev-Projects-ROROROblox\104da8d7-df71-420a-b422-107c83bbd45a\scratchpad\test-capture.ps1`.
Read it before Task 2. It already proves the Win32 block, DPI call, frame-bounds call, and both capture paths.

**Running the app.** Several tasks verify against the live app:

```powershell
dotnet build ROROROblox.slnx
Start-Process src/ROROROblox.App/bin/Debug/net10.0-windows/ROROROblox.App.exe
```

The app may open a "Leftover Roblox processes" modal on launch. Click **Continue**. The other two buttons kill running Roblox clients.

---

### Task 1: Route file and schema test

**Files:**
- Modify: `docs/ui-routes.json` (full rewrite)
- Create: `src/ROROROblox.Tests/UiRoutesSchemaTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: the route-file contract every later task reads. Surface object keys: `id`, `name`, optional `skip`, optional `open` (array of steps), optional `capture`, optional `close` (array of steps). Step keys: `do` (one of `invoke`, `select`, `expand`, `close-window`), `type`, exactly one of `name` or `aid`, optional `within`. Capture object keys: `type`, exactly one of `name` or `aid`.

- [ ] **Step 1: Write the route file**

Replace `docs/ui-routes.json` entirely:

```json
{
  "app": "RoRoRo",
  "processName": "ROROROblox.App",
  "_note": "Every step names a control TYPE as well as a name or AutomationId, and the resolver additionally requires the UIA pattern the verb needs. This is not decoration: five elements in this app are named 'Settings' (a Window, a TitleBar, two Texts and the Button), and a name-only FindFirst returns the Window, which carries no InvokePattern. Steps that resolve to zero or to more than one element are errors, never a silent first-match.",
  "deny": [
    "Stop all Roblox instances",
    "Remove",
    "Launch As",
    "Launch multiple",
    "Squad Launch"
  ],
  "surfaces": [
    {
      "id": "01",
      "name": "main-window",
      "capture": { "type": "Window", "name": "RoRoRo" }
    },
    {
      "id": "02",
      "name": "main-window-empty",
      "skip": "Requires zero saved accounts; the live profile has eight. Documented gap, see the design spec section 11."
    },
    {
      "id": "03",
      "name": "preferences",
      "open": [
        { "do": "invoke", "name": "Settings", "type": "Button" },
        { "do": "select", "name": "Startup", "type": "ListItem", "within": "SettingsNav" }
      ],
      "capture": { "type": "Window", "name": "Settings" },
      "close": [ { "do": "close-window", "name": "Settings", "type": "Window" } ]
    },
    {
      "id": "03a",
      "name": "preferences-accounts",
      "open": [
        { "do": "invoke", "name": "Settings", "type": "Button" },
        { "do": "select", "name": "Accounts", "type": "ListItem", "within": "SettingsNav" }
      ],
      "capture": { "type": "Window", "name": "Settings" },
      "close": [ { "do": "close-window", "name": "Settings", "type": "Window" } ]
    },
    {
      "id": "03b",
      "name": "preferences-alerts",
      "open": [
        { "do": "invoke", "name": "Settings", "type": "Button" },
        { "do": "select", "name": "Alerts & memory", "type": "ListItem", "within": "SettingsNav" }
      ],
      "capture": { "type": "Window", "name": "Settings" },
      "close": [ { "do": "close-window", "name": "Settings", "type": "Window" } ]
    },
    {
      "id": "03c",
      "name": "preferences-discord",
      "open": [
        { "do": "invoke", "name": "Settings", "type": "Button" },
        { "do": "select", "name": "Discord", "type": "ListItem", "within": "SettingsNav" }
      ],
      "capture": { "type": "Window", "name": "Settings" },
      "close": [ { "do": "close-window", "name": "Settings", "type": "Window" } ]
    },
    {
      "id": "03d",
      "name": "preferences-appearance",
      "open": [
        { "do": "invoke", "name": "Settings", "type": "Button" },
        { "do": "select", "name": "Appearance", "type": "ListItem", "within": "SettingsNav" }
      ],
      "capture": { "type": "Window", "name": "Settings" },
      "close": [ { "do": "close-window", "name": "Settings", "type": "Window" } ]
    },
    {
      "id": "05",
      "name": "about",
      "open": [
        { "do": "expand", "name": "Tools", "type": "Button" },
        { "do": "invoke", "name": "About", "type": "MenuItem" }
      ],
      "capture": { "type": "Window", "name": "About RoRoRo" },
      "close": [ { "do": "close-window", "name": "About RoRoRo", "type": "Window" } ]
    },
    {
      "id": "06",
      "name": "history",
      "open": [
        { "do": "expand", "name": "Tools", "type": "Button" },
        { "do": "invoke", "name": "History", "type": "MenuItem" }
      ],
      "capture": { "type": "Window", "name": "History" },
      "close": [ { "do": "close-window", "name": "History", "type": "Window" } ]
    },
    {
      "id": "07",
      "name": "diagnostics",
      "open": [
        { "do": "expand", "name": "Tools", "type": "Button" },
        { "do": "invoke", "name": "Diagnostics", "type": "MenuItem" }
      ],
      "capture": { "type": "Window", "name": "Diagnostics" },
      "close": [ { "do": "close-window", "name": "Diagnostics", "type": "Window" } ]
    },
    {
      "id": "08",
      "name": "games",
      "open": [
        { "do": "expand", "name": "Tools", "type": "Button" },
        { "do": "invoke", "name": "Games", "type": "MenuItem" }
      ],
      "capture": { "type": "Window", "name": "Games" },
      "close": [ { "do": "close-window", "name": "Games", "type": "Window" } ]
    },
    {
      "id": "09",
      "name": "plugins",
      "open": [
        { "do": "expand", "name": "Tools", "type": "Button" },
        { "do": "invoke", "name": "Plugins", "type": "MenuItem" }
      ],
      "capture": { "type": "Window", "name": "Plugins" },
      "close": [ { "do": "close-window", "name": "Plugins", "type": "Window" } ]
    },
    {
      "id": "10",
      "name": "theme-builder",
      "open": [
        { "do": "invoke", "name": "Settings", "type": "Button" },
        { "do": "select", "name": "Appearance", "type": "ListItem", "within": "SettingsNav" },
        { "do": "invoke", "aid": "BuildThemeButton", "type": "Button" }
      ],
      "capture": { "type": "Window", "name": "Build a theme" },
      "close": [
        { "do": "close-window", "name": "Build a theme", "type": "Window" },
        { "do": "close-window", "name": "Settings", "type": "Window" }
      ]
    },
    {
      "id": "11",
      "name": "tray-menu",
      "skip": "Path unverified against the live app. NotifyIcon context menu, plausibly its own in-process HWND."
    },
    {
      "id": "20",
      "name": "welcome",
      "open": [
        { "do": "expand", "name": "Tools", "type": "Button" },
        { "do": "invoke", "name": "Welcome tour", "type": "MenuItem" }
      ],
      "capture": { "type": "Window", "name": "Welcome to RoRoRo" },
      "close": [ { "do": "close-window", "name": "Welcome to RoRoRo", "type": "Window" } ]
    },
    {
      "id": "21",
      "name": "squad-launch",
      "skip": "Denied until someone confirms the button opens a dialog rather than launching Roblox clients."
    },
    {
      "id": "22",
      "name": "join-by-link",
      "skip": "Path unverified against the live app."
    },
    {
      "id": "23",
      "name": "export-accounts",
      "skip": "Path unverified against the live app."
    }
  ]
}
```

Note `04` is absent by design: it was retired by glow wave 2 and is not a live surface.

- [ ] **Step 2: Write the failing schema test**

Create `src/ROROROblox.Tests/UiRoutesSchemaTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Validates docs/ui-routes.json as a contract rather than as prose.
/// <para>
/// The route file drives a tool that clicks buttons in the live app against real accounts, so two
/// of these assertions are safety properties, not tidiness: every step must name a control type
/// (five elements in this app share the name "Settings", and a name-only match binds the Window,
/// which carries no InvokePattern), and no route may target a name on the deny list (those names
/// stop Roblox clients, delete accounts, or launch game sessions). Safety properties belong at
/// build time, not discovered while the tool is driving a live app.
/// </para>
/// </summary>
public class UiRoutesSchemaTests
{
    private static readonly string[] KnownVerbs = { "invoke", "select", "expand", "close-window" };

    /// <summary>Control types the PowerShell resolver knows how to map. Keep in lockstep with
    /// the $script:ControlTypes table in scripts/capture-ui.ps1.</summary>
    private static readonly string[] KnownTypes =
        { "Button", "MenuItem", "ListItem", "Window", "ComboBox", "CheckBox", "List", "Text" };

    private static JsonElement LoadRoutes()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "docs", "ui-routes.json");
        Assert.True(File.Exists(path), $"route file missing at {path}");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Surfaces(JsonElement doc) =>
        doc.GetProperty("surfaces").EnumerateArray();

    private static IEnumerable<JsonElement> StepsOf(JsonElement surface)
    {
        foreach (var key in new[] { "open", "close" })
        {
            if (!surface.TryGetProperty(key, out var arr)) continue;
            foreach (var step in arr.EnumerateArray()) yield return step;
        }
    }

    [Fact]
    public void EveryStepNamesATypeAndExactlyOneSelector()
    {
        var doc = LoadRoutes();
        var problems = new List<string>();

        foreach (var surface in Surfaces(doc))
        {
            var id = surface.GetProperty("id").GetString();
            foreach (var step in StepsOf(surface))
            {
                var verb = step.TryGetProperty("do", out var d) ? d.GetString() : null;
                if (verb is null || !KnownVerbs.Contains(verb))
                    problems.Add($"{id}: unknown verb '{verb}'");

                if (!step.TryGetProperty("type", out var t) || string.IsNullOrWhiteSpace(t.GetString()))
                    problems.Add($"{id}: step '{verb}' carries no type");
                else if (!KnownTypes.Contains(t.GetString()))
                    problems.Add($"{id}: step '{verb}' names unknown type '{t.GetString()}'");

                var hasName = step.TryGetProperty("name", out _);
                var hasAid = step.TryGetProperty("aid", out _);
                if (hasName == hasAid)
                    problems.Add($"{id}: step '{verb}' must carry exactly one of name/aid");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void NoRouteTargetsADeniedName()
    {
        var doc = LoadRoutes();
        var deny = doc.GetProperty("deny").EnumerateArray()
            .Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal);

        // Guards the guard: an empty deny list would make this test vacuously pass forever.
        Assert.Contains("Stop all Roblox instances", deny);
        Assert.Contains("Remove", deny);
        Assert.Contains("Launch As", deny);

        var problems = (from surface in Surfaces(doc)
                        let id = surface.GetProperty("id").GetString()
                        from step in StepsOf(surface)
                        where step.TryGetProperty("name", out var n) && deny.Contains(n.GetString()!)
                        select $"{id}: step targets denied name '{step.GetProperty("name").GetString()}'")
                       .ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void SurfaceIdsAreUniqueAndEveryCapturedSurfaceHasATarget()
    {
        var doc = LoadRoutes();
        var ids = Surfaces(doc).Select(s => s.GetProperty("id").GetString()!).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        var problems = new List<string>();
        foreach (var surface in Surfaces(doc))
        {
            var id = surface.GetProperty("id").GetString();
            var skipped = surface.TryGetProperty("skip", out _);
            var hasCapture = surface.TryGetProperty("capture", out var cap);

            if (skipped && hasCapture) problems.Add($"{id}: skipped surfaces must not declare a capture target");
            if (!skipped && !hasCapture) problems.Add($"{id}: captured surface declares no capture target");

            if (hasCapture)
            {
                if (!cap.TryGetProperty("type", out _)) problems.Add($"{id}: capture target carries no type");
                if (cap.TryGetProperty("name", out _) == cap.TryGetProperty("aid", out _))
                    problems.Add($"{id}: capture target must carry exactly one of name/aid");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheFileDescribesTheCampaignItClaimsTo()
    {
        var doc = LoadRoutes();
        var all = Surfaces(doc).ToList();
        var captured = all.Where(s => !s.TryGetProperty("skip", out _)).ToList();

        // Vacuity floor. A file that lost most of its surfaces would otherwise pass every
        // assertion above, since they all quantify over whatever happens to be present.
        Assert.Equal(18, all.Count);
        Assert.True(captured.Count >= 13,
            $"expected at least 13 captured surfaces, found {captured.Count}");

        // 04 was retired by glow wave 2 and must not come back.
        Assert.DoesNotContain(all, s => s.GetProperty("id").GetString() == "04");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~UiRoutesSchemaTests"`
Expected: FAIL. Before Step 1's file is saved they fail on the old format; if Step 1 is already saved they should pass. If they pass immediately, deliberately break one entry (drop a `type` from a step), re-run to watch it fail, then restore it. A schema test that has never been seen failing is not evidence.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~UiRoutesSchemaTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test ROROROblox.slnx`
Expected: the pre-existing count plus 4. Record the number in your report.

- [ ] **Step 6: Commit**

```bash
git add docs/ui-routes.json src/ROROROblox.Tests/UiRoutesSchemaTests.cs
git commit -m "feat(capture): route file format that cannot bind the wrong element"
```

---

### Task 2: Script skeleton, Win32 interop, capture core

**Files:**
- Create: `scripts/capture-ui.ps1`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `Get-WindowBitmap([IntPtr]$Hwnd)` returning `System.Drawing.Bitmap`; `Get-RegionBitmap([double]$X,[double]$Y,[double]$W,[double]$H)` returning `System.Drawing.Bitmap`; `Test-BlankFrame([System.Drawing.Bitmap]$Bmp,[double]$Threshold)` returning `[bool]`; `Get-AppProcess([string]$ProcessName)` returning a `Process`. The `[Win]` type exposes `SetForegroundWindow`, `ShowWindow`, `IsIconic`, `PrintWindow`, `MakeDpiAware`, `FrameBounds`.

Read the reference implementation named in File Structure before starting. It proves this task's Win32 block already.

- [ ] **Step 1: Create the script skeleton**

```powershell
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
```

- [ ] **Step 2: Add the capture functions**

Append:

```powershell
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
```

- [ ] **Step 3: Add the self-test region and entry point**

Append:

```powershell
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
        $repaint = [math]::Ceiling((1 - $case.Pct) * 16)
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
```

- [ ] **Step 4: Run the self-test to verify it passes**

Run: `pwsh -File scripts/capture-ui.ps1 -SelfTest`
Expected: `Self-test passed.` and exit code 0.

If it fails on the threshold cases, that is a real bug in `Test-BlankFrame`, not a bad test. Fix the function.

- [ ] **Step 5: Prove the real capture path against the live app**

Start the app per the File Structure section. Then write this scratch file as
`scratch-capture-check.ps1` in the repo root, run it, and delete it afterwards. Do not commit it.

```powershell
. ./scripts/capture-ui.ps1 -SelfTest:$false 2>$null
$proc = Get-AppProcess -ProcessName 'ROROROblox.App'
Write-Host "pid $($proc.Id), hwnd $($proc.MainWindowHandle)"

$bmp = Get-WindowBitmap -Hwnd ([IntPtr]$proc.MainWindowHandle)
try {
    Write-Host "captured $($bmp.Width)x$($bmp.Height), blank=$(Test-BlankFrame -Bmp $bmp)"
    $bmp.Save("$PWD\scratch-capture-check.png", [System.Drawing.Imaging.ImageFormat]::Png)
}
finally { $bmp.Dispose() }
```

Dot-sourcing runs the script body, which at this task ends with a `Write-Host` placeholder and no
`exit`, so the functions land in scope.

Expected: a nonzero hwnd, dimensions matching the app window, `blank=False`. **Open
`scratch-capture-check.png` and confirm it shows the RoRoRo window.** A capture that is the right
size but the wrong content is exactly the failure this step exists to catch.

Delete `scratch-capture-check.ps1` and `scratch-capture-check.png` before committing.

- [ ] **Step 6: Commit**

```bash
git add scripts/capture-ui.ps1
git commit -m "feat(capture): Win32 interop and capture core with window guards"
```

---

### Task 3: UIA resolver

**Files:**
- Modify: `scripts/capture-ui.ps1`

**Interfaces:**
- Consumes: `[Win]` from Task 2.
- Produces: `Get-AppRoot([int]$ProcessId)` returning the process's main-window `AutomationElement`; `Resolve-UiaElement` with parameters `-Scope`, `-Type`, `-Name`, `-Aid`, `-Verb`, `-Within` returning exactly one `AutomationElement` or throwing; `$script:ControlTypes` and `$script:VerbPattern` hashtables; `Get-SubtreeText($Element)` returning `[string[]]`.

- [ ] **Step 1: Add the lookup tables and root finder**

Insert a `#region UIA` before the self-test region:

```powershell
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
```

- [ ] **Step 2: Add the resolver**

Append inside the UIA region:

```powershell
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
        $searchRoot = $Scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Within)))
        if (-not $searchRoot) { throw "within-scope AutomationId '$Within' not found" }
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
```

Also add near the top of the script, before the UIA region:

```powershell
$script:DenyList = @()   # populated from the route file by the route engine
```

- [ ] **Step 3: Verify the resolver against the live app**

Start the app, then run this scratch check. It asserts the exact bug the design exists to prevent:

```powershell
pwsh -NoProfile -Command {
    . ./scripts/capture-ui.ps1 -SelfTest 2>$null
}
```

That will exit. Instead dot-source in an interactive session with the guard bypassed by running the script's functions directly. Simplest reliable check, run it as its own file `scratch-resolver-check.ps1` (do not commit it):

```powershell
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$p = Get-Process -Name ROROROblox.App
$root = [System.Windows.Automation.AutomationElement]::RootElement
$main = $root.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)))

$byNameOnly = $main.FindAll([System.Windows.Automation.TreeScope]::Subtree,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Settings')))
"name-only matches for 'Settings': $($byNameOnly.Count)"
```

Expected with Preferences closed: at least 2. With Preferences open: 5.

Then confirm the resolver narrows it to 1. Add a temporary `-ResolverCheck` switch to the script that runs:

```powershell
$proc = Get-AppProcess -ProcessName 'ROROROblox.App'
$appRoot = Get-AppRoot -ProcessId $proc.Id
$el = Resolve-UiaElement -Scope $appRoot -Type 'Button' -Name 'Settings' -Verb 'invoke'
Write-Host "resolved to [$($el.Current.ControlType.ProgrammaticName)] '$($el.Current.Name)'"
```

Expected: resolves to exactly one `ControlType.Button`. Remove the temporary switch before committing.

- [ ] **Step 4: Verify the ambiguity guard fires**

With the same temporary switch, resolve `-Type 'Button' -Name 'Remove'` with the deny list empty.
Expected: throws "8 elements matched ... Ambiguous; add a 'within' scope." (the count equals the saved-account count).

This proves the resolver refuses to guess rather than returning a first match.

- [ ] **Step 5: Run the self-test**

Run: `pwsh -File scripts/capture-ui.ps1 -SelfTest`
Expected: `Self-test passed.` Task 2's assertions must still pass.

- [ ] **Step 6: Commit**

```bash
git add scripts/capture-ui.ps1
git commit -m "feat(capture): UIA resolver requiring control type and verb pattern"
```

---

### Task 4: Route engine and -Verify mode

**Files:**
- Modify: `scripts/capture-ui.ps1`

**Interfaces:**
- Consumes: `Resolve-UiaElement`, `Get-AppRoot`, `Get-AppProcess`, `$script:VerbPattern` from Task 3.
- Produces: `Read-Routes([string]$Path)` returning the parsed object and setting `$script:DenyList`; `Invoke-Step($Scope, $Step)`; `Wait-ForStable($Scope, $CaptureSpec, [int]$TimeoutMs)` returning the stabilised `AutomationElement`; `Open-Surface($Scope, $Surface)` returning the capture-target element; `Close-Surface($Scope, $Surface)`; `Invoke-VerifyMode($Scope, $Routes)` returning `[bool]` all-resolved.

- [ ] **Step 1: Add the route engine**

Insert a `#region Routes` after the UIA region:

```powershell
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

#endregion
```

- [ ] **Step 2: Add -Verify mode**

Append inside the Routes region:

```powershell
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
```

Only steps reachable from the current app state resolve. Surfaces behind a closed dialog will fail here, which is expected and is why `-Verify` reports rather than throws on the first failure. Run it with the app at its default state (no dialogs open).

- [ ] **Step 3: Wire the entry point**

Replace the placeholder line at the bottom of the script:

```powershell
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
```

- [ ] **Step 4: Run -Verify against the live app**

Start the app with no dialogs open, then run:

Run: `pwsh -File scripts/capture-ui.ps1 -Verify`
Expected: every step for surfaces `01`, `03`, `03a`-`03d`, `05`-`09`, `10`, `20` resolves; `02`, `11`, `21`, `22`, `23` print as SKIP.

Steps nested behind a dialog (`10`'s `BuildThemeButton`, the rail `select` steps) will fail from the closed state. Record exactly which fail in your report. If ONLY those fail, that is the expected result for this step and the mode is working. If a top-level step such as `Settings` or `Tools` fails, that is a real defect: fix it.

- [ ] **Step 5: Run the self-test and full suite**

Run: `pwsh -File scripts/capture-ui.ps1 -SelfTest`
Expected: `Self-test passed.`

Run: `dotnet test ROROROblox.slnx`
Expected: unchanged from Task 1.

- [ ] **Step 6: Commit**

```bash
git add scripts/capture-ui.ps1
git commit -m "feat(capture): route engine and read-only -Verify mode"
```

---

### Task 5: Secret scan, evidence writing, run manifest

**Files:**
- Modify: `scripts/capture-ui.ps1`

**Interfaces:**
- Consumes: `Get-SubtreeText` (Task 3), `Get-WindowBitmap` / `Get-RegionBitmap` / `Test-BlankFrame` (Task 2), `Open-Surface` / `Close-Surface` (Task 4).
- Produces: `Find-SecretsInText([string[]]$Texts)` returning objects with `Kind` and `Sample`; `Save-SurfaceCapture($Element, [string]$Path)` returning a result object with `Path`, `Width`, `Height`; `Write-RunManifest([string]$OutDir, [string]$ThemeId, $Results, $Proc)`.

- [ ] **Step 1: Write the failing self-test for the secret scan**

Add these assertions inside `Invoke-SelfTest`, before the failure check:

```powershell
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

    Assert-That ((Find-SecretsInText -Texts @('Alerts & memory', '', 'Idle threshold')).Count -eq 0) `
        'ordinary UI copy must not trip the scan'
    Assert-That ((Find-SecretsInText -Texts @('https://discord.com/api/webhooks/')).Count -eq 0) `
        'a webhook URL with no id or token must not trip the scan'
```

- [ ] **Step 2: Run the self-test to verify it fails**

Run: `pwsh -File scripts/capture-ui.ps1 -SelfTest`
Expected: FAIL with `Find-SecretsInText` not recognised as a command.

- [ ] **Step 3: Implement the secret scan**

Insert a `#region Secrets` before the self-test region:

```powershell
#region Secrets

# Scanned before ANY png is written, on every surface rather than only the Alerts page: the Discord
# page carries a live status line that could render the same URL, and a .ROBLOSECURITY value in a
# PNG is strictly worse than a webhook.
#
# Limit, stated rather than hidden: UIA text is not the rendered pixels. A value could render
# without being exposed to UIA and this would miss it. The checklist's prose warning stays in place
# as backup; this guard does not retire it.
$script:SecretPatterns = @(
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
    return $found.ToArray()
}

function Assert-NoSecrets {
    param([Parameter(Mandatory)]$Element, [Parameter(Mandatory)][string]$SurfaceName)
    $hits = Find-SecretsInText -Texts (Get-SubtreeText -Element $Element)
    if ($hits.Count -gt 0) {
        $kinds = ($hits | ForEach-Object { $_.Kind } | Sort-Object -Unique) -join ', '
        throw @"
ABORTED on surface '$SurfaceName': a credential is rendered on screen ($kinds).
Nothing was written. Clear or mask the field, then re-run.
A Discord webhook URL is a bearer credential: the token segment is the entire auth.
"@
    }
}

#endregion
```

- [ ] **Step 4: Run the self-test to verify it passes**

Run: `pwsh -File scripts/capture-ui.ps1 -SelfTest`
Expected: `Self-test passed.`

If the masked-form assertion fails, the token pattern is too loose: `****` must not satisfy `[A-Za-z0-9_-]{20,}`. Fix the pattern, not the assertion.

- [ ] **Step 5: Add capture writing and the run manifest**

Append a new `#region Evidence` to the script, directly after the Secrets region:

```powershell
#region Evidence

function Save-SurfaceCapture {
    param(
        [Parameter(Mandatory)]$Element,
        [Parameter(Mandatory)][string]$Path
    )
    $hwnd = [IntPtr]$Element.Current.NativeWindowHandle
    if ($hwnd -ne [IntPtr]::Zero) {
        $bmp = Get-WindowBitmap -Hwnd $hwnd
    }
    else {
        # Popups and menus have no window handle of their own. CopyFromScreen reads the composited
        # desktop, so the element must be on top and unobstructed.
        $r = $Element.Current.BoundingRectangle
        $bmp = Get-RegionBitmap -X $r.X -Y $r.Y -W $r.Width -H $r.Height
    }

    try {
        if (Test-BlankFrame -Bmp $bmp) {
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
```

`Write-RunManifest` needs no new assembly: `GetDpiForWindow` was added to the `[Win]` block in Task 2.

- [ ] **Step 6: Run the self-test**

Run: `pwsh -File scripts/capture-ui.ps1 -SelfTest`
Expected: `Self-test passed.` The new functions must not break Task 2's or Step 1's assertions.

- [ ] **Step 7: Commit**

```bash
git add scripts/capture-ui.ps1
git commit -m "feat(capture): secret scan, blank-frame refusal, and run manifest"
```

---

### Task 6: Theme rounds, capture loop, remaining modes

**Files:**
- Modify: `scripts/capture-ui.ps1`
- Modify: `docs/ui-routes.json` (only if a skipped surface is verified in Step 5)
- Modify: `docs/ui-capture-checklist.md`

**Interfaces:**
- Consumes: everything from Tasks 2 through 5.
- Produces: `Get-AvailableThemes($Scope)` returning `[string[]]` theme ids; `Set-ActiveTheme($Scope, [string]$ThemeId)`; `Invoke-CaptureRound($Scope, $Routes, [string]$ThemeId, [string]$OutDir, $Proc)` returning result objects; the script's final report and exit code.

- [ ] **Step 1: Add theme enumeration and switching**

Insert a `#region Themes`:

```powershell
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
    $items = $picker.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)))
    $ids = @()
    foreach ($i in $items) {
        # The picker sets DisplayMemberPath="Name", which is visual only, so UIA exposes the Theme
        # record's ToString(). Matching the id substring is stable against that.
        if ($i.Current.Name -match 'Id = ([A-Za-z0-9_-]+),') { $ids += $Matches[1] }
    }
    $exp.Collapse()
    Start-Sleep -Milliseconds 300
    if ($ids.Count -eq 0) { throw 'ThemePicker exposed no themes; the UIA name format may have changed' }
    return $ids
}

function Set-ActiveTheme {
    param([Parameter(Mandatory)]$Scope, [Parameter(Mandatory)][string]$ThemeId)
    $picker = Resolve-UiaElement -Scope $Scope -Type 'ComboBox' -Aid 'ThemePicker' -Verb 'expand'
    $exp = $picker.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $exp.Expand()
    Start-Sleep -Milliseconds 700
    $items = $picker.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)))
    foreach ($i in $items) {
        if ($i.Current.Name -match "Id = $([regex]::Escape($ThemeId)),") {
            $i.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 900
            return
        }
    }
    $exp.Collapse()
    throw "theme '$ThemeId' not found in the picker"
}

#endregion
```

- [ ] **Step 2: Add the capture round**

Insert a `#region Rounds`:

```powershell
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
                file = Split-Path -Leaf $saved.Path
                width = $saved.Width; height = $saved.Height
            })
            Write-Host ("  ok   {0,-4} {1}" -f $surface.id, $surface.name) -ForegroundColor Green
        }
        catch {
            # A secret hit must abort the whole run, not be recorded as one failed surface.
            if ($_.Exception.Message -like 'ABORTED*') { throw }
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
```

- [ ] **Step 3: Wire the entry point**

Replace the placeholder line at the bottom:

```powershell
if ($Watch) {
    Write-Host 'Watch mode: bring the surface you want on screen, then press Enter. Ctrl+C to stop.'
    $n = 0
    while ($true) {
        $label = Read-Host 'Surface label (blank to quit)'
        if ([string]::IsNullOrWhiteSpace($label)) { break }
        Start-Sleep -Seconds 2
        $bmp = Get-WindowBitmap -Hwnd ([IntPtr]$proc.MainWindowHandle)
        try {
            $path = Join-Path $OutDir ("watch-{0}-{1}.png" -f (++$n), $label)
            $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "  wrote $path"
        }
        finally { $bmp.Dispose() }
    }
    exit 0
}

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

Open-AppearancePage -Scope $appRoot
$themes = Get-AvailableThemes -Scope $appRoot
Close-Preferences -Scope $appRoot
Write-Host "Themes: $($themes -join ', ')"

$all = @()
foreach ($themeId in $themes) {
    Write-Host ''
    Write-Host "Round: $themeId" -ForegroundColor Cyan
    Open-AppearancePage -Scope $appRoot
    Set-ActiveTheme -Scope $appRoot -ThemeId $themeId
    Close-Preferences -Scope $appRoot

    $results = Invoke-CaptureRound -Scope $appRoot -Routes $routes -ThemeId $themeId `
                                   -OutDir $OutDir -Proc $proc -Only $Surface -DumpUia:$DumpUia
    $manifest = Write-RunManifest -OutDir $OutDir -ThemeId $themeId -Results $results -Proc $proc
    Write-Host "  manifest: $manifest"
    $all += $results
}

$ok = @($all | Where-Object { $_.status -eq 'ok' }).Count
$failed = @($all | Where-Object { $_.status -eq 'failed' }).Count
# @() around the call: PowerShell unwraps a single-element array on return, and a bare .Count on a
# scalar PSCustomObject is 1 rather than an error, so the floor would silently compute from garbage.
$expected = @(Get-CapturedSurfaces -Routes $routes -Only $Surface).Count * @($themes).Count

Write-Host ''
Write-Host "Captured $ok of $expected, $failed failed."

# Vacuity floor: a run that captured a handful of surfaces must not exit looking like success.
if ($ok -lt ($expected * 0.5)) {
    Write-Host "Captured fewer than half the expected surfaces. Treating the run as failed." -ForegroundColor Red
    exit 1
}
exit ($(if ($failed -gt 0) { 1 } else { 0 }))
```

- [ ] **Step 4: Run a full capture**

Start the app with no dialogs open, then:

Run: `pwsh -File scripts/capture-ui.ps1 -DumpUia`
Expected: two rounds (`brand`, `magenta-heat`), each writing PNGs to `docs/ui-evidence/` plus `run-<theme>.json`.

Open at least three PNGs and confirm they show the surface they name and are not blank. Report the counts and any failures.

If the run aborts with `ABORTED on surface 'preferences-alerts'`, the guard is working: a webhook is rendered unmasked. Clear the webhook fields in Preferences and re-run. Do not weaken the pattern.

- [ ] **Step 5: Resolve the skipped surfaces**

For `11 tray-menu`, `22 join-by-link` and `23 export-accounts`, determine whether a route exists:

```powershell
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$p = Get-Process -Name ROROROblox.App
$root = [System.Windows.Automation.AutomationElement]::RootElement
$wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)))
foreach ($w in $wins) { "'$($w.Current.Name)' class=$($w.Current.ClassName)" }
```

Open the tray menu by hand first, then re-run the snippet: a new top-level window owned by the process means the menu is routable and its `MenuItem`s can be resolved.

For each surface you can route, replace its `skip` entry in `docs/ui-routes.json` with real `open` / `capture` / `close` blocks. For each you cannot, leave the `skip` and update its text with what you found. Do not guess. `21 squad-launch` stays skipped and stays on the deny list unless you confirm the button opens a dialog rather than launching Roblox.

Re-run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~UiRoutesSchemaTests"`
Expected: PASS. If you converted surfaces, the captured-count assertion still holds since it is a floor.

- [ ] **Step 6: Correct the checklist**

`docs/ui-capture-checklist.md` states three constraints the design proved false. Leave the prose history intact and add a correction block directly under the title, in the repo's banner-correct style:

```markdown
> **Corrected 2026-08-09 by measurement against the running app.** Three constraints below are
> false and the capture tool does not honour them.
>
> - "A popup is not in the main window's automation subtree even while open" is false. All eight
>   Tools items resolve from the main window. The five Tools destinations are routed, not watched.
> - "The rail's pages cannot be routed" is false. They expose `SelectionItemPattern`, and
>   `Select()` routes them. The claim conflated "carries no InvokePattern" with "cannot be routed".
> - The `flatline` round cannot run: the app ships `brand`, `midnight` and `magenta-heat`, and
>   `flatline` does not exist. The tool enumerates themes at runtime, so the round appears when the
>   theme does.
>
> The webhook warning below stands unchanged. `scripts/capture-ui.ps1` also refuses mechanically,
> but UIA text is not the rendered pixels, so the warning is backup rather than obsolete.
```

- [ ] **Step 7: Run everything**

Run: `pwsh -File scripts/capture-ui.ps1 -SelfTest`
Expected: `Self-test passed.`

Run: `pwsh -File scripts/capture-ui.ps1 -Verify`
Expected: reports resolution status; exit 0 if all reachable steps resolve.

Run: `dotnet test ROROROblox.slnx`
Expected: the Task 1 count, unchanged.

- [ ] **Step 8: Confirm no evidence is staged**

```bash
git status --short
git check-ignore -v docs/ui-evidence/run-brand.json
```

Expected: no `docs/ui-evidence/` entries in `git status`; `check-ignore` confirms the rule at `.gitignore:83`. PNGs and manifests of a live profile must never be committed.

- [ ] **Step 9: Commit**

```bash
git add scripts/capture-ui.ps1 docs/ui-routes.json docs/ui-capture-checklist.md
git commit -m "feat(capture): theme rounds, capture loop, and watch mode"
```

---

## Self-Review

**Spec coverage.** Every numbered section of the design maps to a task: §4 route format and §9's schema test to Task 1; §2.6 and §5's window guards, DPI, and blank-frame detection to Task 2; §2.4 and §4's resolver rules to Task 3; §5's wait-for-stable and §9's `-Verify` to Task 4; §6's secret scan and §7's run manifest to Task 5; §7's theme rounds, §8's failure handling and vacuity floor, and §11's unverified surfaces to Task 6. §11's `02` gap is encoded as a `skip` entry in Task 1 with the reason inline.

**Two known weak points, flagged rather than smoothed over.**

Task 2 Step 5 is awkward. Verifying the real capture path needs a running app and a dot-sourced script whose entry point exits, so the step degrades to confirming the process is findable and defers the true end-to-end proof to Task 6 Step 4. An implementer should not treat that step's thinness as permission to skip verification; Task 6 Step 4 is where the capture path is actually proven, and it is a hard gate.

Task 3 Steps 3 and 4 ask for a temporary `-ResolverCheck` switch that is added and then removed. That is deliberate, because the resolver's contract (exactly one match, never a first match) cannot be proven without driving the live tree, and there is no PowerShell test framework in this repo worth adding for it. The step says to remove the switch before committing. If a reviewer sees `-ResolverCheck` in the committed script, that is a defect.

**Corrected during self-review.** Two defects were found in this plan's own code and fixed inline:

- The run manifest computed `dpiScale` by dividing the UIA root's width by the primary screen's width. Once `SetProcessDpiAwarenessContext` has run, both are physical pixels, so it would report `1.0` on every machine while looking like a real measurement. Replaced with `GetDpiForWindow`, added to the `[Win]` block in Task 2.
- The vacuity floor called `.Count` on `Get-CapturedSurfaces` directly. PowerShell unwraps a single-element array on return, and `.Count` on a scalar `PSCustomObject` is `1` rather than an error, so a filtered run could compute its floor from a garbage denominator without failing. Wrapped in `@()`.

**Deferred to implementation, by design.** The routes for surfaces `11`, `22` and `23` cannot be written from a desk: their paths were never verified, and the plan explicitly forbids guessing. Task 6 Step 5 resolves them against the live app and says what to do in either outcome.
