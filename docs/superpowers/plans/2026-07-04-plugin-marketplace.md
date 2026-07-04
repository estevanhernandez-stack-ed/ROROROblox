# Plugin marketplace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A catalog-driven plugin marketplace in the Plugins window (browse + one-click install/update over the existing SHA-verified installer), gated to unpackaged builds so the Store MSIX keeps its certified 10.2.2 posture automatically.

**Architecture:** Four testable units — a runtime `IsPackaged()` gate (`IDistributionMode`), a catalog fetch+parse client, a pure update-decision (`MarketplacePlan.Build`), and the `PluginsViewModel`/`PluginsWindow` extension that renders Installed (with update badges) + Available sections only when unpackaged. Install and Update route through the existing `PluginInstaller`; no new install path.

**Tech Stack:** .NET 10 / C# 14, WPF, CsWin32 (Win32 source generator), `System.Text.Json`, xUnit. Spec: [`docs/superpowers/specs/2026-07-04-plugin-marketplace-design.md`](../specs/2026-07-04-plugin-marketplace-design.md).

## Global Constraints

- **Branch:** `feat/plugin-marketplace` (already created off `main`).
- **Solution:** build/test `ROROROblox.slnx` — never the stray `ROROROblox.sln`.
- **Pinned dotnet host:** bare `dotnet` fails `global.json`. Use `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe"` for ALL builds/tests.
- **The gate is the compliance backbone:** the catalog is fetched, and the Available section + update badges render, **only when `!IsPackaged()`**. In a packaged (MSIX) build the Plugins window is byte-for-byte today's behavior. This is not decorative — reversing it ships a Store-policy violation. Never add a build-time flag or config toggle that could enable the marketplace in a packaged build.
- **Install stays SHA-verified through the existing `PluginInstaller`.** The catalog carries metadata + an `installUrl` only; the marketplace is a discovery/trigger surface, not a new install path.
- **Types are `internal`** in `ROROROblox.App`; `ROROROblox.App.csproj` already has `<InternalsVisibleTo Include="ROROROblox.Tests" />`.
- **Copy voice:** builder-to-builder, second person, sentence case; no "empower / leverage / seamlessly / unlock"; no emoji.
- **Catalog URL (verbatim):** `https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/plugins-catalog.json` (the GitHub "latest release asset" URL; the asset is uploaded to the latest release — no app ship needed to update it).
- **No end-to-end against live GitHub.** Catalog client tested against canned JSON via an injected fetch seam.

---

### Task 1: `IDistributionMode` — the packaged/unpackaged gate

**Files:**
- Modify: `src/ROROROblox.App/NativeMethods.txt` (add the CsWin32 API)
- Create: `src/ROROROblox.App/Distribution/IDistributionMode.cs`
- Create: `src/ROROROblox.App/Distribution/Win32DistributionMode.cs`
- Test: `src/ROROROblox.Tests/DistributionModeTests.cs`

**Interfaces:**
- Produces: `internal interface IDistributionMode { bool IsPackaged { get; } }` and `internal sealed class Win32DistributionMode : IDistributionMode`, namespace `ROROROblox.App.Distribution`.

- [ ] **Step 1: Add the Win32 API to the CsWin32 input**

Append one line to `src/ROROROblox.App/NativeMethods.txt` (after `DWMWINDOWATTRIBUTE`):

```
GetCurrentPackageFullName
```

- [ ] **Step 2: Write the failing test**

Create `src/ROROROblox.Tests/DistributionModeTests.cs`:

```csharp
using ROROROblox.App.Distribution;

namespace ROROROblox.Tests;

/// <summary>
/// The xUnit test host is an UNPACKAGED process, so the real Win32-backed distribution mode must
/// report IsPackaged == false here. This proves the GetCurrentPackageFullName P/Invoke is wired
/// correctly (returns APPMODEL_ERROR_NO_PACKAGE when unpackaged). The marketplace-gating logic that
/// CONSUMES IDistributionMode is unit-tested separately with a fake in the PluginsViewModel tests.
/// </summary>
public class DistributionModeTests
{
    [Fact]
    public void Win32DistributionMode_InUnpackagedTestHost_ReportsNotPackaged()
    {
        var mode = new Win32DistributionMode();

        Assert.False(mode.IsPackaged);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter DistributionModeTests`
Expected: FAIL to compile — `IDistributionMode` / `Win32DistributionMode` do not exist.

- [ ] **Step 4: Write the interface**

Create `src/ROROROblox.App/Distribution/IDistributionMode.cs`:

```csharp
namespace ROROROblox.App.Distribution;

/// <summary>
/// Whether RoRoRo is running as an MSIX-packaged app (Microsoft Store or self-signed sideload) or
/// unpackaged (Velopack direct download, <c>dotnet run</c>, F5). The plugin marketplace is active
/// only when unpackaged — this keeps the Store-listed binary inside policy 10.2.2, which forbids
/// reading a curated plugin list from a server. The gate is a property of how the binary is running,
/// not a build-time flag, so it cannot silently regress across releases.
/// </summary>
internal interface IDistributionMode
{
    bool IsPackaged { get; }
}
```

- [ ] **Step 5: Write the Win32 implementation**

Create `src/ROROROblox.App/Distribution/Win32DistributionMode.cs`:

