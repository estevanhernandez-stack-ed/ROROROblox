# Streamer Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A single sticky toggle that gives every account (and friend) a persistent, playful fake identity — silly name + bundled avatar — so RoRoRo's account manager is safe to screen-share.

**Architecture:** One central `IStreamerIdentityProvider` is the only thing that decides real-vs-fake. Every identity-bearing UI read routes through it; the toggle and reroll flip it; a `Changed` event refreshes bindings. Nothing else checks the mode, so no surface can be forgotten. Fake identities persist (accounts on the `Account` record, friends in a small file) so they're stable across a multi-session stream.

**Tech Stack:** .NET 10, C# 14, WPF, xUnit. Existing patterns: `Account` record + `AccountStore` (DPAPI blob, `SemaphoreSlim` gate, no-op-write avoidance), `IAppSettings` bool Get/Set pairs, `AccountSummary` INPC ViewModel, `RobloxWindowDecorator` (`SetWindowTextW`).

## Global Constraints

- **Design spec:** `docs/superpowers/specs/2026-07-10-streamer-mode-design.md` — the source of truth.
- **Fail-safe:** with the mode ON, no real name, real avatar URL, or Roblox user id may reach any UI surface. This is asserted once, at the provider seam.
- **Non-destructive:** the toggle only changes what is *shown*. It never edits the real `Account` fields. Turning it off restores the real identity untouched.
- **Avatars are a design deliverable, not code.** The silly avatar images go through the `626labs-design` skill (pattern x — no programmatic placeholders). Code references them by id; the pool of image files is produced separately (Task 9).
- **Scope boundary:** masks the account manager only. In-game identity (leaderboards, chat) is Roblox-side and out of scope. UI copy must not claim "fully stream-safe."
- **Build/test:** `dotnet test src/ROROROblox.Tests/` (unit) — run from a cwd outside the repo's `global.json` if the pinned SDK is unavailable, or align the pin. WPF App builds only on Windows.
- **Commits:** conventional commits; scope `streamer`.

---

### Task 1: Silly-name pool

**Files:**
- Create: `src/ROROROblox.Core/StreamerMode/StreamerNamePool.cs`
- Create: `src/ROROROblox.Core/StreamerMode/streamer-names.txt` (embedded resource, one name per line)
- Test: `src/ROROROblox.Tests/StreamerNamePoolTests.cs`

**Interfaces:**
- Produces:
  - `interface IStreamerNamePool { string Next(IReadOnlySet<string> inUse); int Count { get; } }`
  - `sealed class StreamerNamePool : IStreamerNamePool` (parameterless ctor loads the embedded list; test ctor `StreamerNamePool(IReadOnlyList<string> names)`)

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

public class StreamerNamePoolTests
{
    private static StreamerNamePool Pool(params string[] names) => new(names);

    [Fact]
    public void Next_AvoidsNamesInUse_WhenPossible()
    {
        var pool = Pool("CaptainNoodle", "SirRerollington", "LadyPixel");
        var used = new HashSet<string> { "CaptainNoodle", "SirRerollington" };

        var pick = pool.Next(used);

        Assert.Equal("LadyPixel", pick);
    }

    [Fact]
    public void Next_AllInUse_StillReturnsAName_NeverThrows()
    {
        var pool = Pool("CaptainNoodle", "SirRerollington");
        var used = new HashSet<string> { "CaptainNoodle", "SirRerollington" };

        var pick = pool.Next(used);

        Assert.Contains(pick, new[] { "CaptainNoodle", "SirRerollington" });
    }

