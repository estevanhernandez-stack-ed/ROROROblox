# F-001 — the three Tools verbs, and the wiring that keeps them honest

**Date:** 2026-08-09 · **Finding:** F-001 (QF-19), register score 5/5 · **Wave:** glow, post-6
**Register:** `docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md`

---

## What F-001 actually is now

The register row reads as a large IA change: *"Build Tools container on main window (pairs with
QF-21): History, Diagnostics, Plugins, Library, Stop all, Open log folder, Welcome tour; Settings
keeps only what writes settings.json/discord.dat."*

Most of that shipped in glow wave 3. What remains is three menu items.

**Already done, verified in the tree:**

- The Tools container exists (`MainWindow.xaml:1066`, `Controls/ToolsDropDownButton.cs`) and
  carries History, Diagnostics, Games, Plugins, a separator, and About.
- Settings holds only settings. `Preferences/PreferencesWindow.xaml:78-82` is Startup, Accounts,
  Alerts & memory, Discord, Appearance — all of which write `settings.json` or `discord.dat`.
- Two deliberate departures from the register are already recorded in `MainWindow.xaml:1031-1043`
  (Games is not renamed to Library, because wave 1's F-006 swept "Library" out of user-visible
  strings; Tools does not mirror the tray exactly). **Both stand. This spec does not revisit them.**

**What is left,** named by `MainWindow.xaml:1044-1047`:

> F-001 also lists Stop all, Open log folder, and the Welcome tour as Tools items. They are absent:
> none has a view-model command (the tray raises events handled in App.xaml.cs), so they need new
> plumbing. F-001 stays open with exactly those three named.

So the work is the plumbing, not the container.

## Scope

Three Tools items, plus one carried-forward defect that touches the same code:

1. `Stop all Roblox instances`
2. `Open log folder`
3. `Welcome tour`
4. The `.welcome-shown` sentinel bug (`MainWindow.xaml.cs:103-110`)

## Architecture

### Three commands on MainViewModel

| command | dependencies | modal |
|---|---|---|
| `StopAllCommand` | `IRobloxRunningProbe`, `IRobloxInstanceStopper` | `StopAllConfirmWindow` |
| `OpenLogFolderCommand` | `IShellOpener` (new) | none |
| `ShowWelcomeTourCommand` | none | `WelcomeWindow` |

**`StopAllCommand` moves the body of `App.xaml.cs:1876 StopAllInstances()` intact:** probe the
running count, return silently on zero, show the confirm, and on acceptance call
`ExpectCloseForAll()` **before** `StopAll()`.

That ordering is load-bearing and moves as one piece rather than being re-derived. Reversed, eight
deliberate closes raise eight drop-out alerts — the false-alarm class that makes a warning
ignorable by the time it matters.

**`IShellOpener`** is a one-method seam:

```csharp
public interface IShellOpener
{
    void Open(string path);
}
```

The production implementation wraps `Process.Start(new ProcessStartInfo { FileName = path,
UseShellExecute = true, Verb = "open" })`, matching `App.xaml.cs:1632-1638`. It exists so
"Open log folder" is assertable, and so no view-model test launches Explorer on a CI runner.

**Modals use the established pattern, not a new one.** `MainViewModel` already calls
`Modals.LaunchHeadroomWindow.ShouldProceed(snapshot, reserve, count, Application.Current?.MainWindow)`
at `MainViewModel.cs:1742`. Stop-all's confirm and the tour get static entry points taking an owner
window, so the view-model never constructs a `Window` and tests never need an STA thread.

### The tray delegates to the same commands

`App.xaml.cs:913` and `:916` become calls into the view-model commands. The private
`StopAllInstances()` and `OpenLogsFolder()` methods are **deleted, not left in place** — a
duplicate that still compiles is a duplicate that will drift.

This is the same reasoning that made `EvaluateFollow` pure: *"so both follow surfaces share the
exact same decision and can't drift apart."* Two surfaces, one implementation.

### Wiring moves somewhere testable

The eleven `tray.Request* +=` lines (`App.xaml.cs:911-924`) move out of the `App` instance into a
wiring table that takes its behaviour as delegates:

```csharp
internal sealed record TrayHandlers(
    Action OpenMainWindow,
    Action ToggleMutex,
    Action StopAllInstances,
    Action Quit,
    Action OpenDiagnostics,
    Action OpenLogs,
    Action OpenPreferences,
    Action ActivateMain,
    Action OpenHistory,
    Action OpenPlugins,
    Action<Guid> FocusAccount);

internal static class TrayWiring
{
    public static void Connect(ITrayService tray, TrayHandlers handlers);
}
```

`App.xaml.cs` builds the record — `StopAllInstances: () => vm.StopAllCommand.Execute(null)`,
`OpenLogs: () => vm.OpenLogFolderCommand.Execute(null)`, and its existing private methods for the
rest — then calls `Connect`. Tests build a record of recording delegates and a fake `ITrayService`.

Delegates rather than passing `MainViewModel` and the other collaborators directly: `Connect` then
has exactly one job, subscribing, and holds no knowledge of what any handler does. That is what
makes it readable as a table, and the table is the thing the test asserts against.

Without this extraction the wiring test cannot exist, because the handlers are otherwise trapped
inside a WPF `Application`.

### Blast radius

Changed: `ViewModels/MainViewModel.cs`, `App.xaml.cs` (wiring + two deletions),
`MainWindow.xaml` (three menu items), `MainWindow.xaml.cs` (sentinel).
New: `IShellOpener` + implementation, `TrayWiring`, `TrayHandlers`.
Unchanged: `Tray/TrayService.cs`, `Core/ITrayService.cs`, `StopAllConfirmWindow`, `WelcomeWindow`.