```csharp
using Windows.Win32;
using Windows.Win32.Foundation;

namespace ROROROblox.App.Distribution;

/// <summary>
/// Real distribution-mode probe. <c>GetCurrentPackageFullName</c> returns
/// <see cref="WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE"/> when the process has no package identity
/// (unpackaged) and <c>ERROR_INSUFFICIENT_BUFFER</c> when it does (packaged, because we pass a
/// zero-length buffer just to probe). Anything other than the no-package code means packaged.
/// </summary>
internal sealed class Win32DistributionMode : IDistributionMode
{
    public bool IsPackaged
    {
        get
        {
            uint length = 0;
            unsafe
            {
                var rc = PInvoke.GetCurrentPackageFullName(ref length, null);
                return rc != WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE;
            }
        }
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter DistributionModeTests`
Expected: PASS — 1 test. (If CsWin32 doesn't generate `WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE`, compare against the numeric constant `15700` instead and note it in the report.)

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/NativeMethods.txt src/ROROROblox.App/Distribution/ src/ROROROblox.Tests/DistributionModeTests.cs
git commit -m "feat(marketplace): IDistributionMode + Win32 IsPackaged gate"
```

---

### Task 2: `PluginCatalogEntry` + catalog fetch/parse

**Files:**
- Create: `src/ROROROblox.App/Plugins/PluginCatalogEntry.cs` (record + `PluginCatalogParser`)
- Create: `src/ROROROblox.App/Plugins/PluginCatalogClient.cs`
- Test: `src/ROROROblox.Tests/PluginCatalogTests.cs`

**Interfaces:**
- Produces:
  - `internal sealed record PluginCatalogEntry(string Id, string Name, string Description, string Publisher, string? IconUrl, string LatestVersion, string InstallUrl, string? MinHostVersion)`
  - `internal static class PluginCatalogParser { public static IReadOnlyList<PluginCatalogEntry> Parse(string json); }` — never throws; malformed / missing-required-field / empty → `[]`.
  - `internal sealed class PluginCatalogClient` with ctor `(Func<CancellationToken, Task<string>> fetch)` and `public async Task<IReadOnlyList<PluginCatalogEntry>> FetchAsync(CancellationToken ct = default)` — any fetch/parse failure → `[]`. Second ctor `(HttpClient http, string catalogUrl)` wires the real GET.
- Namespace `ROROROblox.App.Plugins`.

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/PluginCatalogTests.cs`:

```csharp
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class PluginCatalogTests
{
    private const string GoodJson = """
    [
      {
        "id": "626labs.ur-task",
        "name": "Ur Task",
        "description": "Record once, play on any alt.",
        "publisher": "626 Labs",
        "iconUrl": "https://example.invalid/ico.png",
        "latestVersion": "0.4.0",
        "installUrl": "https://github.com/estevanhernandez-stack-ed/rororo-ur-task/releases/latest/download/",
        "minHostVersion": "1.8.0.0"
      }
    ]
    """;

    [Fact]
    public void Parse_WellFormed_ReturnsEntries()
    {
        var entries = PluginCatalogParser.Parse(GoodJson);

        var e = Assert.Single(entries);
        Assert.Equal("626labs.ur-task", e.Id);
        Assert.Equal("Ur Task", e.Name);
        Assert.Equal("0.4.0", e.LatestVersion);
        Assert.Equal("https://github.com/estevanhernandez-stack-ed/rororo-ur-task/releases/latest/download/", e.InstallUrl);
        Assert.Equal("1.8.0.0", e.MinHostVersion);
    }

    [Fact]
    public void Parse_Malformed_ReturnsEmpty()
    {
        Assert.Empty(PluginCatalogParser.Parse("{ not json"));
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmpty()
    {
        Assert.Empty(PluginCatalogParser.Parse("[]"));
    }

    [Fact]
    public void Parse_EntryMissingRequiredField_IsSkipped()
    {
        // Missing installUrl → that entry is dropped (an entry with no install target is useless).
        const string json = """
        [ { "id": "x.y", "name": "X", "description": "d", "publisher": "p", "latestVersion": "1.0" } ]
        """;

        Assert.Empty(PluginCatalogParser.Parse(json));
    }

    [Fact]
    public async Task Client_FetchThrows_ReturnsEmpty()
    {
        var client = new PluginCatalogClient(_ => throw new HttpRequestException("offline"));

        Assert.Empty(await client.FetchAsync());
    }

    [Fact]
    public async Task Client_FetchReturnsGoodJson_ReturnsEntries()
    {
        var client = new PluginCatalogClient(_ => Task.FromResult(GoodJson));

        Assert.Single(await client.FetchAsync());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter PluginCatalogTests`
Expected: FAIL to compile — types don't exist.

- [ ] **Step 3: Write the record + parser**

Create `src/ROROROblox.App/Plugins/PluginCatalogEntry.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.App.Plugins;

/// <summary>
/// One plugin as listed in the remote marketplace catalog. Metadata + an install URL only — the
/// catalog never carries plugin code or hashes; install stays SHA-verified through
/// <see cref="PluginInstaller"/> against the release's own manifest.sha256.
/// </summary>
internal sealed record PluginCatalogEntry(
    string Id,
    string Name,
    string Description,
    string Publisher,
    string? IconUrl,
    string LatestVersion,
    string InstallUrl,
    string? MinHostVersion);

/// <summary>
/// Parses catalog.json (an array of entries) into <see cref="PluginCatalogEntry"/>. Total: malformed
/// JSON, a non-array root, or an entry missing a required field never throws — the bad input (or bad
/// entry) is dropped and the rest returned. The marketplace degrades to "no catalog" on any failure.
/// </summary>
internal static class PluginCatalogParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<PluginCatalogEntry> Parse(string json)
    {
        List<Dto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<Dto>>(json, Options);
        }
        catch (JsonException)
        {
            return [];
        }

        if (dtos is null)
        {
            return [];
        }

        var entries = new List<PluginCatalogEntry>(dtos.Count);
        foreach (var d in dtos)
        {
            // Required fields — an entry missing any of these can't be shown or installed, so drop it
            // rather than surface a half-broken row.
            if (string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name)
                || string.IsNullOrWhiteSpace(d.Description) || string.IsNullOrWhiteSpace(d.Publisher)
                || string.IsNullOrWhiteSpace(d.LatestVersion) || string.IsNullOrWhiteSpace(d.InstallUrl))
            {
                continue;
            }

            entries.Add(new PluginCatalogEntry(
                d.Id!, d.Name!, d.Description!, d.Publisher!, d.IconUrl, d.LatestVersion!, d.InstallUrl!, d.MinHostVersion));
        }
        return entries;
    }

    private sealed class Dto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("publisher")] public string? Publisher { get; set; }
        [JsonPropertyName("iconUrl")] public string? IconUrl { get; set; }
        [JsonPropertyName("latestVersion")] public string? LatestVersion { get; set; }
        [JsonPropertyName("installUrl")] public string? InstallUrl { get; set; }
        [JsonPropertyName("minHostVersion")] public string? MinHostVersion { get; set; }
    }
}
```

- [ ] **Step 4: Write the client**

Create `src/ROROROblox.App/Plugins/PluginCatalogClient.cs`:

```csharp
using System.Net.Http;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Fetches + parses the remote marketplace catalog. Only ever called when RoRoRo is unpackaged (the
/// caller gates on <see cref="Distribution.IDistributionMode"/>). Any failure — offline, non-200,
/// malformed JSON — resolves to an EMPTY list, never an exception: the marketplace simply shows no
/// Available section. Mirrors the remote-config fetch shape of <c>RobloxCompatChecker</c>.
/// </summary>
internal sealed class PluginCatalogClient
{
    private readonly Func<CancellationToken, Task<string>> _fetch;

    // Test seam: inject the raw-JSON fetch directly.
    public PluginCatalogClient(Func<CancellationToken, Task<string>> fetch)
    {
        _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
    }

    // Production: GET the catalog URL and read the body as a string.
    public PluginCatalogClient(HttpClient http, string catalogUrl)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogUrl);
        _fetch = async ct =>
        {
            using var response = await http.GetAsync(catalogUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        };
    }

    public async Task<IReadOnlyList<PluginCatalogEntry>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _fetch(ct).ConfigureAwait(false);
            return PluginCatalogParser.Parse(json);
        }
        catch (Exception)
        {
            // Offline / non-200 / cancelled → no catalog. Never blocks the Plugins window.
            return [];
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter PluginCatalogTests`
Expected: PASS — 6 tests.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginCatalogEntry.cs src/ROROROblox.App/Plugins/PluginCatalogClient.cs src/ROROROblox.Tests/PluginCatalogTests.cs
git commit -m "feat(marketplace): PluginCatalogEntry + fetch/parse client (fail-safe to empty)"
```

---

### Task 3: `MarketplacePlan.Build` — the pure update decision

**Files:**
- Create: `src/ROROROblox.App/Plugins/MarketplacePlan.cs` (views + `Build` + version helper)
- Test: `src/ROROROblox.Tests/MarketplacePlanTests.cs`

**Interfaces:**
- Consumes: `InstalledPlugin` (existing — `Manifest.Id`, `Manifest.Version`), `PluginCatalogEntry` (Task 2).
- Produces (namespace `ROROROblox.App.Plugins`):
  - `internal abstract record PluginUpdateState { public sealed record UpToDate : PluginUpdateState; public sealed record UpdateAvailable(string FromVersion, string ToVersion) : PluginUpdateState; }`
  - `internal sealed record InstalledPluginView(InstalledPlugin Plugin, PluginUpdateState Update, string? UpdateInstallUrl)`
  - `internal sealed record AvailablePluginView(PluginCatalogEntry Entry, bool Installable)`
  - `internal sealed record MarketplaceView(IReadOnlyList<InstalledPluginView> Installed, IReadOnlyList<AvailablePluginView> Available)`
  - `internal static class MarketplacePlan { public static MarketplaceView Build(IReadOnlyList<InstalledPlugin> installed, IReadOnlyList<PluginCatalogEntry> catalog, Version hostVersion); }`

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/MarketplacePlanTests.cs`:

```csharp
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class MarketplacePlanTests
{
    private static PluginManifest Manifest(string id, string version) => new()
    {
        SchemaVersion = 1,
        Id = id,
        Name = id,
        Version = version,
        ContractVersion = "1.0",
        Publisher = "626 Labs",
        Description = "x",
        Capabilities = [],
    };

    private static InstalledPlugin Installed(string id, string version) => new()
    {
        Manifest = Manifest(id, version),
        InstallDir = @"C:\x",
        Consent = new ConsentRecord(id, [], AutostartEnabled: false),
    };

    private static PluginCatalogEntry Entry(string id, string latest, string? minHost = null) =>
        new(id, id, "d", "626 Labs", null, latest, $"https://github.com/x/{id}/releases/latest/download/", minHost);

    [Fact]
    public void Build_InstalledMatchesCatalogSameVersion_UpToDate()
    {
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [Entry("a.b", "1.0.0")], new Version(1, 8, 0, 0));

        var iv = Assert.Single(view.Installed);
        Assert.IsType<PluginUpdateState.UpToDate>(iv.Update);
        Assert.Empty(view.Available); // catalog entry is installed → not in Available
    }

    [Fact]
    public void Build_CatalogNewerThanInstalled_UpdateAvailableWithFromTo()
    {
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [Entry("a.b", "1.3.0")], new Version(1, 8, 0, 0));

        var iv = Assert.Single(view.Installed);
        var upd = Assert.IsType<PluginUpdateState.UpdateAvailable>(iv.Update);
        Assert.Equal("1.0.0", upd.FromVersion);
        Assert.Equal("1.3.0", upd.ToVersion);
        Assert.Equal("https://github.com/x/a.b/releases/latest/download/", iv.UpdateInstallUrl);
    }

    [Fact]
    public void Build_InstalledNotInCatalog_UpToDateNoUrl()
    {
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [], new Version(1, 8, 0, 0));

        var iv = Assert.Single(view.Installed);
        Assert.IsType<PluginUpdateState.UpToDate>(iv.Update);
        Assert.Null(iv.UpdateInstallUrl);
    }

    [Fact]
    public void Build_CatalogEntryNotInstalled_AppearsAvailable()
    {
        var view = MarketplacePlan.Build([], [Entry("a.b", "1.0.0")], new Version(1, 8, 0, 0));

        var av = Assert.Single(view.Available);
        Assert.Equal("a.b", av.Entry.Id);
        Assert.True(av.Installable);
    }

    [Fact]
    public void Build_AvailableNeedsNewerHost_NotInstallable()
    {
        var view = MarketplacePlan.Build([], [Entry("a.b", "1.0.0", minHost: "2.0.0.0")], new Version(1, 8, 0, 0));

        var av = Assert.Single(view.Available);
        Assert.False(av.Installable);
    }

    [Fact]
    public void Build_PrereleaseTagTolerated_ComparesNumericHead()
    {
        // installed 1.0.0, catalog 1.0.0-beta → same numeric head → up to date (no downgrade/upgrade churn).
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [Entry("a.b", "1.0.0-beta")], new Version(1, 8, 0, 0));

        Assert.IsType<PluginUpdateState.UpToDate>(Assert.Single(view.Installed).Update);
    }

    [Fact]
    public void Build_UnparseableCatalogVersion_NoUpdateBadge()
    {
        var view = MarketplacePlan.Build([Installed("a.b", "1.0.0")], [Entry("a.b", "garbage")], new Version(1, 8, 0, 0));

        Assert.IsType<PluginUpdateState.UpToDate>(Assert.Single(view.Installed).Update);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter MarketplacePlanTests`
Expected: FAIL to compile — `MarketplacePlan` and view types don't exist.

- [ ] **Step 3: Write the implementation**

Create `src/ROROROblox.App/Plugins/MarketplacePlan.cs`:

```csharp
namespace ROROROblox.App.Plugins;

/// <summary>An installed plugin's update status relative to the catalog.</summary>
internal abstract record PluginUpdateState
{
    private PluginUpdateState() { }

    public sealed record UpToDate : PluginUpdateState;
    public sealed record UpdateAvailable(string FromVersion, string ToVersion) : PluginUpdateState;
}

internal sealed record InstalledPluginView(InstalledPlugin Plugin, PluginUpdateState Update, string? UpdateInstallUrl);

internal sealed record AvailablePluginView(PluginCatalogEntry Entry, bool Installable);

internal sealed record MarketplaceView(
    IReadOnlyList<InstalledPluginView> Installed,
    IReadOnlyList<AvailablePluginView> Available);

/// <summary>
/// Pure join of installed plugins + catalog + host version into the marketplace's view model:
/// per-installed update status (matched by id; "update available" only when the catalog's
/// latestVersion parses strictly newer than the installed version), and the not-installed catalog
/// entries as Available (each flagged installable unless a parseable minHostVersion exceeds the host).
/// No I/O — the one place marketplace version math lives.
/// </summary>
internal static class MarketplacePlan
{
    public static MarketplaceView Build(
        IReadOnlyList<InstalledPlugin> installed,
        IReadOnlyList<PluginCatalogEntry> catalog,
        Version hostVersion)
    {
        var catalogById = new Dictionary<string, PluginCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog)
        {
            catalogById[entry.Id] = entry; // last wins on a dup id — catalog-authoring bug, not ours to fix here
        }

        var installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedViews = new List<InstalledPluginView>(installed.Count);
        foreach (var plugin in installed)
        {
            installedIds.Add(plugin.Manifest.Id);

            PluginUpdateState state = new PluginUpdateState.UpToDate();
            string? updateUrl = null;
            if (catalogById.TryGetValue(plugin.Manifest.Id, out var entry)
                && TryParseVersion(plugin.Manifest.Version, out var current)
                && TryParseVersion(entry.LatestVersion, out var latest)
                && latest > current)
            {
                state = new PluginUpdateState.UpdateAvailable(plugin.Manifest.Version, entry.LatestVersion);
                updateUrl = entry.InstallUrl;
            }
            installedViews.Add(new InstalledPluginView(plugin, state, updateUrl));
        }

        var availableViews = new List<AvailablePluginView>();
        foreach (var entry in catalog)
        {
            if (installedIds.Contains(entry.Id))
            {
                continue;
            }
            availableViews.Add(new AvailablePluginView(entry, Installable(entry, hostVersion)));
        }

        return new MarketplaceView(installedViews, availableViews);
    }

    // Installable unless a PARSEABLE minHostVersion is newer than the host. An unparseable
    // minHostVersion fails open here — the install-time check in PluginInstaller is the real gate.
    private static bool Installable(PluginCatalogEntry entry, Version hostVersion)
    {
        if (string.IsNullOrWhiteSpace(entry.MinHostVersion))
        {
            return true;
        }
        return !TryParseVersion(entry.MinHostVersion, out var min) || hostVersion >= min;
    }

    // Lenient parse mirroring PluginInstaller.TryParseHostVersion: split on the first '-' and parse
    // the numeric head, so "1.4.3-beta" is treated as "1.4.3".
    private static bool TryParseVersion(string input, out Version version)
    {
        var head = input;
        var dash = head.IndexOf('-');
        if (dash >= 0) head = head[..dash];
        return Version.TryParse(head, out version!);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter MarketplacePlanTests`
Expected: PASS — 7 tests. (If `ConsentRecord`'s constructor differs from `ConsentRecord(id, [], AutostartEnabled: false)`, read `src/ROROROblox.App/Plugins/ConsentStore.cs` for its real shape and adjust the test helper only — the production code doesn't touch ConsentRecord.)

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/MarketplacePlan.cs src/ROROROblox.Tests/MarketplacePlanTests.cs
git commit -m "feat(marketplace): MarketplacePlan.Build pure update/available decision"
```

---

### Task 4: `PluginsViewModel` marketplace integration (the gate in action)

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginsViewModel.cs` (ctor + load + rows + commands)
- Modify: `src/ROROROblox.App/App.xaml.cs` (construct the new deps + pass to the VM)
- Test: `src/ROROROblox.Tests/PluginsViewModelMarketplaceTests.cs`

**Interfaces:**
- Consumes: `IDistributionMode` (Task 1), `PluginCatalogClient` (Task 2), `MarketplacePlan.Build` + view types (Task 3), existing `PluginInstaller`, `PluginProcessSupervisor`, `PluginRegistry`, `PluginRow`.
- Produces on `PluginsViewModel`: `bool MarketplaceEnabled { get; }`, `ObservableCollection<AvailablePluginRow> Available { get; }`, `ICommand UpdatePluginCommand`, `ICommand InstallFromCatalogCommand`. On `PluginRow`: `bool UpdateAvailable`, `string UpdateLabel`, `string? UpdateInstallUrl`. New `internal sealed class AvailablePluginRow`.

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/PluginsViewModelMarketplaceTests.cs`. This is the compliance-critical test: packaged → nothing fetched, nothing available; unpackaged → catalog drives Available + update badges.

```csharp
using System.IO;
using System.Net.Http;
using ROROROblox.App.Distribution;
using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;

namespace ROROROblox.Tests;

public class PluginsViewModelMarketplaceTests : IDisposable
{
    private const string PluginId = "626labs.fake";
    private const string ManifestJson =
        """{"schemaVersion":1,"id":"626labs.fake","name":"Fake Plugin","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":["host.events.account-launched"]}""";

    private readonly string _tempRoot;
    private readonly string _pluginsRoot;
    private readonly ConsentStore _consentStore;
    private readonly PluginRegistry _registry;
    private readonly InstalledPluginsLookupAdapter _adapter;
    private readonly PluginInstaller _installer;
    private readonly PluginProcessSupervisor _supervisor;

    public PluginsViewModelMarketplaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ROROROblox-mktplace-{Guid.NewGuid():N}");
        _pluginsRoot = Path.Combine(_tempRoot, "plugins");
        var pluginDir = Path.Combine(_pluginsRoot, PluginId);
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"), ManifestJson);

        _consentStore = new ConsentStore(Path.Combine(_tempRoot, "consent.dat"));
        _registry = new PluginRegistry(_pluginsRoot, _consentStore);
        _adapter = new InstalledPluginsLookupAdapter(_registry);
        _installer = new PluginInstaller(new HttpClient(), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 8, 0, 0));
        _supervisor = new PluginProcessSupervisor(new FakeStarter());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private const string CatalogWithNewerFake =
        """[{"id":"626labs.fake","name":"Fake Plugin","description":"d","publisher":"626 Labs","latestVersion":"0.2.0","installUrl":"https://github.com/x/fake/releases/latest/download/"},
            {"id":"626labs.other","name":"Other","description":"d","publisher":"626 Labs","latestVersion":"1.0.0","installUrl":"https://github.com/x/other/releases/latest/download/"}]""";

    private PluginsViewModel BuildVm(bool isPackaged) => new(
        _registry, _adapter, _consentStore, _installer, _supervisor,
        _ => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>()),
        new FakeDistributionMode(isPackaged),
        new PluginCatalogClient(_ => Task.FromResult(CatalogWithNewerFake)),
        new Version(1, 8, 0, 0));

    [Fact]
    public async Task Unpackaged_CatalogDrivesAvailableAndUpdateBadge()
    {
        var vm = BuildVm(isPackaged: false);

        await vm.LoadAsync();

        Assert.True(vm.MarketplaceEnabled);
        // The installed fake has a newer catalog version → update badge.
        var installedRow = Assert.Single(vm.Plugins);
        Assert.True(installedRow.UpdateAvailable);
        Assert.Contains("0.2.0", installedRow.UpdateLabel);
        // The other catalog entry is not installed → Available.
        var avail = Assert.Single(vm.Available);
        Assert.Equal("626labs.other", avail.Id);
    }

    [Fact]
    public async Task Packaged_NoCatalogFetch_NoAvailable_NoBadges()
    {
        var vm = BuildVm(isPackaged: true);

        await vm.LoadAsync();

        Assert.False(vm.MarketplaceEnabled);
        Assert.Empty(vm.Available);
        Assert.False(Assert.Single(vm.Plugins).UpdateAvailable); // no update state applied when packaged
    }

    private sealed class FakeDistributionMode(bool packaged) : IDistributionMode
    {
        public bool IsPackaged => packaged;
    }

    private sealed class FakeStarter : IPluginProcessStarter
    {
        public List<(string PluginId, string ExePath)> Started { get; } = new();
        public event Action<int>? ProcessExited;
        public int Start(string pluginId, string exePath) { Started.Add((pluginId, exePath)); return 1; }
        public void Kill(int pid) => ProcessExited?.Invoke(pid);
        public IReadOnlyList<int> FindRunningUnder(string dirPath) => Array.Empty<int>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter PluginsViewModelMarketplaceTests`
Expected: FAIL to compile — the new VM ctor params, `MarketplaceEnabled`, `Available`, `AvailablePluginRow`, and `PluginRow.UpdateAvailable` don't exist.

- [ ] **Step 3: Extend `PluginsViewModel` — ctor + fields**

In `src/ROROROblox.App/Plugins/PluginsViewModel.cs`, add `using ROROROblox.App.Distribution;` at the top. Change the field block + constructor. Find:

```csharp
    private readonly PluginProcessSupervisor _supervisor;
    private readonly Func<PluginManifest, Task<IReadOnlyList<string>?>> _showConsentSheet;
    private readonly ILogger<PluginsViewModel> _log;
```

Replace with:

```csharp
    private readonly PluginProcessSupervisor _supervisor;
    private readonly Func<PluginManifest, Task<IReadOnlyList<string>?>> _showConsentSheet;
    private readonly ILogger<PluginsViewModel> _log;
    private readonly IDistributionMode _distributionMode;
    private readonly PluginCatalogClient _catalogClient;
    private readonly Version _hostVersion;
```

Find the constructor signature + body opening:

```csharp
    public PluginsViewModel(
        PluginRegistry registry,
        InstalledPluginsLookupAdapter registryAdapter,
        ConsentStore consentStore,
        PluginInstaller installer,
        PluginProcessSupervisor supervisor,
        Func<PluginManifest, Task<IReadOnlyList<string>?>> showConsentSheet,
        ILogger<PluginsViewModel>? log = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _registryAdapter = registryAdapter ?? throw new ArgumentNullException(nameof(registryAdapter));
        _consentStore = consentStore ?? throw new ArgumentNullException(nameof(consentStore));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _showConsentSheet = showConsentSheet ?? throw new ArgumentNullException(nameof(showConsentSheet));
        _log = log ?? NullLogger<PluginsViewModel>.Instance;
```

Replace with (adds three params + assignments + the two new commands):

```csharp
    public PluginsViewModel(
        PluginRegistry registry,
        InstalledPluginsLookupAdapter registryAdapter,
        ConsentStore consentStore,
        PluginInstaller installer,
        PluginProcessSupervisor supervisor,
        Func<PluginManifest, Task<IReadOnlyList<string>?>> showConsentSheet,
        IDistributionMode distributionMode,
        PluginCatalogClient catalogClient,
        Version hostVersion,
        ILogger<PluginsViewModel>? log = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _registryAdapter = registryAdapter ?? throw new ArgumentNullException(nameof(registryAdapter));
        _consentStore = consentStore ?? throw new ArgumentNullException(nameof(consentStore));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _showConsentSheet = showConsentSheet ?? throw new ArgumentNullException(nameof(showConsentSheet));
        _distributionMode = distributionMode ?? throw new ArgumentNullException(nameof(distributionMode));
        _catalogClient = catalogClient ?? throw new ArgumentNullException(nameof(catalogClient));
        _hostVersion = hostVersion ?? throw new ArgumentNullException(nameof(hostVersion));
        _log = log ?? NullLogger<PluginsViewModel>.Instance;
        UpdatePluginCommand = new RelayCommand(p => UpdatePluginAsync(p as PluginRow));
        InstallFromCatalogCommand = new RelayCommand(p => InstallFromCatalogAsync(p as AvailablePluginRow));
```

- [ ] **Step 4: Add the `MarketplaceEnabled` flag, `Available` collection, and the commands' properties**

In `PluginsViewModel`, find:

```csharp
    public ObservableCollection<PluginRow> Plugins { get; } = new();
```

Replace with:

```csharp
    public ObservableCollection<PluginRow> Plugins { get; } = new();

    /// <summary>Not-installed catalog plugins. Always empty in a packaged (Store/sideload) build.</summary>
    public ObservableCollection<AvailablePluginRow> Available { get; } = new();

    /// <summary>
    /// The marketplace (catalog fetch, Available section, update badges) is active ONLY when
    /// unpackaged. In a packaged MSIX build this is false and the window stays the paste-URL-only
    /// surface that policy 10.2.2 was certified against. See the design doc §2.
    /// </summary>
    public bool MarketplaceEnabled => !_distributionMode.IsPackaged;

    public ICommand UpdatePluginCommand { get; }
    public ICommand InstallFromCatalogCommand { get; }
```

- [ ] **Step 5: Load the catalog in `LoadAsync` (gated) and apply the view**

In `PluginsViewModel`, find `LoadAsync`:

```csharp
    public async Task LoadAsync()
    {
        var installed = await _registry.ScanAsync().ConfigureAwait(true);
        var running = _supervisor.RunningPids;
        Plugins.Clear();
        foreach (var p in installed)
        {
            Plugins.Add(new PluginRow(p, isRunning: running.ContainsKey(p.Manifest.Id)));
        }
    }
```

Replace with:

```csharp
    public async Task LoadAsync()
    {
        var installed = await _registry.ScanAsync().ConfigureAwait(true);
        var running = _supervisor.RunningPids;

        // Fetch the catalog ONLY when unpackaged — the packaged build must never read a curated list
        // from a server (policy 10.2.2). MarketplacePlan then joins installed + catalog + host version.
        IReadOnlyList<PluginCatalogEntry> catalog = MarketplaceEnabled
            ? await _catalogClient.FetchAsync().ConfigureAwait(true)
            : [];
        var view = MarketplacePlan.Build(installed, catalog, _hostVersion);

        Plugins.Clear();
        foreach (var iv in view.Installed)
        {
            var row = new PluginRow(iv.Plugin, isRunning: running.ContainsKey(iv.Plugin.Manifest.Id));
            if (iv.Update is PluginUpdateState.UpdateAvailable upd)
            {
                row.SetUpdateAvailable($"Update available ({upd.FromVersion} → {upd.ToVersion})", iv.UpdateInstallUrl);
            }
            Plugins.Add(row);
        }

        Available.Clear();
        foreach (var av in view.Available)
        {
            Available.Add(new AvailablePluginRow(av));
        }
    }
```

- [ ] **Step 6: Add the Update + Install-from-catalog handlers**

In `PluginsViewModel`, add these methods next to `InstallAsync` (they mirror its supervisor/installer usage — Update stops nothing extra; the installer already stops the running instance before re-extracting):

```csharp
    private async Task UpdatePluginAsync(PluginRow? row)
    {
        if (row?.UpdateInstallUrl is not { } url) return;
        IsBusy = true;
        StatusBanner = null;
        try
        {
            // The installer stops any running instance out of the plugin's dir before re-extracting,
            // then unpacks the new version. Same SHA-verified path as a fresh install.
            var updated = await _installer.InstallAsync(url, Array.Empty<string>()).ConfigureAwait(true);
            _log.LogInformation("Plugin {PluginId} updated to v{Version}.", updated.Manifest.Id, updated.Manifest.Version);
            _registryAdapter.Refresh();
            await LoadAsync().ConfigureAwait(true);
            _supervisor.Start(updated); // relaunch on the new version
            var newRow = Plugins.FirstOrDefault(p => p.Plugin.Manifest.Id == updated.Manifest.Id);
            if (newRow is not null) newRow.IsRunning = true;
            StatusBanner = $"{updated.Manifest.Name} updated to {updated.Manifest.Version}.";
        }
        catch (Exception ex)
        {
            _log.LogWarning("Plugin update failed (url input): {ExceptionType}.", ex.GetType().Name);
            StatusBanner = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallFromCatalogAsync(AvailablePluginRow? row)
    {
        if (row is null) return;
        // Reuse the exact URL-install path (consent sheet included) — the catalog just pre-fills the URL.
        InstallUrlInput = row.InstallUrl;
        await InstallAsync().ConfigureAwait(true);
    }
```

- [ ] **Step 7: Add the `PluginRow` update fields + the new `AvailablePluginRow`**

In `PluginsViewModel.cs`, find the `PluginRow` class's `IsRunning` property and add the update fields right after it (inside `PluginRow`):

```csharp
    private bool _updateAvailable;
    /// <summary>True when the catalog lists a newer version than the installed one. Drives the
    /// update badge + Update button in the window.</summary>
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set { if (_updateAvailable != value) { _updateAvailable = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateAvailable))); } }
    }

    /// <summary>"Update available (0.1.0 → 0.2.0)" — the badge text. Empty when up to date.</summary>
    public string UpdateLabel { get; private set; } = string.Empty;

    /// <summary>The catalog installUrl to update from. Null when no update is available.</summary>
    public string? UpdateInstallUrl { get; private set; }

    internal void SetUpdateAvailable(string label, string? installUrl)
    {
        UpdateLabel = label;
        UpdateInstallUrl = installUrl;
        UpdateAvailable = true;
    }
```

Then, at the end of `PluginsViewModel.cs` (after the `PluginRow` class), add:

```csharp
/// <summary>
/// A catalog plugin the user does NOT have installed — the Available section's row. Wraps
/// <see cref="AvailablePluginView"/> so XAML binds friendly names.
/// </summary>
internal sealed class AvailablePluginRow
{
    private readonly AvailablePluginView _view;

    public AvailablePluginRow(AvailablePluginView view) => _view = view ?? throw new ArgumentNullException(nameof(view));

    public string Id => _view.Entry.Id;
    public string Name => _view.Entry.Name;
    public string Publisher => _view.Entry.Publisher;
    public string Description => _view.Entry.Description;
    public string Version => _view.Entry.LatestVersion;
    public string InstallUrl => _view.Entry.InstallUrl;
    public bool Installable => _view.Installable;

    /// <summary>"Install" when installable, else the reason it isn't.</summary>
    public string ActionLabel => Installable ? "Install" : $"Needs RoRoRo {_view.Entry.MinHostVersion}+";
}
```

- [ ] **Step 8: Wire the new deps in `App.xaml.cs`**

In `src/ROROROblox.App/App.xaml.cs`, find the `PluginsViewModel` construction (the tray "Open Plugins" handler):

```csharp
            var vm = new ROROROblox.App.Plugins.PluginsViewModel(
                registry, registryAdapter, consentStore, installer, supervisor, showSheet,
                _services.GetRequiredService<ILogger<ROROROblox.App.Plugins.PluginsViewModel>>());
```

Replace with (constructs the gate, the catalog client with the verbatim URL, and passes the host version):

```csharp
            var catalogHttp = _services.GetRequiredService<IHttpClientFactory>().CreateClient();
            var catalogClient = new ROROROblox.App.Plugins.PluginCatalogClient(
                catalogHttp,
                "https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/plugins-catalog.json");
            var vm = new ROROROblox.App.Plugins.PluginsViewModel(
                registry, registryAdapter, consentStore, installer, supervisor, showSheet,
                new ROROROblox.App.Distribution.Win32DistributionMode(),
                catalogClient,
                typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0, 0),
                _services.GetRequiredService<ILogger<ROROROblox.App.Plugins.PluginsViewModel>>());