    [Fact]
    public void EmbeddedList_LoadsAtLeast50Names()
    {
        var pool = new StreamerNamePool();
        Assert.True(pool.Count >= 50, $"expected >=50 bundled names, got {pool.Count}");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter FullyQualifiedName~StreamerNamePoolTests`
Expected: FAIL — `StreamerNamePool` does not exist.

- [ ] **Step 3: Create the embedded name list**

Create `src/ROROROblox.Core/StreamerMode/streamer-names.txt` with at least 60 clearly-fake, family-friendly names, one per line. Examples to seed (extend to 60+):

```
CaptainNoodle
SirRerollington
LadyPixel
BaronBloxwell
DoctorDuck
PrinceParsnip
MajorMuffin
CountCabbage
ProfessorPotato
AdmiralArtichoke
```

Register it as an embedded resource in `src/ROROROblox.Core/ROROROblox.Core.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="StreamerMode/streamer-names.txt" />
</ItemGroup>
```

- [ ] **Step 4: Write the implementation**

```csharp
using System.Reflection;

namespace ROROROblox.Core.StreamerMode;

public interface IStreamerNamePool
{
    /// <summary>A name not in <paramref name="inUse"/> when one exists; otherwise any name. Never throws, never empty.</summary>
    string Next(IReadOnlySet<string> inUse);
    int Count { get; }
}

public sealed class StreamerNamePool : IStreamerNamePool
{
    private readonly IReadOnlyList<string> _names;
    private int _cursor;

    public StreamerNamePool() : this(LoadEmbedded()) { }

    public StreamerNamePool(IReadOnlyList<string> names)
    {
        if (names is null || names.Count == 0)
            throw new ArgumentException("Name pool must be non-empty.", nameof(names));
        _names = names;
    }

    public int Count => _names.Count;

    public string Next(IReadOnlySet<string> inUse)
    {
        // One pass from a rotating cursor picks the first free name; falls back to the cursor's
        // name when every name is taken (repeats allowed, never a real name, never a throw).
        for (var i = 0; i < _names.Count; i++)
        {
            var candidate = _names[(_cursor + i) % _names.Count];
            if (!inUse.Contains(candidate))
            {
                _cursor = (_cursor + i + 1) % _names.Count;
                return candidate;
            }
        }
        var fallback = _names[_cursor];
        _cursor = (_cursor + 1) % _names.Count;
        return fallback;
    }

    private static IReadOnlyList<string> LoadEmbedded()
    {
        var asm = typeof(StreamerNamePool).Assembly;
        var resource = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("streamer-names.txt", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd()
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        return lines;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter FullyQualifiedName~StreamerNamePoolTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/StreamerMode/ src/ROROROblox.Core/ROROROblox.Core.csproj src/ROROROblox.Tests/StreamerNamePoolTests.cs
git commit -m "feat(streamer): silly-name pool with in-use avoidance"
```

---

### Task 2: Fake-avatar pool (id selection)

**Files:**
- Create: `src/ROROROblox.Core/StreamerMode/StreamerAvatarPool.cs`
- Test: `src/ROROROblox.Tests/StreamerAvatarPoolTests.cs`

**Interfaces:**
- Produces:
  - `interface IStreamerAvatarPool { string Next(IReadOnlySet<string> inUse); string ResourceUri(string avatarId); int Count { get; } }`
  - `sealed class StreamerAvatarPool : IStreamerAvatarPool`

The avatar *images* land in Task 9 (design skill). This task owns only the id list + the pack URI mapping, so id selection is testable now without the art. Ids are stable strings like `noodle`, `duck`, `potato`; the resource uri is `pack://application:,,,/ROROROblox.App;component/StreamerMode/Avatars/{id}.png`.

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

public class StreamerAvatarPoolTests
{
    private static StreamerAvatarPool Pool(params string[] ids) => new(ids);

    [Fact]
    public void Next_AvoidsIdsInUse_WhenPossible()
    {
        var pool = Pool("noodle", "duck", "potato");
        var pick = pool.Next(new HashSet<string> { "noodle", "duck" });
        Assert.Equal("potato", pick);
    }

    [Fact]
    public void ResourceUri_BuildsPackUri()
    {
        var pool = Pool("noodle");
        Assert.Equal(
            "pack://application:,,,/ROROROblox.App;component/StreamerMode/Avatars/noodle.png",
            pool.ResourceUri("noodle"));
    }

    [Fact]
    public void Next_AllInUse_ReturnsAnId_NeverThrows()
    {
        var pool = Pool("noodle", "duck");
        var pick = pool.Next(new HashSet<string> { "noodle", "duck" });
        Assert.Contains(pick, new[] { "noodle", "duck" });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter FullyQualifiedName~StreamerAvatarPoolTests`
Expected: FAIL — type missing.

- [ ] **Step 3: Write the implementation**

```csharp
namespace ROROROblox.Core.StreamerMode;

public interface IStreamerAvatarPool
{
    string Next(IReadOnlySet<string> inUse);
    string ResourceUri(string avatarId);
    int Count { get; }
}

public sealed class StreamerAvatarPool : IStreamerAvatarPool
{
    // Ids MUST match the shipped image filenames in Task 9 (App/StreamerMode/Avatars/{id}.png).
    private static readonly string[] DefaultIds =
    {
        "noodle", "duck", "potato", "cabbage", "muffin", "artichoke",
        "parsnip", "pixel", "bloxwell", "turnip", "waffle", "pickle",
    };

    private readonly IReadOnlyList<string> _ids;
    private int _cursor;

    public StreamerAvatarPool() : this(DefaultIds) { }

    public StreamerAvatarPool(IReadOnlyList<string> ids)
    {
        if (ids is null || ids.Count == 0)
            throw new ArgumentException("Avatar pool must be non-empty.", nameof(ids));
        _ids = ids;
    }

    public int Count => _ids.Count;

    public string Next(IReadOnlySet<string> inUse)
    {
        for (var i = 0; i < _ids.Count; i++)
        {
            var candidate = _ids[(_cursor + i) % _ids.Count];
            if (!inUse.Contains(candidate))
            {
                _cursor = (_cursor + i + 1) % _ids.Count;
                return candidate;
            }
        }
        var fallback = _ids[_cursor];
        _cursor = (_cursor + 1) % _ids.Count;
        return fallback;
    }

    public string ResourceUri(string avatarId)
        => $"pack://application:,,,/ROROROblox.App;component/StreamerMode/Avatars/{avatarId}.png";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter FullyQualifiedName~StreamerAvatarPoolTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/StreamerMode/StreamerAvatarPool.cs src/ROROROblox.Tests/StreamerAvatarPoolTests.cs
git commit -m "feat(streamer): fake-avatar id pool + pack-uri mapping"
```

---

### Task 3: StreamerIdentity persistence (accounts + friends)

**Files:**
- Create: `src/ROROROblox.Core/StreamerMode/StreamerIdentity.cs`
- Create: `src/ROROROblox.Core/StreamerMode/IStreamerIdentityStore.cs`
- Create: `src/ROROROblox.Core/StreamerMode/FileStreamerIdentityStore.cs`
- Modify: `src/ROROROblox.Core/Account.cs` (add two fields)
- Modify: `src/ROROROblox.Core/AccountStore.cs` (add `UpdateStreamerIdentityAsync`, thread the fields through the two `new Account(...)` projections at lines ~58 and ~101, and the stored↔record maps at ~560/~617)
- Test: `src/ROROROblox.Tests/FileStreamerIdentityStoreTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct StreamerIdentity(string FakeName, string FakeAvatarId)`
  - `interface IStreamerIdentityStore { Task<IReadOnlyDictionary<string, StreamerIdentity>> LoadAllAsync(); Task SaveAsync(string key, StreamerIdentity identity); }`
  - `sealed class FileStreamerIdentityStore : IStreamerIdentityStore` (JSON file under `%LOCALAPPDATA%\ROROROblox\streamer-identities.dat`)
  - `Account` gains `string? StreamerName = null, string? StreamerAvatarId = null`
  - `AccountStore.UpdateStreamerIdentityAsync(Guid accountId, string fakeName, string fakeAvatarId)`
- Consumes: nothing.

**Key convention:** identity keys are `account:{guid}` and `friend:{robloxUserId}`. Accounts persist on the `Account` record (so they ride the DPAPI blob); friends persist in `FileStreamerIdentityStore` (they aren't accounts). The provider (Task 4) merges both into one map.

> **Why a plaintext file for friends is acceptable:** a fake name↔friend mapping is not a secret (no cookie, no `.ROBLOSECURITY`). The account-side identities ride the existing DPAPI blob because they live on `Account`. Do NOT put any real cookie or user secret in this file.

- [ ] **Step 1: Write the failing test (friends file store)**

```csharp
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

public class FileStreamerIdentityStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"streamer-{Guid.NewGuid():N}.dat");

    [Fact]
    public async Task SaveThenLoad_RoundTripsIdentity()
    {
        var store = new FileStreamerIdentityStore(_path);
        await store.SaveAsync("friend:12345", new StreamerIdentity("CaptainNoodle", "noodle"));

        var loaded = await new FileStreamerIdentityStore(_path).LoadAllAsync();

        Assert.True(loaded.TryGetValue("friend:12345", out var id));
        Assert.Equal("CaptainNoodle", id.FakeName);
        Assert.Equal("noodle", id.FakeAvatarId);
    }

    [Fact]
    public async Task LoadAll_MissingFile_ReturnsEmpty()
        => Assert.Empty(await new FileStreamerIdentityStore(_path).LoadAllAsync());

    [Fact]
    public async Task Save_OverwritesSameKey()
    {
        var store = new FileStreamerIdentityStore(_path);
        await store.SaveAsync("friend:1", new StreamerIdentity("A", "duck"));
        await store.SaveAsync("friend:1", new StreamerIdentity("B", "potato"));

        var loaded = await store.LoadAllAsync();
        Assert.Single(loaded);
        Assert.Equal("B", loaded["friend:1"].FakeName);
    }

    public void Dispose() { try { File.Delete(_path); } catch { } }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter FullyQualifiedName~FileStreamerIdentityStoreTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Write the model + interface + file store**

```csharp
// StreamerIdentity.cs
namespace ROROROblox.Core.StreamerMode;

public readonly record struct StreamerIdentity(string FakeName, string FakeAvatarId);
```

```csharp
// IStreamerIdentityStore.cs
namespace ROROROblox.Core.StreamerMode;

public interface IStreamerIdentityStore
{
    Task<IReadOnlyDictionary<string, StreamerIdentity>> LoadAllAsync();
    Task SaveAsync(string key, StreamerIdentity identity);
}
```

```csharp
// FileStreamerIdentityStore.cs
using System.Text.Json;

namespace ROROROblox.Core.StreamerMode;

/// <summary>
/// Persists friend fake-identities to a JSON file. NOT a secret store — never put a cookie here.
/// Account identities live on the DPAPI-backed Account record instead (see AccountStore).
/// </summary>
public sealed class FileStreamerIdentityStore : IStreamerIdentityStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileStreamerIdentityStore() : this(DefaultPath()) { }

    public FileStreamerIdentityStore(string path) => _path = path;

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox", "streamer-identities.dat");

    public async Task<IReadOnlyDictionary<string, StreamerIdentity>> LoadAllAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, StreamerIdentity>();
            var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, StreamerIdentity>>(json)
                   ?? new Dictionary<string, StreamerIdentity>();
        }
        catch
        {
            return new Dictionary<string, StreamerIdentity>(); // corrupt file must not brick startup
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(string key, StreamerIdentity identity)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var map = File.Exists(_path)
                ? (JsonSerializer.Deserialize<Dictionary<string, StreamerIdentity>>(
                       await File.ReadAllTextAsync(_path).ConfigureAwait(false))
                   ?? new Dictionary<string, StreamerIdentity>())
                : new Dictionary<string, StreamerIdentity>();
            map[key] = identity;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(map)).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
```

- [ ] **Step 4: Add the Account fields**

In `src/ROROROblox.Core/Account.cs`, append two parameters to the record (after `JoinViaFriend`):

```csharp
    bool JoinViaFriend = false,
    string? StreamerName = null,
    string? StreamerAvatarId = null);
```

- [ ] **Step 5: Thread the fields through AccountStore + add the update method**

In `src/ROROROblox.Core/AccountStore.cs`, add `StreamerName`/`StreamerAvatarId` to the four `new Account(...)` / stored-record projections (lines ~58, ~101, ~560, ~617 — carry the value through like `LocalName` is carried), then add the update method mirroring `UpdateLocalNameAsync`:

```csharp
public async Task UpdateStreamerIdentityAsync(Guid accountId, string fakeName, string fakeAvatarId)
{
    await _gate.WaitAsync().ConfigureAwait(false);
    try
    {
        var blob = await LoadAsync().ConfigureAwait(false);
        var index = blob.Accounts.FindIndex(a => a.Id == accountId);
        if (index < 0) throw new KeyNotFoundException($"Account {accountId} not found.");
        var cur = blob.Accounts[index];
        if (cur.StreamerName == fakeName && cur.StreamerAvatarId == fakeAvatarId) return;
        blob.Accounts[index] = cur with { StreamerName = fakeName, StreamerAvatarId = fakeAvatarId };
        await SaveAsync(blob).ConfigureAwait(false);
    }
    finally { _gate.Release(); }
}
```

Add to `IAccountStore`: `Task UpdateStreamerIdentityAsync(Guid accountId, string fakeName, string fakeAvatarId);`

> **Note:** the stored blob record type (the JSON-serialized shape backing `LoadAsync`) also needs the two nullable string fields. Add `StreamerName`/`StreamerAvatarId` to that record with `= null` defaults so existing `accounts.dat` blobs deserialize (missing fields → null → no fake identity yet). Find it near the other stored-record definition the file already uses.

- [ ] **Step 6: Run the store test + the full suite**

Run: `dotnet test src/ROROROblox.Tests/ --filter FullyQualifiedName~FileStreamerIdentityStoreTests`
Then: `dotnet test src/ROROROblox.Tests/` (the whole suite — the Account-record change touches many construction sites; confirm nothing regressed).
Expected: PASS; existing account round-trip tests still green (new fields default to null).

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.Core/StreamerMode/StreamerIdentity.cs src/ROROROblox.Core/StreamerMode/IStreamerIdentityStore.cs src/ROROROblox.Core/StreamerMode/FileStreamerIdentityStore.cs src/ROROROblox.Core/Account.cs src/ROROROblox.Core/AccountStore.cs src/ROROROblox.Tests/FileStreamerIdentityStoreTests.cs
git commit -m "feat(streamer): persist fake identities (accounts on record, friends in file)"
```

---

### Task 4: The identity provider (the central seam)

**Files:**
- Create: `src/ROROROblox.Core/StreamerMode/IStreamerIdentityProvider.cs`
- Create: `src/ROROROblox.Core/StreamerMode/StreamerIdentityProvider.cs`
- Test: `src/ROROROblox.Tests/StreamerIdentityProviderTests.cs`

**Interfaces:**
- Consumes: `IStreamerNamePool` (Task 1), `IStreamerAvatarPool` (Task 2), `IStreamerIdentityStore` (Task 3, for friends), `IAppSettings` (Task 5, for the sticky toggle).
- Produces:
  - `readonly record struct DisplayIdentity(string Name, string AvatarSource)`
  - `interface IStreamerIdentityProvider` with: `bool IsActive`, `Task InitializeAsync(...)`, `Task SetActiveAsync(bool)`, `DisplayIdentity ForAccount(Guid id, string realName, string realAvatarUrl)`, `DisplayIdentity ForFriend(long userId, string realName, string realAvatarUrl)`, `Task RerollAsync(string key)`, `Task RerollAllAsync()`, `event EventHandler? Changed`.

Provider holds an in-memory `Dictionary<string, StreamerIdentity>` seeded on init (accounts passed in from the loaded account list; friends from the file store). `ForAccount`/`ForFriend` return real values verbatim when inactive; when active they look up (lazily assigning + persisting a fresh identity if the key has none) and return the fake name + `avatarPool.ResourceUri(fakeAvatarId)`. Persistence of account identities is via an injected callback so Core stays store-agnostic and testable.

- [ ] **Step 1: Write the failing tests (incl. the leak scan)**

```csharp
using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

public class StreamerIdentityProviderTests
{
    private static StreamerIdentityProvider Make(bool active = false)
    {
        var settings = new FakeSettings(active);
        var provider = new StreamerIdentityProvider(
            new StreamerNamePool(new[] { "CaptainNoodle", "SirRerollington", "LadyPixel" }),
            new StreamerAvatarPool(new[] { "noodle", "duck", "potato" }),
            new InMemoryIdentityStore(),
            settings,
            persistAccount: (_, _) => Task.CompletedTask);
        provider.InitializeAsync(System.Array.Empty<(Guid, StreamerIdentity)>()).GetAwaiter().GetResult();
        return provider;
    }

    private static readonly Guid A = Guid.NewGuid();

    [Fact]
    public async Task Inactive_ReturnsRealIdentityVerbatim()
    {
        var p = Make(active: false);
        var id = p.ForAccount(A, "RealName", "https://real/avatar.png");
        Assert.Equal("RealName", id.Name);
        Assert.Equal("https://real/avatar.png", id.AvatarSource);
    }

    [Fact]
    public async Task Active_LeakScan_NeverReturnsRealNameAvatarOrUrl()
    {
        var p = Make(active: true);
        var id = p.ForAccount(A, "RealName", "https://real/avatar.png");
        Assert.NotEqual("RealName", id.Name);
        Assert.DoesNotContain("real", id.AvatarSource, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", id.AvatarSource, System.StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("pack://", id.AvatarSource);
    }

    [Fact]
    public async Task Active_SameAccount_StableAcrossCalls()
    {
        var p = Make(active: true);
        var first = p.ForAccount(A, "RealName", "x");
        var second = p.ForAccount(A, "RealName", "x");
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Reroll_ChangesTheIdentity_AndRaisesChanged()
    {
        var p = Make(active: true);
        var before = p.ForAccount(A, "RealName", "x");
        var raised = false; p.Changed += (_, _) => raised = true;
        await p.RerollAsync($"account:{A}");
        var after = p.ForAccount(A, "RealName", "x");
        Assert.True(raised);
        Assert.NotEqual(before.Name, after.Name);
    }

    [Fact]
    public async Task SetActive_PersistsToSettings_AndRaisesChanged()
    {
        var p = Make(active: false);
        var raised = false; p.Changed += (_, _) => raised = true;
        await p.SetActiveAsync(true);
        Assert.True(p.IsActive);
        Assert.True(raised);
    }

    private sealed class FakeSettings : IAppSettings
    {
        private bool _on;
        public FakeSettings(bool on) => _on = on;
        public Task<bool> GetStreamerModeAsync() => Task.FromResult(_on);
        public Task SetStreamerModeAsync(bool on) { _on = on; return Task.CompletedTask; }
        // Remaining IAppSettings members throw NotImplementedException — not exercised here.
        public Task<string?> GetDefaultPlaceUrlAsync() => throw new NotImplementedException();
        public Task SetDefaultPlaceUrlAsync(string url) => throw new NotImplementedException();
        public Task<bool> GetLaunchMainOnStartupAsync() => throw new NotImplementedException();
        public Task SetLaunchMainOnStartupAsync(bool e) => throw new NotImplementedException();
        public Task<string?> GetActiveThemeIdAsync() => throw new NotImplementedException();
        public Task SetActiveThemeIdAsync(string t) => throw new NotImplementedException();
        public Task<bool> GetBloxstrapWarningDismissedAsync() => throw new NotImplementedException();
        public Task SetBloxstrapWarningDismissedAsync(bool v) => throw new NotImplementedException();
        public Task<bool> GetMuteIdleAlertsAsync() => throw new NotImplementedException();
        public Task SetMuteIdleAlertsAsync(bool m) => throw new NotImplementedException();
        public Task<int> GetIdleWarnThresholdMinutesAsync() => throw new NotImplementedException();
        public Task SetIdleWarnThresholdMinutesAsync(int m) => throw new NotImplementedException();
        public Task<bool> GetCarefulSquadLaunchAsync() => throw new NotImplementedException();
        public Task SetCarefulSquadLaunchAsync(bool c) => throw new NotImplementedException();
    }

    private sealed class InMemoryIdentityStore : IStreamerIdentityStore
    {
        private readonly Dictionary<string, StreamerIdentity> _m = new();
        public Task<IReadOnlyDictionary<string, StreamerIdentity>> LoadAllAsync()
            => Task.FromResult<IReadOnlyDictionary<string, StreamerIdentity>>(_m);
        public Task SaveAsync(string key, StreamerIdentity id) { _m[key] = id; return Task.CompletedTask; }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter FullyQualifiedName~StreamerIdentityProviderTests`
Expected: FAIL — provider + `GetStreamerModeAsync` missing (the settings members come in Task 5; for now the test's fake declares them, so only the provider is missing).

- [ ] **Step 3: Write the provider**

```csharp
// IStreamerIdentityProvider.cs
namespace ROROROblox.Core.StreamerMode;

public readonly record struct DisplayIdentity(string Name, string AvatarSource);

public interface IStreamerIdentityProvider
{
    bool IsActive { get; }
    Task InitializeAsync(IReadOnlyCollection<(Guid accountId, StreamerIdentity identity)> accountIdentities);
    Task SetActiveAsync(bool active);
    DisplayIdentity ForAccount(Guid accountId, string realName, string realAvatarUrl);
    DisplayIdentity ForFriend(long robloxUserId, string realName, string realAvatarUrl);
    Task RerollAsync(string identityKey);
    Task RerollAllAsync();
    event EventHandler? Changed;
}
```

```csharp
// StreamerIdentityProvider.cs
using ROROROblox.Core;

namespace ROROROblox.Core.StreamerMode;

public sealed class StreamerIdentityProvider : IStreamerIdentityProvider
{
    private readonly IStreamerNamePool _names;
    private readonly IStreamerAvatarPool _avatars;
    private readonly IStreamerIdentityStore _friendStore;
    private readonly IAppSettings _settings;
    private readonly Func<Guid, StreamerIdentity, Task> _persistAccount;
    private readonly Dictionary<string, StreamerIdentity> _map = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public StreamerIdentityProvider(
        IStreamerNamePool names, IStreamerAvatarPool avatars,
        IStreamerIdentityStore friendStore, IAppSettings settings,
        Func<Guid, StreamerIdentity, Task> persistAccount)
    {
        _names = names; _avatars = avatars; _friendStore = friendStore;
        _settings = settings; _persistAccount = persistAccount;
    }

    public bool IsActive { get; private set; }
    public event EventHandler? Changed;

    public static string AccountKey(Guid id) => $"account:{id}";
    public static string FriendKey(long userId) => $"friend:{userId}";

    public async Task InitializeAsync(IReadOnlyCollection<(Guid accountId, StreamerIdentity identity)> accountIdentities)
    {
        IsActive = await _settings.GetStreamerModeAsync().ConfigureAwait(false);
        var friends = await _friendStore.LoadAllAsync().ConfigureAwait(false);
        lock (_lock)
        {
            foreach (var (id, identity) in accountIdentities)
                if (!string.IsNullOrEmpty(identity.FakeName)) _map[AccountKey(id)] = identity;
            foreach (var kv in friends) _map[kv.Key] = kv.Value;
        }
    }

    public DisplayIdentity ForAccount(Guid accountId, string realName, string realAvatarUrl)
        => Resolve(AccountKey(accountId), realName, realAvatarUrl, accountId);

    public DisplayIdentity ForFriend(long robloxUserId, string realName, string realAvatarUrl)
        => Resolve(FriendKey(robloxUserId), realName, realAvatarUrl, accountId: null);

    private DisplayIdentity Resolve(string key, string realName, string realAvatarUrl, Guid? accountId)
    {
        if (!IsActive) return new DisplayIdentity(realName, realAvatarUrl);

        StreamerIdentity id;
        bool assigned = false;
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out id))
            {
                id = MakeFresh();
                _map[key] = id;
                assigned = true;
            }
        }
        if (assigned) _ = Persist(key, id, accountId); // fire-and-forget persist of the lazy assignment
        return new DisplayIdentity(id.FakeName, _avatars.ResourceUri(id.FakeAvatarId));
    }

    private StreamerIdentity MakeFresh()
    {
        var usedNames = _map.Values.Select(v => v.FakeName).ToHashSet(StringComparer.Ordinal);
        var usedAvatars = _map.Values.Select(v => v.FakeAvatarId).ToHashSet(StringComparer.Ordinal);
        return new StreamerIdentity(_names.Next(usedNames), _avatars.Next(usedAvatars));
    }

    public async Task RerollAsync(string identityKey)
    {
        StreamerIdentity id;
        Guid? accountId = TryAccountId(identityKey);
        lock (_lock) { id = MakeFresh(); _map[identityKey] = id; }
        await Persist(identityKey, id, accountId).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RerollAllAsync()
    {
        List<string> keys;
        lock (_lock) { keys = _map.Keys.ToList(); _map.Clear(); }
        foreach (var key in keys)
        {
            StreamerIdentity id; lock (_lock) { id = MakeFresh(); _map[key] = id; }
            await Persist(key, id, TryAccountId(key)).ConfigureAwait(false);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetActiveAsync(bool active)
    {
        IsActive = active;
        await _settings.SetStreamerModeAsync(active).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Task Persist(string key, StreamerIdentity id, Guid? accountId)
        => accountId is { } gid ? _persistAccount(gid, id) : _friendStore.SaveAsync(key, id);

    private static Guid? TryAccountId(string key)
        => key.StartsWith("account:", StringComparison.Ordinal) && Guid.TryParse(key.AsSpan(8), out var g) ? g : null;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter FullyQualifiedName~StreamerIdentityProviderTests`
Expected: PASS (5 tests), including the leak scan.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/StreamerMode/IStreamerIdentityProvider.cs src/ROROROblox.Core/StreamerMode/StreamerIdentityProvider.cs src/ROROROblox.Tests/StreamerIdentityProviderTests.cs
git commit -m "feat(streamer): central identity provider + leak-scan test"
```

---

### Task 5: Sticky toggle in settings

**Files:**
- Modify: `src/ROROROblox.Core/IAppSettings.cs` (add the pair)
- Modify: the `IAppSettings` implementation (the JSON-settings class — find via `grep -rl "class .*: IAppSettings"`); add the backing get/set following an existing bool pair like `GetCarefulSquadLaunchAsync`
- Test: extend the existing app-settings test file (find via `grep -rl "GetCarefulSquadLaunchAsync" src/ROROROblox.Tests`)

**Interfaces:**
- Produces on `IAppSettings`: `Task<bool> GetStreamerModeAsync();` / `Task SetStreamerModeAsync(bool enabled);` (defaults false).

- [ ] **Step 1: Add the interface members**

In `IAppSettings.cs`, after the careful-squad pair:

```csharp
    /// <summary>
    /// True when streamer mode is on — the account manager shows fake identities instead of real
    /// names/avatars. Sticky across launches (a streamer wants it reliably on). Defaults to false.
    /// </summary>
    Task<bool> GetStreamerModeAsync();
    Task SetStreamerModeAsync(bool enabled);
```

- [ ] **Step 2: Write the failing test**

Add to the existing settings test class (mirror the careful-squad test):

```csharp
[Fact]
public async Task StreamerMode_DefaultsFalse_ThenPersists()
{
    var settings = NewSettings(); // use the file the existing tests use
    Assert.False(await settings.GetStreamerModeAsync());
    await settings.SetStreamerModeAsync(true);
    Assert.True(await NewSettings().GetStreamerModeAsync()); // survives a fresh instance
}
```

- [ ] **Step 3: Run it to verify failure**

Run: `dotnet test src/ROROROblox.Tests/ --filter FullyQualifiedName~StreamerMode_DefaultsFalse`
Expected: FAIL — member missing.

- [ ] **Step 4: Implement on the settings class**

Add the backing key + get/set exactly like `GetCarefulSquadLaunchAsync`/`SetCarefulSquadLaunchAsync` in the impl class, with JSON key `"streamerMode"`, default `false`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter FullyQualifiedName~StreamerMode_DefaultsFalse`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/IAppSettings.cs <impl-file> <settings-test-file>
git commit -m "feat(streamer): sticky streamer-mode toggle in app settings"
```

---

### Task 6: DI wiring + provider initialization

**Files:**
- Modify: `src/ROROROblox.App/App.xaml.cs` (register the pools, friend store, provider; initialize it after the account list loads)

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces: a singleton `IStreamerIdentityProvider` resolvable from DI, initialized with the loaded accounts' persisted identities.

- [ ] **Step 1: Register the services**

In `ConfigureServices`, near the other Core singletons:

```csharp
services.AddSingleton<ROROROblox.Core.StreamerMode.IStreamerNamePool>(_ => new ROROROblox.Core.StreamerMode.StreamerNamePool());
services.AddSingleton<ROROROblox.Core.StreamerMode.IStreamerAvatarPool>(_ => new ROROROblox.Core.StreamerMode.StreamerAvatarPool());
services.AddSingleton<ROROROblox.Core.StreamerMode.IStreamerIdentityStore>(_ => new ROROROblox.Core.StreamerMode.FileStreamerIdentityStore());
services.AddSingleton<ROROROblox.Core.StreamerMode.IStreamerIdentityProvider>(sp =>
{
    var store = sp.GetRequiredService<IAccountStore>();
    return new ROROROblox.Core.StreamerMode.StreamerIdentityProvider(
        sp.GetRequiredService<ROROROblox.Core.StreamerMode.IStreamerNamePool>(),
        sp.GetRequiredService<ROROROblox.Core.StreamerMode.IStreamerAvatarPool>(),
        sp.GetRequiredService<ROROROblox.Core.StreamerMode.IStreamerIdentityStore>(),
        sp.GetRequiredService<IAppSettings>(),
        persistAccount: (id, identity) => store.UpdateStreamerIdentityAsync(id, identity.FakeName, identity.FakeAvatarId));
});
```

- [ ] **Step 2: Initialize after the account list loads**

In `OnStartup`, after the accounts are listed for the main VM (where `AccountsSnapshot` is first populated), seed the provider:

```csharp
var provider = _services.GetRequiredService<ROROROblox.Core.StreamerMode.IStreamerIdentityProvider>();
var seed = accounts // the IReadOnlyList<Account> already loaded here
    .Where(a => !string.IsNullOrEmpty(a.StreamerName))
    .Select(a => (a.Id, new ROROROblox.Core.StreamerMode.StreamerIdentity(a.StreamerName!, a.StreamerAvatarId ?? "noodle")))
    .ToList();
await provider.InitializeAsync(seed);
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ROROROblox.App/App.xaml.cs
git commit -m "feat(streamer): DI wiring + provider initialization from persisted accounts"
```

---

### Task 7: Route AccountSummary display through the provider

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/AccountSummary.cs` (add `DisplayName2`/avatar display props that consult the provider, OR make `RenderName`/`AvatarUrl` provider-aware — see below)
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs` (give AccountSummary access to the provider; subscribe to `Changed`)

**Interfaces:**
- Consumes: `IStreamerIdentityProvider`.
- Produces: `AccountSummary.RenderName` and `AccountSummary.AvatarDisplaySource` return fake values when the mode is active; both raise `PropertyChanged` on the provider's `Changed`.

Design note: keep the real `DisplayName`/`AvatarUrl` properties as-is (the provider needs the real values to pass through when inactive, and other code may rely on them). Add provider-aware display properties and repoint the XAML bindings to them.

- [ ] **Step 1: Add a provider reference to AccountSummary**

Give `AccountSummary` an `IStreamerIdentityProvider? _identity` (constructor-injected or set by `MainViewModel` right after construction). On the provider's `Changed`, call `OnPropertyChanged(nameof(RenderName)); OnPropertyChanged(nameof(AvatarDisplaySource));`.

- [ ] **Step 2: Make the display properties provider-aware**

```csharp
public string RenderName =>
    _identity is { } p ? p.ForAccount(Id, _localName ?? DisplayName, AvatarUrl).Name
                       : (_localName ?? DisplayName);

public string AvatarDisplaySource =>
    _identity is { } p ? p.ForAccount(Id, _localName ?? DisplayName, AvatarUrl).AvatarSource
                       : AvatarUrl;
```

- [ ] **Step 3: Repoint the avatar bindings in XAML**

In `src/ROROROblox.App/MainWindow.xaml`, change the three `<Image Source="{Binding AvatarUrl}" .../>` (lines ~46, ~267, ~425) to `Source="{Binding AvatarDisplaySource}"`. Name bindings already use `RenderName` where the rename feature routed them; confirm each visible name uses `RenderName`, not `DisplayName`, and fix any that still bind `DisplayName`.

- [ ] **Step 4: Run the suite + manual smoke**

Run: `dotnet test src/ROROROblox.Tests/`
Manual: launch the app, toggle streamer mode (Task 10 wires the control; until then, temporarily default the setting true to smoke it), confirm rows show fake names + fake avatars and flip back live.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/ViewModels/AccountSummary.cs src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.App/MainWindow.xaml
git commit -m "feat(streamer): account rows show fake identity when active"
```

---

### Task 8: Window-title propagation

**Files:**
- Modify: `src/ROROROblox.App/Tray/RobloxWindowDecorator.cs:123` (title from the provider, not `Summary.DisplayName`)

**Interfaces:**
- Consumes: `IStreamerIdentityProvider`.

- [ ] **Step 1: Inject the provider into the decorator**

Add an `IStreamerIdentityProvider` constructor parameter (wire it at the decorator's construction site in `App.xaml.cs`). Subscribe to `Changed` and call the existing "push the latest title for tracked processes" refresh so open windows re-title live.

- [ ] **Step 2: Use the fake name in the title**

Change line 123 from:

```csharp
ApplyTitle(hwnd, $"Roblox - {target.Summary.DisplayName}");
```

to:

```csharp
var shown = _identity.ForAccount(target.Summary.Id, target.Summary.RenderName, target.Summary.AvatarUrl).Name;
ApplyTitle(hwnd, $"Roblox - {shown}");
```

- [ ] **Step 3: Build + manual smoke**

Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj`
Manual: with streamer mode on, launch an account, confirm the Roblox window title reads `Roblox - CaptainNoodle`; toggle off and confirm open windows re-title to the real name.

- [ ] **Step 4: Commit**

```bash
git add src/ROROROblox.App/Tray/RobloxWindowDecorator.cs src/ROROROblox.App/App.xaml.cs
git commit -m "feat(streamer): Roblox window titles use the fake name when active"
```

---

### Task 9: Banked avatar images (design-skill deliverable)

**Files:**
- Create: `src/ROROROblox.App/StreamerMode/Avatars/{id}.png` for each id in `StreamerAvatarPool.DefaultIds`
- Modify: `src/ROROROblox.App/ROROROblox.App.csproj` (include the PNGs as `Resource`)

**This task is not code — it is art produced through the `626labs-design` skill.** Invoke it to generate a cohesive set of silly, on-brand avatar tiles (cyan/magenta family, playful), one per id (`noodle`, `duck`, `potato`, …). Programmatic placeholders are disqualifying (Global Constraints).

- [ ] **Step 1:** Run the `626labs-design` skill to produce the avatar set; export each as `{id}.png` (square, ~256×256) into `src/ROROROblox.App/StreamerMode/Avatars/`.
- [ ] **Step 2:** Ensure they're packed as WPF resources:

```xml
<ItemGroup>
  <Resource Include="StreamerMode/Avatars/*.png" />
</ItemGroup>
```

- [ ] **Step 3:** Manual: with streamer mode on, confirm each account shows a real silly avatar (not a broken-image box).
- [ ] **Step 4: Commit**

```bash
git add src/ROROROblox.App/StreamerMode/Avatars/ src/ROROROblox.App/ROROROblox.App.csproj
git commit -m "feat(streamer): banked silly avatar set (626 design)"
```

---

### Task 10: Toggle UI + reroll controls

**Files:**
- Modify: `src/ROROROblox.App/Tray/TrayService.cs` (add a "Streamer mode" checkable menu item near the Multi-Instance toggle)
- Modify: `src/ROROROblox.App/MainWindow.xaml` (+ its VM) — a streamer-mode switch and a "Reroll all identities" button, and a per-row reroll affordance
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs` — commands that call `provider.SetActiveAsync` / `RerollAsync` / `RerollAllAsync`

**Interfaces:**
- Consumes: `IStreamerIdentityProvider`.

- [ ] **Step 1: Tray toggle**

In `TrayService.BuildMenu` (near the `Multi-Instance` item at line ~119), add a checkable `MenuItem { Header = "Streamer mode", IsCheckable = true }`; its `IsChecked` reflects `provider.IsActive`, its `Click` calls `provider.SetActiveAsync(!provider.IsActive)`. Wire the provider into `TrayService` via constructor (mirror how the existing multi-instance state reaches it).

- [ ] **Step 2: Main-window switch + reroll-all**

Add a `ToggleSwitch` (WPF-UI) bound to a `StreamerModeOn` VM property (get: `provider.IsActive`; set: `await provider.SetActiveAsync(value)`), plus a "Reroll all identities" `Button` bound to a `RerollAllCommand` → `provider.RerollAllAsync()`. Place near the existing header controls. Include one line of copy stating the scope boundary: *"Hides names, avatars, and share links in RoRoRo. Doesn't change what shows inside Roblox."*

- [ ] **Step 3: Per-row reroll**

Add a small "🎲 reroll" affordance on each account row's context menu → `RerollAccountCommand` (param = account id) → `provider.RerollAsync($"account:{id}")`.

- [ ] **Step 4: Refresh on Changed**

Ensure the VM subscribes to `provider.Changed` and raises `OnPropertyChanged(nameof(StreamerModeOn))` so the switch + tray check stay in sync when toggled from either surface.

- [ ] **Step 5: Build + full manual smoke**

Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj`
Manual, the whole loop: flip the tray toggle → rows + open window titles disguise; flip the main-window switch → tray check follows; reroll one row → only that identity changes; reroll all → every identity changes; restart the app → the mode is still on and the identities are the same (sticky + persistent).

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/Tray/TrayService.cs src/ROROROblox.App/MainWindow.xaml src/ROROROblox.App/ViewModels/MainViewModel.cs
git commit -m "feat(streamer): tray toggle + settings switch + reroll controls"
```

---

### Task 11: Share-link redaction + friends masking

**Files:**
- Modify: the private-server share-link UI (find via `grep -rn "ShareUrl" src/ROROROblox.App/MainWindow.xaml src/ROROROblox.App/ViewModels`)
- Modify: the friends picker VM/XAML (find via `grep -rln "Friend" src/ROROROblox.App/ViewModels src/ROROROblox.App`)

**Interfaces:**
- Consumes: `IStreamerIdentityProvider`.

- [ ] **Step 1: Redact share links when active**

Where the share link renders, bind visibility to `!StreamerModeOn` for the raw link and show a `•••` "hidden in streamer mode" pill instead, with a reveal-on-hover or a copy button that still copies the real link (streamer's own use). The link value never appears in the visual tree while active.

- [ ] **Step 2: Mask friend rows**

In the friends picker, route each friend's displayed name/avatar through `provider.ForFriend(friendUserId, realName, realAvatar)` exactly as accounts do in Task 7, refreshing on `Changed`.

- [ ] **Step 3: Manual smoke**

With streamer mode on: the current-server share link shows `•••` (copy still works for you); the friends picker shows fake names + avatars; toggling off restores both live.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(streamer): redact share links + mask friends when active"
```

---

## Self-Review

**Spec coverage:**
- Core idea (fake identities) → Tasks 1–4, 7. ✓
- Scope boundary copy → Task 10 step 2. ✓
- Architecture (one indirection) → Task 4 (provider) + leak-scan test. ✓
- Data model (persist accounts on record, friends in file) → Task 3. ✓
- Coverage map (names, avatars, UID hidden, window title, friends, share link) → Tasks 7, 8, 11. ✓
- Window-title propagation → Task 8. ✓
- Toggle (tray + settings, sticky) → Tasks 5, 10. ✓
- Banked assets (names + avatars via design) → Tasks 1, 2, 9. ✓
- Testing (leak scan, persistence, reroll, toggle passthrough, window title, pool exhaustion) → Tasks 1–4 tests + manual smokes. ✓
- Non-goals (auto-detect, in-game, per-account mode, custom) → not built. ✓

**Open questions from the spec, resolved here:** friends storage = `FileStreamerIdentityStore` (Task 3); reroll re-picks name + avatar together (`MakeFresh`, Task 4).

**Placeholder scan:** UI tasks (7, 10, 11) specify exact files, exact properties, and exact manual verification rather than fabricated XAML — the WPF surfaces are integration points, not unit-testable logic. No "TBD"/"handle edge cases" left.

**Type consistency:** `DisplayIdentity(Name, AvatarSource)`, `StreamerIdentity(FakeName, FakeAvatarId)`, `AccountKey`/`FriendKey`, `ForAccount`/`ForFriend`, `RerollAsync`/`RerollAllAsync`, `GetStreamerModeAsync`/`SetStreamerModeAsync` — consistent across tasks.

**UID note:** "UID hidden" is satisfied because the provider returns no user id and the display props don't expose one; if any XAML binds `RobloxUserId` visibly, hide it under `!StreamerModeOn` in Task 7 step 3.
