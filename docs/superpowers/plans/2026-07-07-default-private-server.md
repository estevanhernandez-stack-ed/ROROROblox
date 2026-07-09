# Default Private Server + Library Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a saved private server be marked (and unmarked) the default — independent of the default game — surfaced with a Set/Clear-default control + DEFAULT badge in the Library, pre-selected (top + highlighted) in Squad Launch; and fix the Library's dead-space layout + clipped header.

**Architecture:** `SavedPrivateServer` gains `IsDefault`; `PrivateServerStore` gains `SetDefaultAsync(Guid)` / `ClearDefaultAsync()` / `DefaultChanged`, mirroring `FavoriteGameStore`'s gate → validate → no-op short-circuit → mutate-all-rows → save → fire-outside-gate pattern. The Library (`SettingsWindow`) server rows mirror the game rows' default controls; the window's fixed 2:1 list proportions are replaced by one shared scroll. Squad Launch orders default-first via a pure, testable helper.

**Tech Stack:** .NET 10 / C#, WPF, xUnit (real stores over temp files + hand-rolled fakes — no Moq).

**Spec:** `docs/superpowers/specs/2026-07-07-default-private-server-design.md` (approved). Branch: `feat/default-private-server` (spec committed).

## Global Constraints

- **Build/test with the explicit dotnet host:** PowerShell `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" …`; bash `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" …`. Bare `dotnet` fails the SDK pin.
- **Solution is `ROROROblox.slnx`** — never the stray `.sln`.
- **No hardcoded colors** in any new XAML — only existing `DynamicResource` theme tokens (`CyanBrush`, `NavyBrush`, `RowBgBrush`, `WhiteBrush`, `MutedTextBrush`, `DividerBrush`), the same set the game DEFAULT badge uses. No new tokens expected.
- **Every `SavedPrivateServer` construct/copy site carries `IsDefault`** — the two full-reconstruction sites (`AddAsync`, legacy migration in `LoadAsync`) are updated explicitly in Task 1; `with { }` sites need no change.
- **`DefaultChanged` fires OUTSIDE the store gate** (subscribers may re-enter the store) and **only on real changes** (no-op paths skip write AND event).
- **Zero-default is legal.** No auto-promotion anywhere: removing/clearing the default leaves no default.
- **Existing behavior with no default set is unchanged** (Squad Launch pure-recency order, no highlight).
- No user-profile paths in committed files (CI guard). Conventional commits. Hand-rolled fakes; store tests use a real store over a unique temp file (match the existing `PrivateServerStoreTests` harness style — read it before writing tests).

---

## File Structure

**Modified (Core):**
- `src/ROROROblox.Core/SavedPrivateServer.cs` — `IsDefault` record field.
- `src/ROROROblox.Core/IPrivateServerStore.cs` — `SetDefaultAsync`, `ClearDefaultAsync`, `DefaultChanged`.
- `src/ROROROblox.Core/PrivateServerStore.cs` — impl + `AddAsync`/migration preservation + `RemoveAsync` event.

**Modified (App):**
- `src/ROROROblox.App/Settings/SettingsWindow.xaml` — server-row default controls + badge + border; shared-scroll layout; header wrap + copy.
- `src/ROROROblox.App/Settings/SettingsWindow.xaml.cs` — `OnSetDefaultServerClick` / `OnClearDefaultServerClick`.
- `src/ROROROblox.App/SquadLaunch/SquadLaunchWindow.xaml.cs` — default-first ordering + highlight.

**Created:**
- `src/ROROROblox.App/SquadLaunch/SquadLaunchOrdering.cs` — pure ordering helper.
- `docs/superpowers/smoke/2026-07-07-default-private-server-smoke.md` — manual smoke checklist (Task 6).

**Tests:**
- `src/ROROROblox.Tests/PrivateServerStoreTests.cs` — extend (Tasks 1-2).
- `src/ROROROblox.Tests/SquadLaunchOrderingTests.cs` — new (Task 3).

---

## Task 1: `IsDefault` on the record + every construct site carries it

**Files:**
- Modify: `src/ROROROblox.Core/SavedPrivateServer.cs`, `src/ROROROblox.Core/PrivateServerStore.cs`
- Test: `src/ROROROblox.Tests/PrivateServerStoreTests.cs` (extend — read the file first and match its harness/naming style)

**Interfaces:**
- Produces: `SavedPrivateServer.IsDefault` (bool, default `false`). Consumed by Task 2 (mutators), Task 3 (ordering), Tasks 4/6 (UI bindings).