```

(If `IHttpClientFactory` isn't already `using`-imported or resolvable here, the surrounding method already resolves other services from `_services`; `Microsoft.Extensions.DependencyInjection` + `System.Net.Http` are referenced project-wide. Confirm the build.)

- [ ] **Step 9: Build + run the marketplace tests, then the full suite**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter PluginsViewModelMarketplaceTests`
Expected: PASS — 2 tests.

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test ROROROblox.slnx`
Expected: Build succeeded; full suite green (prior count + Task 1's 1 + Task 2's 6 + Task 3's 7 + Task 4's 2 = prior + 16), 1 integration skipped.

- [ ] **Step 10: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginsViewModel.cs src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/PluginsViewModelMarketplaceTests.cs
git commit -m "feat(marketplace): gate-driven catalog load + update/install-from-catalog in PluginsViewModel"
```

---

### Task 5: `PluginsWindow` — Available section + update badges (manual smoke)

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginsWindow.xaml`

**Interfaces:**
- Consumes: `PluginsViewModel.{MarketplaceEnabled, Available, UpdatePluginCommand, InstallFromCatalogCommand}` and `PluginRow.{UpdateAvailable, UpdateLabel}` + `AvailablePluginRow` (Task 4).

No unit tests — WPF window, manual smoke per house convention. The automated gate is: solution builds + full suite stays green (unchanged from Task 4). The compliance behavior (marketplace hidden when packaged) is unit-tested at the VM level in Task 4 and manually smoked in both modes here.

- [ ] **Step 1: Add the update badge + Update button to the installed-plugin row template**

In `src/ROROROblox.App/Plugins/PluginsWindow.xaml`, inside the installed `PluginRow` `DataTemplate`, find the capability chip Border (the one binding `CapabilitySummary`) and add, immediately AFTER that `</Border>` (still inside the left `StackPanel Grid.Column="0"`):

```xml
                                            <Border Background="{DynamicResource MagentaBrush}"
                                                    CornerRadius="4"
                                                    Padding="6,2"
                                                    HorizontalAlignment="Left"
                                                    Margin="0,6,0,0"
                                                    Visibility="{Binding UpdateAvailable, Converter={StaticResource BoolToVisibilityConverter}}">
                                                <TextBlock Text="{Binding UpdateLabel}"
                                                           FontFamily="JetBrains Mono, Cascadia Mono, Consolas"
                                                           FontSize="10"
                                                           Foreground="{DynamicResource WhiteBrush}" />
                                            </Border>