## Menu placement

```text
History                      destinations — windows inside the app
Diagnostics
Games
Plugins
─────────────
Open log folder              actions that reach outside the app
Stop all Roblox instances
─────────────
Welcome tour                 about the app
About
```

Two reasons for the grouping. The existing menu is entirely destinations, and dropping a
destructive verb into that cluster puts "Stop all" one row from "Plugins"; the confirm catches a
mis-click, but distance is cheaper than a dialog. And it matches how the register frames F-001 —
tools are "verb-shaped and episodic," so the verbs sit together rather than interleaved with
places.

Separators use the explicit `Background="{DynamicResource DividerBrush}"` treatment already applied
at `MainWindow.xaml:1095`, for the reason documented there: WPF-UI's default `Separator` draws from
its own dictionary, which `ThemeService` never touches, so it vanishes on any user theme with a
light field.

**The label is `Stop all Roblox instances`, not `Stop all`.** It matches `StopAllConfirmWindow`'s
own title, and bare "Stop all" in a menu that also lists Games and Plugins does not say what it
stops.

## The sentinel fix

Today (`MainWindow.xaml.cs:103-110`):

```csharp
if (WelcomeWindow.IsFirstRun())
{
    WelcomeWindow.MarkShown();                    // burns the sentinel unconditionally
    if (DataContext is MainViewModel mvm && mvm.Accounts.Count == 0)
    {
        var welcome = new WelcomeWindow { Owner = this };
        welcome.ShowDialog();
    }
}
```

An upgrading user with accounts burns the sentinel and never sees the tour — permanently. The one
surface documenting six unlabelled row affordances never fires for that entire user class.

After:

```csharp
if (WelcomeWindow.IsFirstRun())
{
    if (DataContext is MainViewModel mvm && mvm.Accounts.Count == 0)
    {
        WelcomeWindow.MarkShown();                // only mark it if we actually showed it
        var welcome = new WelcomeWindow { Owner = this };
        welcome.ShowDialog();
    }
}
```

**Stated consequence, so it is not discovered later:** an upgrading user now keeps `IsFirstRun()`
true indefinitely, so if they later remove every account, the tour appears. That is judged correct
— an empty account list is the exact state the tour is written for, and that user has never seen
it. The alternative (show the tour to upgraders once, then mark) interrupts existing users on
upgrade, which is worse.

**`ShowWelcomeTourCommand` never touches the sentinel.** Opening the tour from Tools is a manual
request: it shows the window and writes nothing. The sentinel governs only the automatic
first-run path.

The two halves overlap deliberately. The menu item is what actually fixes discoverability; the
sentinel change stops the app recording that it showed something it did not.

## Testing

`XamlStyleIntegrityTests` documents the gap this work sits in:

> WHAT THIS DOES NOT COVER [...]: bindings to properties that do not exist (WPF fails those
> silently, so no static or runtime check sees them without watching the trace log)

Three new `Command="{Binding …}"` menu items fail silently if misnamed. That is the drift to catch.

**1. Command-binding resolver.** Scan app XAML for every `Command="{Binding X}"`, reflect over the
corresponding view-model, assert `X` exists and is an `ICommand`. Reuses
`XamlStyleIntegrityTests`'s `FindRepoRoot()` / `AppSourceDirectory()` / `XDocument` scaffolding.
Scoped to `MainWindow` → `MainViewModel`, so it ships as a working gate rather than a
window-to-view-model mapping exercise. Widening it later is additive.

**2. Tray wiring.** A fake `ITrayService`, `TrayWiring.Connect(...)`, then raise each of the eleven
events and assert the right action fired. Catches a handler bound to the wrong action, and a new
tray event added later with no handler at all. The second is the one that rots quietly.

**3. Stop-all behavior:**

- zero instances running → no confirm shown, `StopAll` never called
- confirm accepted → `ExpectCloseForAll()` called **before** `StopAll()`
- confirm declined → neither called

The ordering assertion is the load-bearing one; it gets a test name that says so.

**4. Open log folder** → `IShellOpener` receives `AppLogging.LogDirectory`.

**5. Welcome sentinel:**

- first run, no accounts → shown, and marked
- first run, accounts present → **not marked, not shown** (this is the bug; the test is named for it)
- not first run → neither

Estimated 12-14 test methods against the existing 1343.

## Explicitly not covered

- **Whether the menu renders.** No test in this repo loads XAML.
  `XamlStyleIntegrityTests` argues at length that a construct-the-window smoke test would not have
  caught the bug that motivated it either, because WPF creates resources lazily. The binding
  resolver is the honest coverage; visual confirmation stays a manual check.
- **The two wave-3 departures** (Games not renamed to Library; Tools not mirroring the tray).
  Settled and documented in the markup.
- **QF-21.** The register pairs F-001 with it. Nothing here depends on it and it is not in scope.
- **Widening the binding resolver to every window.** Additive follow-up, not a gate on this work.

## Acceptance

F-001 closes when:

1. Tools carries all three verbs in the grouping above.
2. `App.xaml.cs` no longer defines the private methods `StopAllInstances()` and `OpenLogsFolder()`
   — the tray reaches that behaviour through the view-model commands. (The `TrayHandlers` record
   still has fields of those names; what must be gone are the method bodies, so there is exactly
   one implementation of each.)
3. The five test groups pass, including the binding resolver and the tray-wiring table.
4. The register row and `MainWindow.xaml:1044-1047`'s "F-001 stays open with exactly those three
   named" comment are both updated to reflect the close.