- [ ] **Step 1: Write the failing tests** (adapt construction to the real test harness — it builds stores over unique temp files):

```csharp
    [Fact]
    public async Task Add_NewServer_IsDefaultFalse()
    {
        using var store = CreateStore(out var path);
        try
        {
            var added = await store.AddAsync(1, "code-a", PrivateServerCodeKind.LinkCode, "A", "Place A", "");
            Assert.False(added.IsDefault);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Add_ReplaceExisting_PreservesIsDefault()
    {
        using var store = CreateStore(out var path);
        try
        {
            var added = await store.AddAsync(1, "code-a", PrivateServerCodeKind.LinkCode, "A", "Place A", "");
            await store.SetDefaultAsync(added.Id); // Task 2 API — see note below
            var replaced = await store.AddAsync(1, "code-a", PrivateServerCodeKind.LinkCode, "A renamed", "Place A", "");
            Assert.True(replaced.IsDefault); // re-adding the same (placeId, code) must not drop the default
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Load_LegacyBlobWithoutIsDefault_DefaultsFalse()
    {
        var path = TempPath();
        try
        {
            // A legacy-shaped row: accessCode field, no isDefault property anywhere.
            await File.WriteAllTextAsync(path, """
                {"version":1,"servers":[{"id":"7e5c9a51-0e6f-4b7e-9a2f-111111111111","placeId":42,
                "accessCode":"legacy-code","name":"Old","placeName":"Old Place","thumbnailUrl":"",
                "addedAt":"2026-01-01T00:00:00+00:00"}]}
                """);
            using var store = new PrivateServerStore(path);
            var list = await store.ListAsync();
            Assert.Single(list);
            Assert.False(list[0].IsDefault);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task IsDefault_RoundTripsThroughJson()
    {
        var path = TempPath();
        try
        {
            using (var store = new PrivateServerStore(path))
            {
                var added = await store.AddAsync(1, "code-a", PrivateServerCodeKind.LinkCode, "A", "P", "");
                await store.SetDefaultAsync(added.Id); // Task 2 API
            }
            using var reopened = new PrivateServerStore(path);
            var list = await reopened.ListAsync();
            Assert.True(Assert.Single(list).IsDefault);
        }
        finally { Cleanup(path); }
    }
```

**Sequencing note:** two of these tests call `SetDefaultAsync`, which Task 2 adds. To keep every commit green, Task 1 implements the FIELD + construct sites and commits only the two tests that compile (`Add_NewServer_IsDefaultFalse`, `Load_LegacyBlobWithoutIsDefault_DefaultsFalse`); the other two tests ship IN TASK 2's test step. (They're shown here so the field's full contract is visible in one place — do not add them in Task 1.)

- [ ] **Step 2: Run to verify the two Task-1 tests fail** — `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PrivateServerStoreTests"` → FAIL (`IsDefault` not defined).

- [ ] **Step 3: Add the field.** In `SavedPrivateServer.cs`, append to the record parameter list (after `LocalName`):

```csharp
    string? LocalName = null,
    bool IsDefault = false)
```

(Defaulted → additive; all existing positional constructions still compile.)

- [ ] **Step 4: Update the three construct sites in `PrivateServerStore.cs`:**

1. `AddAsync` record construction (line ~106) — add after `LocalName: preservedLocalName`:

```csharp
                LocalName: preservedLocalName,
                IsDefault: existingIndex >= 0 && blob.Servers[existingIndex].IsDefault);
```

2. Legacy migration in `LoadAsync` (line ~245) — add after `LocalName: s.LocalName`:

```csharp
                    LocalName: s.LocalName,
                    IsDefault: s.IsDefault));
```

3. The tolerant `StoredServer` record (line ~297) — add the field so old JSON (missing it) defaults false and new JSON round-trips:

```csharp
        string? LocalName = null,
        bool IsDefault = false);
```