```

Then, in the right-hand action `StackPanel Grid.Column="1"` of the same template, add an Update button right after the `Remove` button:

```xml
                                            <Button Content="Update"
                                                    Margin="0,8,0,0"
                                                    Padding="12,4"
                                                    Style="{StaticResource CyanCtaButton}"
                                                    FontSize="11"
                                                    HorizontalAlignment="Right"
                                                    Visibility="{Binding UpdateAvailable, Converter={StaticResource BoolToVisibilityConverter}}"
                                                    Command="{Binding DataContext.UpdatePluginCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                    CommandParameter="{Binding}" />
```

- [ ] **Step 2: Add the Available section below the installed list**

The window's outer `Grid` has 5 rows (header, banner, install-row, list `*`, footer). The Available section goes inside the Row-3 list area so it scrolls with the installed list. Find the `ScrollViewer` in `Grid.Row="3"` containing the installed `ItemsControl`. Wrap its content so the installed list and an Available block share one scroll. Replace:

```xml
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <ItemsControl ItemsSource="{Binding Plugins}" Margin="8">
```

with:

```xml
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                  <StackPanel Margin="8">
                    <ItemsControl ItemsSource="{Binding Plugins}">
```

and find the matching close of that `ItemsControl` (the `</ItemsControl>` before `</ScrollViewer>`) and replace:

```xml
                    </ItemsControl>
                </ScrollViewer>
