# Plugin In-Session Start Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a user installs and consents to a plugin, RoRoRo starts the plugin process immediately in the current session — no RoRoRo restart required. The per-row "Autostart" toggle then governs only whether the plugin *also* launches on future RoRoRo starts.

**Architecture:** Today `PluginProcessSupervisor` can only start a plugin via `StartAutostart` (the once-per-session startup sweep) or `Restart` (reachable only from the crash banner) — `StartOne` is private. This plan adds a public `Start(InstalledPlugin)` method, calls it from `PluginsViewModel.InstallAsync` right after the consent record is written, and deletes the now-dead "Restart RoRoRo" CTA (the `IsAppRestartPending` machinery + its XAML banner). Autostart wiring in `App.xaml.cs` is untouched — it still governs future launches.

**Tech Stack:** C# 14 / .NET 10, WPF, xUnit.

**Branch base:** `main` (v1.4 plugin system is already merged to main; local `main` is 1 commit behind `origin/main` — `git fetch && git checkout main && git pull` first). Suggested branch: `feat/plugin-in-session-start`.

**Known limitation (accepted decision):** This model has no "start now" affordance for a plugin that is installed-but-not-running in a *fresh* session (autostart off, past the install moment). The crash banner's Restart is the only other in-session start path. Accepted as the tradeoff for option B over a full per-row Start/Stop control. If this bites later, the follow-up is a per-row Start/Stop button.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `src/ROROROblox.App/Plugins/PluginProcessSupervisor.cs` | Owns plugin-process lifecycle + PID tracking | Add public `Start(InstalledPlugin)` |
| `src/ROROROblox.App/Plugins/PluginsViewModel.cs` | Backs the Plugins window | Call `_supervisor.Start` in `InstallAsync`; delete `IsAppRestartPending` / `RestartApp` machinery |
| `src/ROROROblox.App/Plugins/PluginsWindow.xaml` | Plugins window markup | Delete the "Restart RoRoRo" CTA banner |
| `src/ROROROblox.Tests/PluginProcessSupervisorTests.cs` | Supervisor unit tests | Add coverage for `Start` |

`PluginsViewModel` has no unit test in the suite today (concrete, hard-to-mock dependencies — `PluginRegistry`, `ConsentStore`, `PluginInstaller`). The new install→start wiring is covered by the supervisor unit test (Task 1) plus manual smoke (Task 3). A `PluginsViewModel` test harness is out of scope here.

---

### Task 1: Public `Start(InstalledPlugin)` on the supervisor

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginProcessSupervisor.cs`
- Test: `src/ROROROblox.Tests/PluginProcessSupervisorTests.cs`

- [ ] **Step 1: Write the failing tests**

Append these two tests inside the `PluginProcessSupervisorTests` class in `src/ROROROblox.Tests/PluginProcessSupervisorTests.cs`, immediately after `PluginExited_FiresWhenStarterReportsExit` (before the `private sealed class FakeProcessStarter` declaration):

```csharp
    [Fact]
    public void Start_LaunchesPluginNotAlreadyRunning()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);
        var plugin = MakePlugin("626labs.a", autostart: false);

        supervisor.Start(plugin);

        Assert.Single(fake.Started);
        Assert.Equal("626labs.a", fake.Started[0].id);
        Assert.True(supervisor.RunningPids.ContainsKey("626labs.a"));
    }

    [Fact]
    public void Start_RestartsPluginAlreadyRunning()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);
        var plugin = MakePlugin("626labs.a", autostart: false);

        supervisor.Start(plugin);
        var firstPid = supervisor.RunningPids["626labs.a"];

        supervisor.Start(plugin);

        Assert.Equal(2, fake.Started.Count);          // started twice
        Assert.Single(fake.KilledPids);               // old process killed once
        Assert.Equal(firstPid, fake.KilledPids[0]);
        Assert.NotEqual(firstPid, supervisor.RunningPids["626labs.a"]);
    }