- [ ] **Step 5: Run to verify** — the two Task-1 tests PASS; whole `PrivateServerStoreTests` class green; full build `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` → 0 errors (the defaulted parameter keeps `MainViewModel.ToFavoriteEntry` etc. compiling untouched).

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/SavedPrivateServer.cs src/ROROROblox.Core/PrivateServerStore.cs src/ROROROblox.Tests/PrivateServerStoreTests.cs
git commit -m "feat(core): IsDefault on SavedPrivateServer — all construct sites carry it"
```

---

## Task 2: `SetDefaultAsync` / `ClearDefaultAsync` / `DefaultChanged` on the store

**Files:**
- Modify: `src/ROROROblox.Core/IPrivateServerStore.cs`, `src/ROROROblox.Core/PrivateServerStore.cs`
- Test: `src/ROROROblox.Tests/PrivateServerStoreTests.cs` (extend)

**Interfaces:**
- Produces: `Task IPrivateServerStore.SetDefaultAsync(Guid id)`, `Task ClearDefaultAsync()`, `event EventHandler? DefaultChanged`. Consumed by Task 4 (Library handlers). Mirrors `FavoriteGameStore.SetDefaultAsync` (`FavoriteGameStore.cs:154-191`) exactly: gate → load → existence check → no-op short-circuit → mutate every row → save → set `changed` → fire event OUTSIDE the gate.

- [ ] **Step 1: Write the failing tests** (plus the two deferred from Task 1 — `Add_ReplaceExisting_PreservesIsDefault`, `IsDefault_RoundTripsThroughJson`):

```csharp
    [Fact]
    public async Task SetDefault_MutualExclusion_ExactlyOneDefault()
    {
        using var store = CreateStore(out var path);
        try
        {
            var a = await store.AddAsync(1, "a", PrivateServerCodeKind.LinkCode, "A", "P", "");
            var b = await store.AddAsync(2, "b", PrivateServerCodeKind.LinkCode, "B", "P", "");
            await store.SetDefaultAsync(a.Id);
            await store.SetDefaultAsync(b.Id);
            var list = await store.ListAsync();
            Assert.Equal(b.Id, Assert.Single(list, s => s.IsDefault).Id);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task SetDefault_AlreadyDefault_NoOpNoEvent()
    {
        using var store = CreateStore(out var path);
        try
        {
            var a = await store.AddAsync(1, "a", PrivateServerCodeKind.LinkCode, "A", "P", "");
            var fires = 0;
            store.DefaultChanged += (_, _) => fires++;
            await store.SetDefaultAsync(a.Id);
            await store.SetDefaultAsync(a.Id); // second call: already default
            Assert.Equal(1, fires);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task SetDefault_UnknownId_Throws()
    {
        using var store = CreateStore(out var path);
        try
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => store.SetDefaultAsync(Guid.NewGuid()));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ClearDefault_RemovesDefault_FiresOnce_NoOpWhenNone()
    {
        using var store = CreateStore(out var path);
        try
        {
            var a = await store.AddAsync(1, "a", PrivateServerCodeKind.LinkCode, "A", "P", "");
            var fires = 0;
            store.DefaultChanged += (_, _) => fires++;
            await store.ClearDefaultAsync();          // nothing default yet -> no-op, no event
            Assert.Equal(0, fires);
            await store.SetDefaultAsync(a.Id);        // fires (1)
            await store.ClearDefaultAsync();          // fires (2)
            Assert.Equal(2, fires);
            Assert.DoesNotContain((await store.ListAsync()), s => s.IsDefault);
            await store.SetDefaultAsync(a.Id);        // set -> clear -> set round-trip works
            Assert.True(Assert.Single(await store.ListAsync()).IsDefault);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Remove_DefaultServer_ZeroDefault_FiresEvent()
    {
        using var store = CreateStore(out var path);
        try
        {
            var a = await store.AddAsync(1, "a", PrivateServerCodeKind.LinkCode, "A", "P", "");
            var b = await store.AddAsync(2, "b", PrivateServerCodeKind.LinkCode, "B", "P", "");
            await store.SetDefaultAsync(a.Id);
            var fires = 0;
            store.DefaultChanged += (_, _) => fires++;
            await store.RemoveAsync(a.Id);            // removed the default -> event, zero default
            Assert.Equal(1, fires);
            Assert.DoesNotContain((await store.ListAsync()), s => s.IsDefault);
            await store.RemoveAsync(b.Id);            // b was never default -> no event
            Assert.Equal(1, fires);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Rename_PreservesIsDefault()
    {
        using var store = CreateStore(out var path);
        try
        {
            var a = await store.AddAsync(1, "a", PrivateServerCodeKind.LinkCode, "A", "P", "");
            await store.SetDefaultAsync(a.Id);
            await store.UpdateLocalNameAsync(a.Id, "My clan server");
            Assert.True(Assert.Single(await store.ListAsync()).IsDefault);
        }
        finally { Cleanup(path); }
    }
```

- [ ] **Step 2: Run to verify they fail** (`SetDefaultAsync`/`ClearDefaultAsync`/`DefaultChanged` not defined).

- [ ] **Step 3: Extend the interface** (`IPrivateServerStore.cs`, after `UpdateLocalNameAsync`):

```csharp
    /// <summary>
    /// Mark this server the default (clears the flag on every other server — at most one
    /// default). Throws <see cref="KeyNotFoundException"/> if no server has this id. No-op
    /// (no write, no event) when it's already the default.
    /// </summary>
    Task SetDefaultAsync(Guid id);

    /// <summary>
    /// Clear the default flag on every server, returning to the zero-default state. No-op
    /// (no write, no event) when nothing is default. Zero-default is legal: Squad Launch
    /// falls back to manual pick.
    /// </summary>
    Task ClearDefaultAsync();

    /// <summary>
    /// Fired after <see cref="SetDefaultAsync"/> / <see cref="ClearDefaultAsync"/> (or a
    /// default-removing <see cref="RemoveAsync"/>) mutates state and persists. Fired outside
    /// the store gate so subscribers can re-enter the store without deadlocking. Mirrors
    /// <see cref="IFavoriteGameStore.DefaultChanged"/>.
    /// </summary>
    event EventHandler? DefaultChanged;
```

- [ ] **Step 4: Implement in `PrivateServerStore.cs`.** Add the event field near the top of the class (`public event EventHandler? DefaultChanged;`), then:

```csharp
    public async Task SetDefaultAsync(Guid id)
    {
        bool changed = false;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            if (!blob.Servers.Any(s => s.Id == id))
            {
                throw new KeyNotFoundException($"Private server {id} not found.");
            }

            // No-op short-circuit: already the default -> skip the write AND the event, so
            // subscribers treat each event as a real change (mirrors FavoriteGameStore).
            if (blob.Servers.Any(s => s.Id == id && s.IsDefault))
            {
                return;
            }

            for (var i = 0; i < blob.Servers.Count; i++)
            {
                blob.Servers[i] = blob.Servers[i] with { IsDefault = blob.Servers[i].Id == id };
            }

            await SaveAsync(blob).ConfigureAwait(false);
            changed = true;
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            // Outside the gate so subscribers can re-enter the store without deadlocking.
            DefaultChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ClearDefaultAsync()
    {
        bool changed = false;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            if (!blob.Servers.Any(s => s.IsDefault))
            {
                return; // zero-default already -> no write, no event
            }

            for (var i = 0; i < blob.Servers.Count; i++)
            {
                blob.Servers[i] = blob.Servers[i] with { IsDefault = false };
            }

            await SaveAsync(blob).ConfigureAwait(false);
            changed = true;
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            DefaultChanged?.Invoke(this, EventArgs.Empty);
        }
    }
```

And rework `RemoveAsync` to fire when the removed row was the default (keep everything else identical):

```csharp
    public async Task RemoveAsync(Guid id)
    {
        bool removedDefault = false;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Servers.FindIndex(s => s.Id == id);
            if (idx < 0)
            {
                return;
            }
            removedDefault = blob.Servers[idx].IsDefault;
            blob.Servers.RemoveAt(idx);
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (removedDefault)
        {
            // Zero-default is the intended post-state (no auto-promotion — spec §3); the event
            // just tells subscribers the default changed.
            DefaultChanged?.Invoke(this, EventArgs.Empty);
        }
    }
```

- [ ] **Step 5: Run to verify** — all new tests + all pre-existing `PrivateServerStoreTests` PASS. Full build 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/IPrivateServerStore.cs src/ROROROblox.Core/PrivateServerStore.cs src/ROROROblox.Tests/PrivateServerStoreTests.cs
git commit -m "feat(core): SetDefaultAsync/ClearDefaultAsync/DefaultChanged on the private-server store"
```

---

## Task 3: `SquadLaunchOrdering` pure helper

**Files:**
- Create: `src/ROROROblox.App/SquadLaunch/SquadLaunchOrdering.cs`
- Test: `src/ROROROblox.Tests/SquadLaunchOrderingTests.cs` (new file)

**Interfaces:**
- Produces: `static IReadOnlyList<SavedPrivateServer> SquadLaunchOrdering.Order(IReadOnlyList<SavedPrivateServer> servers)` — default first (if any), then the existing recency order (`LastLaunchedAt ?? AddedAt`, descending). Consumed by Task 6.

- [ ] **Step 1: Write the failing tests**

```csharp
using ROROROblox.App.SquadLaunch;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class SquadLaunchOrderingTests
{
    private static SavedPrivateServer Server(string name, bool isDefault = false,
        DateTimeOffset? added = null, DateTimeOffset? launched = null) => new(
        Id: Guid.NewGuid(), PlaceId: 1, Code: name, CodeKind: PrivateServerCodeKind.LinkCode,
        Name: name, PlaceName: "P", ThumbnailUrl: "",
        AddedAt: added ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        LastLaunchedAt: launched, IsDefault: isDefault);

    [Fact]
    public void Order_DefaultFirst_ThenRecency()
    {
        var older = Server("older", launched: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var newer = Server("newer", launched: new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
        var def   = Server("default", isDefault: true, launched: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var ordered = SquadLaunchOrdering.Order([older, newer, def]);

        Assert.Equal(new[] { "default", "newer", "older" }, ordered.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Order_NoDefault_PureRecency_LaunchedBeatsAdded()
    {
        var addedOnly = Server("addedOnly", added: new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero));
        var launched  = Server("launched",  added: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                               launched: new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero));

        var ordered = SquadLaunchOrdering.Order([addedOnly, launched]);

        Assert.Equal(new[] { "launched", "addedOnly" }, ordered.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Order_EmptyList_Empty()
    {
        Assert.Empty(SquadLaunchOrdering.Order([]));
    }
}
```

- [ ] **Step 2: Run to verify it fails** (type not defined).

- [ ] **Step 3: Implement**

```csharp
using ROROROblox.Core;

namespace ROROROblox.App.SquadLaunch;

/// <summary>
/// Pure ordering for the Squad Launch saved-servers list: the default server (if any) first,
/// then the pre-existing recency order (most-recently-launched, falling back to AddedAt).
/// Extracted static so the ordering is unit-testable without WPF.
/// </summary>
internal static class SquadLaunchOrdering
{
    public static IReadOnlyList<SavedPrivateServer> Order(IReadOnlyList<SavedPrivateServer> servers) =>
        servers
            .OrderByDescending(s => s.IsDefault)
            .ThenByDescending(s => s.LastLaunchedAt ?? s.AddedAt)
            .ToList();
}
```

- [ ] **Step 4: Run to verify** — 3/3 PASS; full build 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/SquadLaunch/SquadLaunchOrdering.cs src/ROROROblox.Tests/SquadLaunchOrderingTests.cs
git commit -m "feat(app): SquadLaunchOrdering — default-first then recency, pure + tested"
```

---

## Task 4: Library server-row default controls (badge + Set/Clear + border)

UI task — thin XAML + code-behind over the tested Task-2 store (house convention: windows are manual-smoke). Read `SettingsWindow.xaml` fully before editing; the game-row template (lines ~62-169) is the pattern being mirrored.

**Files:**
- Modify: `src/ROROROblox.App/Settings/SettingsWindow.xaml`, `src/ROROROblox.App/Settings/SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `IPrivateServerStore.SetDefaultAsync` / `ClearDefaultAsync` (Task 2); `SavedPrivateServer.IsDefault` (Task 1); existing converters `BoolToVisibilityConverter` / `InverseBoolConverter` (app-level resources — the game template already uses both) and theme brushes.

- [ ] **Step 1: XAML — the `SavedPrivateServer` DataTemplate.** Mirror the game template:

1. **Cyan default border** — replace the template's plain `<Border Margin=… Padding=… CornerRadius="8" Background=…>` opening with the same `Border.Style` DataTrigger the game template has (lines 66-75), verbatim:

```xml
                <Border.Style>
                    <Style TargetType="Border">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsDefault}" Value="True">
                                <Setter Property="BorderBrush" Value="{DynamicResource CyanBrush}" />
                                <Setter Property="BorderThickness" Value="1" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Border.Style>
```

2. **DEFAULT badge** — in the name `StackPanel` (next to the existing magenta PRIVATE badge), add AFTER the PRIVATE badge, mirroring the game badge (lines 119-125):

```xml
                            <Border Background="{DynamicResource CyanBrush}" CornerRadius="3"
                                    Margin="6,2,0,0" Padding="6,1"
                                    Visibility="{Binding IsDefault, Converter={StaticResource BoolToVisibilityConverter}}">
                                <TextBlock Text="DEFAULT"
                                           FontSize="9" FontWeight="Bold"
                                           Foreground="{DynamicResource NavyBrush}" />
                            </Border>
```

3. **Set default / Clear default buttons** — in the row's action `StackPanel`, BEFORE the Rename button:

```xml
                        <Button Content="Set default"
                                Tag="{Binding Id}"
                                Click="OnSetDefaultServerClick"
                                Padding="10,6" Margin="0,0,8,0"
                                Background="{DynamicResource NavyBrush}"
                                Foreground="{DynamicResource MutedTextBrush}"
                                BorderBrush="{DynamicResource DividerBrush}"
                                BorderThickness="1"
                                FontSize="11"
                                ToolTip="Pre-select this server when you launch all accounts to a private server."
                                Visibility="{Binding IsDefault, Converter={StaticResource InverseBoolConverter}}" />
                        <Button Content="Clear default"
                                Click="OnClearDefaultServerClick"
                                Padding="10,6" Margin="0,0,8,0"
                                Background="{DynamicResource NavyBrush}"
                                Foreground="{DynamicResource MutedTextBrush}"
                                BorderBrush="{DynamicResource DividerBrush}"
                                BorderThickness="1"
                                FontSize="11"
                                ToolTip="Stop pre-selecting this server. You'll pick one each time again."
                                Visibility="{Binding IsDefault, Converter={StaticResource BoolToVisibilityConverter}}" />
```

(Exactly one of the two is visible per row. Confirm the two converters resolve as StaticResources in this window — the game template already uses both keys, so they're reachable; if either is missing at runtime, find its app-level key by grepping `BoolToVisibilityConverter` in `App.xaml` and use the real key.)

- [ ] **Step 2: Code-behind handlers** (`SettingsWindow.xaml.cs`, next to `OnSetDefaultClick`; mirror its shape):

```csharp
    private async void OnSetDefaultServerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Guid id)
        {
            return;
        }

        try
        {
            await _servers.SetDefaultAsync(id);
            await ReloadServersAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't set default server: {ex.Message}";
        }
    }

    private async void OnClearDefaultServerClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _servers.ClearDefaultAsync();
            await ReloadServersAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't clear the default server: {ex.Message}";
        }
    }
```

- [ ] **Step 3: Verify** — full build 0 errors; full test suite no regressions. (Visual verification lands in Task 6's smoke checklist.)

- [ ] **Step 4: Commit**

```bash
git add src/ROROROblox.App/Settings/SettingsWindow.xaml src/ROROROblox.App/Settings/SettingsWindow.xaml.cs
git commit -m "feat(library): Set/Clear default + DEFAULT badge + cyan border on private-server rows"
```

---

## Task 5: Library layout cleanup (shared scroll, header wrap, copy)

UI/layout task — no unit tests; verified by build + Task 6 smoke. Read the current `SettingsWindow.xaml` grid (lines ~268-428) before editing.

**Files:**
- Modify: `src/ROROROblox.App/Settings/SettingsWindow.xaml` (layout + header only — do not touch the DataTemplates from Task 4)

- [ ] **Step 1: Header fixes.** On the description `TextBlock` (line ~294): add `TextWrapping="Wrap"` and replace the text with:

```
Saved games and private servers. The game marked DEFAULT is what Launch As uses; the private server marked DEFAULT is pre-selected when you launch all accounts to a server. Rename any row to give it a custom name (Roblox-side names stay untouched).
```

- [ ] **Step 2: Replace the fixed-proportion rows with one shared scroll.** Current rows 5-8 (games header / games list `2*` / servers header / servers list `*`) always claim a 2:1 slice → the dead gap. Replace the `Grid.RowDefinitions` rows 5-8 with a single `*` row, and rows 5-8's content with ONE `ScrollViewer` containing a `StackPanel` (headers + both `ItemsControl`s + inline empty states). The footer Close button becomes row 6. Concretely — new row definitions:

```xml
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />  <!-- Title -->
            <RowDefinition Height="Auto" />  <!-- Search box -->
            <RowDefinition Height="Auto" />  <!-- Search results -->
            <RowDefinition Height="Auto" />  <!-- Or paste URL -->
            <RowDefinition Height="Auto" />  <!-- Add status -->
            <RowDefinition Height="*" />     <!-- Shared scroll: games + servers -->
            <RowDefinition Height="Auto" />  <!-- Footer -->
        </Grid.RowDefinitions>
```

And the shared region (replaces the old rows 5-8 content; empty states are inline `TextBlock`s that the code-behind toggles exactly as it already does via `EmptyState`/`ServersEmptyState` visibility):

```xml
        <ScrollViewer Grid.Row="5" VerticalScrollBarVisibility="Auto" Margin="0,20,0,0">
            <StackPanel>
                <TextBlock Text="Saved games"
                           FontSize="12" FontWeight="SemiBold"
                           Foreground="{DynamicResource MutedTextBrush}"
                           Margin="0,0,0,8" />
                <ItemsControl x:Name="FavoritesList" />
                <StackPanel x:Name="EmptyState" Margin="0,4,0,0">
                    <TextBlock Text="No saved games yet."
                               FontSize="13"
                               Foreground="{DynamicResource MutedTextBrush}" />
                    <TextBlock Text="Search above or paste a Roblox game URL to add your first one."
                               FontSize="11"
                               Foreground="{DynamicResource MutedTextBrush}"
                               Margin="0,4,0,0"
                               Opacity="0.7" />
                </StackPanel>

                <TextBlock Text="Saved private servers"
                           FontSize="12" FontWeight="SemiBold"
                           Foreground="{DynamicResource MutedTextBrush}"
                           Margin="0,16,0,8" />
                <ItemsControl x:Name="ServersList" />
                <StackPanel x:Name="ServersEmptyState" Margin="0,4,0,0">
                    <TextBlock Text="No saved private servers."
                               FontSize="13"
                               Foreground="{DynamicResource MutedTextBrush}" />
                    <TextBlock Text="Use the Private server toolbar button to add one."
                               FontSize="11"
                               Foreground="{DynamicResource MutedTextBrush}"
                               Margin="0,4,0,0"
                               Opacity="0.7" />
                </StackPanel>
            </StackPanel>
        </ScrollViewer>
```

Update the Close button to `Grid.Row="6"`. Keep the `x:Name`s identical (`FavoritesList`, `ServersList`, `EmptyState`, `ServersEmptyState`) — the code-behind toggles them and must not change. The empty states move from centered-overlay to inline-under-header (deliberate: content-height sections, no dead space).

- [ ] **Step 3: Verify** — full build 0 errors; suite no regressions.

- [ ] **Step 4: Commit**

```bash
git add src/ROROROblox.App/Settings/SettingsWindow.xaml
git commit -m "fix(library): shared-scroll layout (no fixed 2:1 dead space) + wrapped, both-defaults header"
```

---

## Task 6: Squad Launch pre-selection + smoke checklist

**Files:**
- Modify: `src/ROROROblox.App/SquadLaunch/SquadLaunchWindow.xaml.cs`
- Create: `docs/superpowers/smoke/2026-07-07-default-private-server-smoke.md`

**Interfaces:**
- Consumes: `SquadLaunchOrdering.Order` (Task 3), `SavedPrivateServer.IsDefault` (Task 1), theme brushes.

- [ ] **Step 1: Use the ordering helper.** In `RenderListAsync` (line ~88), replace

```csharp
        // Most-recently-launched first; ties fall back to addedAt.
        var sorted = servers.OrderByDescending(s => s.LastLaunchedAt ?? s.AddedAt);
```

with

```csharp
        // Default server first (pre-selected), then most-recently-launched. Pure + unit-tested.
        var sorted = SquadLaunchOrdering.Order(servers);
```

- [ ] **Step 2: Highlight the default row.** In `BuildServerRow` (line ~96): after the `row` Border is constructed, add the default treatment (cyan border + a DEFAULT tag next to the name), all via theme resources:

```csharp
        if (server.IsDefault)
        {
            row.BorderBrush = (Brush)FindResource("CyanBrush");
            row.BorderThickness = new Thickness(1);
        }
```

and in the name/info `StackPanel` construction, after the name `TextBlock` is added, insert (only when `server.IsDefault`) a horizontal wrap so the name row carries the badge — concretely, replace the plain name TextBlock add with:

```csharp
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = renderName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("WhiteBrush"),
        });
        if (server.IsDefault)
        {
            var badge = new Border
            {
                Background = (Brush)FindResource("CyanBrush"),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(8, 1, 0, 0),
                Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock
                {
                    Text = "DEFAULT",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("NavyBrush"),
                },
            };
            nameRow.Children.Add(badge);
        }
        info.Children.Add(nameRow);
```

(Confirm the resource keys `CyanBrush`/`NavyBrush`/`WhiteBrush` are what this window already uses — `BuildServerRow` resolves `RowBgBrush`/`WhiteBrush`/`MutedTextBrush`/`CyanBrush`/`NavyBrush` today, so they're all present.)

- [ ] **Step 3: Write the smoke checklist** to `docs/superpowers/smoke/2026-07-07-default-private-server-smoke.md`:

```markdown
# Smoke checklist — default private server + Library cleanup

**Branch:** `feat/default-private-server` · **Spec:** [`../specs/2026-07-07-default-private-server-design.md`](../specs/2026-07-07-default-private-server-design.md)

The store + ordering logic is unit-tested; the two windows are manual-smoke by house convention.

## Setup
- [ ] Quit the installed RoRoRo from the tray (single-instance guard).
- [ ] Build + run the dev build; have 2+ saved private servers and 1+ saved game.

## Library (Games button → RoRoRo / Library)
- [ ] **1. Set default:** server row → **Set default** → cyan DEFAULT badge appears next to PRIVATE, row gets a cyan border, its Set-default button becomes **Clear default**.
- [ ] **2. Switch:** Set default on a second server → badge + border MOVE (exactly one default).
- [ ] **3. Clear:** **Clear default** → no badge anywhere; button flips back to Set default on all rows.
- [ ] **4. Game default untouched:** the game marked DEFAULT keeps its badge through all of the above; Launch As still goes to the default game.
- [ ] **5. Rename the default server** → badge survives. **Remove the default server** → default gone (no other server promoted).
- [ ] **6. Layout:** with 1 game + 1 server, sections sit directly under each other — no dead gap. Header text wraps (no clipping) and mentions both defaults. Many rows → one shared scrollbar.
- [ ] **7. Theming:** switch to a custom theme → badge/border/buttons recolor with the theme (no stuck default-palette colors).

## Squad Launch (Private server toolbar button)
- [ ] **8. Pre-selection:** with a default set, it lists FIRST with a cyan border + DEFAULT tag; Launch all on it works.
- [ ] **9. No default:** clear the default → order returns to most-recent-first, no highlight (today's behavior).

## Result
- [ ] All pass → merge-ready. Anything off → note the check + what you saw; fix pass before merge.
```

- [ ] **Step 4: Verify** — full build 0 errors; whole solution test `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test ROROROblox.slnx` green.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/SquadLaunch/SquadLaunchWindow.xaml.cs docs/superpowers/smoke/2026-07-07-default-private-server-smoke.md
git commit -m "feat(squadlaunch): default server first + highlighted; smoke checklist"
```

---

## Final verification (after all tasks)

- [ ] Whole solution build + test: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` and `… test ROROROblox.slnx` — green.
- [ ] Local-path guard: `SCAN_ALL=1 bash .claude/hooks/pre-commit-local-path-guard.sh` — clean.
- [ ] Grep the diff for hardcoded hex in new XAML/C# (`#17d4fa|#0f1f31|#f22f89` outside comments) — zero hits; everything through `DynamicResource`/`FindResource`.
- [ ] Manual smoke: the Task 6 checklist (user's step, live desktop).

---

## Self-review notes (author)

**Spec coverage:** §2.1-2.2 defaults model → Tasks 1-2. §2.3 pre-select → Tasks 3, 6. §2.4 unset → Tasks 2, 4. §2.5 theming → Tasks 4, 6 + final grep. §2.6/§4.2 layout cleanup → Task 5. §4.1 copy-site preservation → Task 1 (all three sites incl. the tolerant StoredServer). §6 edge cases → Task 2 tests (unknown id, no-ops, remove-default) + Task 6 smoke (rename survives, game default untouched). §7 test matrix → Tasks 1-3 unit + Task 6 smoke.

**Type consistency:** `IsDefault` (Task 1) consumed by Tasks 2/3/4/6. `SetDefaultAsync(Guid)`/`ClearDefaultAsync()`/`DefaultChanged` (Task 2) consumed by Task 4. `SquadLaunchOrdering.Order(IReadOnlyList<SavedPrivateServer>)` (Task 3) consumed by Task 6. Handler names in Task 4's XAML match Task 4's code-behind.

**Green-commit discipline:** Task 1 defers the two `SetDefaultAsync`-dependent tests to Task 2 (called out explicitly). Task 4's XAML + handlers land in one commit (XAML references the handlers). Task 5 touches only layout, after Task 4's template work, so the two don't collide in one file's same region — implementers read the current file state before editing.

**Adaptation points flagged:** the real `PrivateServerStoreTests` harness (`CreateStore`/`TempPath`/`Cleanup` names are illustrative — match the file), the converter resource keys (verify against App.xaml), and the exact current XAML line positions (read before edit; line refs are orientation, not gospel).