```

with (adds the Available header + list, both gated on `MarketplaceEnabled`, and closes the wrapping StackPanel):

```xml
                    </ItemsControl>

                    <!-- Available (marketplace) — present only when unpackaged. Bound to a VM
                         collection that is always empty in a packaged (Store/sideload) build, and
                         the whole block collapses on MarketplaceEnabled so nothing paints there. -->
                    <StackPanel Visibility="{Binding MarketplaceEnabled, Converter={StaticResource BoolToVisibilityConverter}}"
                                Margin="0,8,0,0">
                        <TextBlock Text="AVAILABLE"
                                   FontFamily="JetBrains Mono, Cascadia Mono, Consolas"
                                   FontSize="10"
                                   Foreground="{DynamicResource MutedTextBrush}"
                                   Margin="0,8,0,6"
                                   Visibility="{Binding Available.Count, Converter={StaticResource ZeroToVisibilityConverterLocal}, ConverterParameter=invert}" />
                        <ItemsControl ItemsSource="{Binding Available}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate DataType="{x:Type plugins:AvailablePluginRow}">
                                    <Border Background="{DynamicResource RowBgBrush}"
                                            CornerRadius="8"
                                            Padding="14,12"
                                            Margin="0,0,0,8">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="Auto" />
                                            </Grid.ColumnDefinitions>
                                            <StackPanel Grid.Column="0">
                                                <StackPanel Orientation="Horizontal">
                                                    <TextBlock Text="{Binding Name}"
                                                               FontSize="14" FontWeight="SemiBold"
                                                               Foreground="{DynamicResource WhiteBrush}" />
                                                    <TextBlock Text="{Binding Version}"
                                                               Margin="8,2,0,0"
                                                               FontFamily="JetBrains Mono, Cascadia Mono, Consolas"
                                                               FontSize="10"
                                                               Foreground="{DynamicResource MutedTextBrush}"
                                                               VerticalAlignment="Center" />
                                                </StackPanel>
                                                <TextBlock Text="{Binding Publisher}"
                                                           FontSize="11"
                                                           Foreground="{DynamicResource MutedTextBrush}"
                                                           Margin="0,2,0,0" />
                                                <TextBlock Text="{Binding Description}"
                                                           FontSize="11"
                                                           Foreground="{DynamicResource MutedTextBrush}"
                                                           TextWrapping="Wrap"
                                                           Opacity="0.85"
                                                           Margin="0,4,0,0" />
                                            </StackPanel>
                                            <Button Grid.Column="1"
                                                    Content="{Binding ActionLabel}"
                                                    Padding="14,6"
                                                    VerticalAlignment="Top"
                                                    Style="{StaticResource CyanCtaButton}"
                                                    FontSize="11"
                                                    IsEnabled="{Binding Installable}"
                                                    Command="{Binding DataContext.InstallFromCatalogCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                    CommandParameter="{Binding}" />
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                  </StackPanel>
                </ScrollViewer>