```

(`Start` reads only `Manifest.Id` + `ExecutablePath`, never `Consent`, so `autostart: false` is irrelevant — it just keeps the existing `MakePlugin` helper unchanged.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginProcessSupervisorTests.Start_"`
Expected: FAIL — compile error, `'PluginProcessSupervisor' does not contain a definition for 'Start'`.

- [ ] **Step 3: Implement `Start`**

In `src/ROROROblox.App/Plugins/PluginProcessSupervisor.cs`, add this method immediately after the `Restart` method (after line 41, before the `Stop` method's xmldoc):

```csharp
    /// <summary>
    /// Start a single plugin process now, outside the autostart sweep. If the plugin is
    /// already running it's restarted (stop + start) so the caller always ends with a
    /// fresh process. The install flow calls this right after consent is granted so a
    /// freshly-installed plugin runs without a RoRoRo restart — autostart governs future
    /// launches, this governs "now".
    /// </summary>
    public void Start(InstalledPlugin plugin)
    {
        bool alreadyRunning;
        lock (_lock) { alreadyRunning = _pidByPluginId.ContainsKey(plugin.Manifest.Id); }
        if (alreadyRunning)
        {
            Restart(plugin);
        }
        else
        {
            StartOne(plugin);
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginProcessSupervisorTests"`
Expected: PASS — all supervisor tests green (the 6 existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginProcessSupervisor.cs src/ROROROblox.Tests/PluginProcessSupervisorTests.cs
git commit -m "feat(plugins): public Start(InstalledPlugin) on the process supervisor"
```

---

### Task 2: Install flow starts the plugin + remove the "Restart RoRoRo" CTA

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginsViewModel.cs`
- Modify: `src/ROROROblox.App/Plugins/PluginsWindow.xaml`

These two files must change in the same commit: the XAML binds to `IsAppRestartPending`, `RestartAppCommand`, and `DismissAppRestartCommand`. Removing the VM members without removing the XAML (or vice versa) breaks the build.

- [ ] **Step 1: Rewire `InstallAsync` to start the plugin**

In `src/ROROROblox.App/Plugins/PluginsViewModel.cs`, find this block inside `InstallAsync` (the success tail, currently around lines 148-153):

```csharp
            await _consentStore.GrantAsync(installed.Manifest.Id, granted).ConfigureAwait(true);
            _registryAdapter.Refresh();
            await LoadAsync().ConfigureAwait(true);
            StatusBanner = $"{installed.Manifest.Name} installed.";
            IsAppRestartPending = true;
            InstallUrlInput = string.Empty;
```

Replace it with:

```csharp
            await _consentStore.GrantAsync(installed.Manifest.Id, granted).ConfigureAwait(true);
            _registryAdapter.Refresh();
            await LoadAsync().ConfigureAwait(true);

            // Start the plugin process now — no RoRoRo restart needed. The per-row
            // Autostart toggle only governs whether it ALSO launches on future RoRoRo
            // starts. A start failure here is non-fatal: the plugin is installed, and
            // the user can still toggle autostart and restart RoRoRo. InstalledPlugin
            // .ExecutablePath is a computed property (InstallDir + id + ".exe"), so the
            // installer's return value is sufficient — no rescan needed.
            try
            {
                _supervisor.Start(installed);
                StatusBanner = $"{installed.Manifest.Name} installed and running.";
            }
            catch (Exception startEx)
            {
                StatusBanner = $"{installed.Manifest.Name} installed, but failed to start: {startEx.Message}";
            }

            InstallUrlInput = string.Empty;
```

- [ ] **Step 2: Delete the `IsAppRestartPending` field**

In `PluginsViewModel.cs`, delete this line (currently line 38, in the private-field block):

```csharp
    private bool _isAppRestartPending;
```

- [ ] **Step 3: Delete the restart-app command initializations**

In the constructor, delete these two lines (currently lines 60-61):

```csharp
        RestartAppCommand = new RelayCommand(_ => RestartApp());
        DismissAppRestartCommand = new RelayCommand(_ => IsAppRestartPending = false);
```

- [ ] **Step 4: Delete the `IsAppRestartPending` property**

Delete the whole property + its xmldoc (currently lines 98-108):

```csharp
    /// <summary>
    /// True after a fresh install while the user hasn't restarted RoRoRo yet. Surfaces the
    /// post-install "Restart RoRoRo now" CTA. Plugins start at app startup via autostart, so
    /// a fresh install doesn't actually launch anything until restart. Decision
    /// M9E6B82i4y4gAyd7esG3.
    /// </summary>
    public bool IsAppRestartPending
    {
        get => _isAppRestartPending;
        set { if (_isAppRestartPending != value) { _isAppRestartPending = value; Raise(); } }
    }
```

- [ ] **Step 5: Delete the restart-app `ICommand` declarations**

Delete these two lines (currently lines 114-115, in the `ICommand` property block):

```csharp
    public ICommand RestartAppCommand { get; }
    public ICommand DismissAppRestartCommand { get; }
```

- [ ] **Step 6: Delete the `RestartApp` method**

Delete the whole method + its xmldoc (currently lines 215-238):

```csharp
    /// <summary>
    /// Restart RoRoRo to pick up freshly installed plugins. Plugin processes are launched by
    /// <see cref="PluginProcessSupervisor.StartAutostart"/> during App.OnStartup, so a fresh
    /// install doesn't run until next launch. Best-effort: spawn the same EXE then shut down.
    /// Decision M9E6B82i4y4gAyd7esG3.
    /// </summary>
    private void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            StatusBanner = "Restart failed: couldn't resolve process path. Close and reopen RoRoRo manually.";
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            StatusBanner = $"Restart failed: {ex.Message}. Close and reopen RoRoRo manually.";
        }
    }
```

Keep `RestartCommand` and `RestartFromBannerAsync` — those drive the crash-recovery banner ("plugin stopped — click to restart") and are unaffected.

- [ ] **Step 7: Delete the "Restart RoRoRo" CTA banner from the XAML**

In `src/ROROROblox.App/Plugins/PluginsWindow.xaml`, delete the entire second `<Border>` inside the `Grid.Row="1"` StackPanel — the block that starts with the comment `<!-- App-restart CTA after a fresh install. ... -->` and ends with its closing `</Border>` (currently lines 133-173):

```xml
            <!-- App-restart CTA after a fresh install. Plugin processes are launched at
                 App.OnStartup via autostart, so a new install doesn't run until restart.
                 Decision M9E6B82i4y4gAyd7esG3. -->
            <Border Padding="14,10"
                    Margin="0,8,0,0"
                    Background="{DynamicResource RowBgBrush}"
                    BorderBrush="{DynamicResource CyanBrush}"
                    BorderThickness="0,0,0,2"
                    CornerRadius="6"
                    Visibility="{Binding IsAppRestartPending, Converter={StaticResource BoolToVisibilityConverter}}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0"
                               Text="Restart RoRoRo to start your newly installed plugin."
                               Foreground="{DynamicResource WhiteBrush}"
                               FontSize="12"
                               VerticalAlignment="Center"
                               TextWrapping="Wrap" />
                    <Button Grid.Column="1"
                            Content="Restart RoRoRo"
                            Margin="12,0,0,0"
                            Padding="14,4"
                            Style="{StaticResource CyanCtaButton}"
                            FontSize="11"
                            Command="{Binding RestartAppCommand}" />
                    <Button Grid.Column="2"
                            Content="Later"
                            Margin="6,0,0,0"
                            Padding="10,4"
                            Background="{DynamicResource NavyBrush}"
                            Foreground="{DynamicResource MutedTextBrush}"
                            BorderBrush="{DynamicResource DividerBrush}"
                            BorderThickness="1"
                            FontSize="11"
                            Command="{Binding DismissAppRestartCommand}" />
                </Grid>
            </Border>
```

- [ ] **Step 8: Trim the now-stale banner comment**

Still in `PluginsWindow.xaml`, find the comment above the `Grid.Row="1"` StackPanel (currently lines 84-89). Delete the last sentence referring to the removed third banner. Change:

```xml
        <!-- Status banner — non-actionable cyan strip when an info / install message is set.
             When BannerIsRestartable flips on (PluginExited fired), the magenta-strip banner
             shows underneath with a Restart button bound to RestartCommand. Two banners so
             the cosmetic difference between "info" and "actionable" lands on first read.
             A third banner below this one (IsAppRestartPending) surfaces a "Restart RoRoRo"
             CTA after a fresh install — plugins start on next app launch. -->
```

to:

```xml
        <!-- Status banner — non-actionable cyan strip when an info / install message is set.
             When BannerIsRestartable flips on (PluginExited fired), the magenta-strip banner
             shows underneath with a Restart button bound to RestartCommand. Two banners so
             the cosmetic difference between "info" and "actionable" lands on first read. -->
```

- [ ] **Step 9: Build to verify the VM + XAML are consistent**

Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj`
Expected: BUILD SUCCEEDED — no unresolved `IsAppRestartPending` / `RestartAppCommand` / `DismissAppRestartCommand` binding-target compile errors, no unused-field warnings.

- [ ] **Step 10: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginsViewModel.cs src/ROROROblox.App/Plugins/PluginsWindow.xaml
git commit -m "feat(plugins): install starts the plugin in-session; drop Restart RoRoRo CTA"
```

---

### Task 3: Full build, test, and manual smoke

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build`
Expected: BUILD SUCCEEDED across all projects.

- [ ] **Step 2: Full test suite**

Run: `dotnet test src/ROROROblox.Tests/`
Expected: PASS — all tests green, including the 8 `PluginProcessSupervisorTests`.

- [ ] **Step 3: Manual smoke — install starts the plugin now**

1. Build + run RoRoRo (`dotnet run --project src/ROROROblox.App`).
2. Open the Plugins window (tray → "Plugins..." or the main-window Plugins entry).
3. Paste the RoRoRo Ur Task release URL (`https://github.com/estevanhernandez-stack-ed/rororo-ur-task/releases/download/v0.2.0/`) and click Install.
4. Grant capabilities on the consent sheet.
5. **Expected:** the status banner reads "RoRoRo Ur Task installed and running" — and the Ur Task recorder window + tray icon appear within ~2s, with **no** "Restart RoRoRo" banner anywhere.
6. Confirm the Ur Task process is alive: `Get-Process 626labs.ur-task`.

- [ ] **Step 4: Manual smoke — autostart still governs future launches**

1. With Ur Task installed from Step 3, leave its Autostart checkbox **off**. Quit RoRoRo fully (tray → Quit) and confirm `626labs.ur-task` exits.
2. Relaunch RoRoRo. **Expected:** Ur Task does **not** start (autostart off).
3. Open Plugins, toggle Ur Task's Autostart **on**, quit RoRoRo fully, relaunch.
4. **Expected:** Ur Task starts automatically on launch (the `App.StartPluginAutostartAsync` sweep is unchanged).

- [ ] **Step 5: Local-path audit before push**

Run: `git grep -nI "C:\\\\Users" -- "src/ROROROblox.App/Plugins/*" "src/ROROROblox.Tests/PluginProcessSupervisorTests.cs"`
Expected: no output (no machine-specific paths in committable code — per pattern kk from wbp-azure).

- [ ] **Step 6: Push the branch**

```bash
git push -u origin feat/plugin-in-session-start
```

---

## Self-Review

**Spec coverage:**
- "Install starts the plugin in-session" → Task 1 (`Start`) + Task 2 Step 1 (`InstallAsync` wiring), smoke Task 3 Step 3.
- "Autostart governs only future launches" → unchanged `App.StartPluginAutostartAsync`; verified Task 3 Step 4.
- "Remove the Restart RoRoRo CTA" → Task 2 Steps 2-8.

**Placeholder scan:** none — every step shows exact code or exact commands.

**Type consistency:** `Start(InstalledPlugin)` defined in Task 1 Step 3, called in Task 2 Step 1 with the `installed` local (type `InstalledPlugin`, the return of `_installer.InstallAsync`). `_supervisor` is the existing `PluginProcessSupervisor` field on `PluginsViewModel`. `RestartCommand` / `RestartFromBannerAsync` are explicitly preserved.