```

> Note: the `ZeroToVisibilityConverterLocal` with `ConverterParameter=invert` is used to hide the "AVAILABLE" header when the collection is empty. If that converter does not accept an `invert` parameter, drop the `ConverterParameter` and the header always shows when `MarketplaceEnabled` — a cosmetic-only fallback; confirm the converter's contract when wiring and pick whichever compiles, noting the choice in your report.

- [ ] **Step 3: Build the solution**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build ROROROblox.slnx`
Expected: Build succeeded. Fix any XAML binding/converter error before proceeding (the `AvailablePluginRow` `DataType`, the `BoolToVisibilityConverter` resource, and the `plugins:` xmlns are all already in the window).

- [ ] **Step 4: Full suite (regression gate)**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test ROROROblox.slnx`
Expected: unchanged from Task 4 — full suite green, 1 skipped. (No new tests; this task is XAML.)

- [ ] **Step 5: Manual smoke — BOTH distribution modes**

This is the gate for this task. Deferred to the human (needs a real run).

**Unpackaged (marketplace visible):** quit installed RoRoRo from the tray, then `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" run --project src/ROROROblox.App` (an unpackaged run). Open the Plugins window. Verify:
1. An **Available** section appears listing catalog plugins you don't have (once a real `plugins-catalog.json` is on the latest release; until then it's empty and the section is present-but-blank — acceptable).
2. An installed plugin with a newer catalog version shows the magenta **Update available (x → y)** badge + an **Update** button; clicking Update re-installs the new version and relaunches.
3. An Available entry needing a newer host shows **Needs RoRoRo X+**, Install disabled.
4. Offline (disconnect / bad catalog URL) → no Available section error, Installed list + paste-URL still work.

**Packaged (marketplace absent):** build + install the sideload MSIX (`scripts/build-msix.ps1 -Sideload ...`), launch it, open Plugins. Verify the window is exactly today's behavior — **no Available section, no update badges** — and the paste-URL install still works. This is the compliance check.

- [ ] **Step 6: Local-path audit + commit**

Run: `git diff main | grep -inE 'c:\\\\+users|/c/users' | grep -viE 'estevan|localappdata' || echo CLEAN`
Expected: `CLEAN`.

```bash
git add src/ROROROblox.App/Plugins/PluginsWindow.xaml
git commit -m "feat(marketplace): Available section + update badges in the Plugins window (unpackaged-only)"
```

---

## Self-Review

**1. Spec coverage** (by §):
- §1 marketplace = update detection + catalog, unpackaged-only, two sections, paste-URL kept → Tasks 4 (VM) + 5 (window). ✓
- §2 compliance gate (unpackaged-only, runtime, not a build flag) → Task 1 (`IDistributionMode`) + Task 4 (`MarketplaceEnabled` gates the fetch + Available; unit-tested packaged→empty). ✓
- §3.1 `DistributionMode` via CsWin32 `GetCurrentPackageFullName`, injectable seam → Task 1. ✓
- §3.2 `PluginCatalogClient` fetch+parse, failure→empty, entry schema → Task 2. ✓
- §3.3 pure update-decision (version compare, available filter, minHostVersion installable flag) → Task 3. ✓
- §3.4 VM/window extension, Install+Update via existing installer, absent when packaged → Tasks 4 + 5. ✓
- §4 data flow (packaged short-circuit; else fetch→build→render; Update = stop/install/restart via installer) → Task 4 `LoadAsync` + `UpdatePluginAsync`. ✓
- §5 edge cases (packaged, offline→empty, minHostVersion, drift trusts catalog, off-catalog installed no badge, update-fail) → Tasks 2/3/4 tests + Task 5 smoke. ✓
- §6 catalog release-asset URL, metadata-only, SHA-verified install → Task 4 (verbatim URL) + reuse of `PluginInstaller`. ✓
- §7 testing (DistributionMode / client / decision unit-tested; window manual-smoke both modes) → Tasks 1-4 tests + Task 5 smoke. ✓
- §8 out-of-scope (no stats, no UpdateFeed wiring, no third-party catalog, no install-path change) → nothing in the plan adds these. ✓

**2. Placeholder scan:** none — every code step has complete code; the two "confirm the converter / constant" notes name a concrete fallback and are decisions, not gaps.

**3. Type consistency:** `IDistributionMode.IsPackaged` (Task 1) consumed in Task 4. `PluginCatalogEntry(Id,Name,Description,Publisher,IconUrl,LatestVersion,InstallUrl,MinHostVersion)` (Task 2) used by Task 3's `Entry` helper + `MarketplacePlan` + Task 4's `AvailablePluginRow`. `MarketplacePlan.Build(installed, catalog, hostVersion) → MarketplaceView(Installed: InstalledPluginView[], Available: AvailablePluginView[])` with `PluginUpdateState.{UpToDate,UpdateAvailable(from,to)}` (Task 3) consumed in Task 4's `LoadAsync`. `PluginRow.SetUpdateAvailable(label, url)` + `UpdateAvailable`/`UpdateLabel`/`UpdateInstallUrl` (Task 4) bound in Task 5. `AvailablePluginRow.{Name,Publisher,Description,Version,Installable,ActionLabel,InstallUrl}` (Task 4) bound in Task 5. `PluginsViewModel` new ctor param order (…, showConsentSheet, distributionMode, catalogClient, hostVersion, log?) matches the Task 4 test's `BuildVm` and the App.xaml.cs call site. Consistent.
