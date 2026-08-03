# Discord Rich Presence + Join Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RoRoRo publishes a roster-level Discord rich presence, and a clan member clicking Join lands in the same Roblox server.

**Architecture:** Pure computation in `ROROROblox.Core/Discord/` (config store, payload builder, join-secret codec); world-facing shell in `ROROROblox.App/Discord/` (Lachee IPC adapter, presence service, inbound join listener). The presence service subscribes to signals that already exist — `IRobloxProcessTracker`, `IPresenceService`, `IMemoryWatchdog` — and pushes what the builder computes. Inbound joins resolve through `ServerInstanceTargeting.Upgrade` and the normal launch path.

**Tech Stack:** .NET 10, C# 14, WPF, `Lachee.DiscordRichPresence` (MIT), `System.Security.Cryptography.ProtectedData` (DPAPI), xUnit.

**Spec:** [`../specs/2026-08-03-discord-presence-alerts-design.md`](../specs/2026-08-03-discord-presence-alerts-design.md)

## Global Constraints

- **Build:** `dotnet build ROROROblox.slnx`. **Test:** `dotnet test ROROROblox.slnx`. `.slnx` is canonical — a bare `dotnet build` errors MSB1011 while a stray `.sln` exists.
- **Close `ROROROblox.App` before building** — a running instance locks `ROROROblox.Core.dll` (MSB3027). Scoping to the test project does not dodge it.
- **No test may sleep in real time, and none may fail by hanging.** xUnit has no default timeout; a `.WaitAsync(TimeSpan.FromSeconds(5))` ceiling that elapses only on failure is the pattern.
- **No mocking library.** Tests use hand-rolled fakes — follow `MainViewModelTests`' nested `Fake*` classes.
- **Defaults are off.** No presence, no outbound anything, until the user enables it.
- **Streamer mode is honored outbound.** Any account name leaving the machine renders through `IStreamerIdentityProvider.ForAccount`.
- **No Discord failure may affect a Roblox launch.** Presence is a passenger; every Discord path swallows and logs.
- **No telemetry.** Nothing about usage leaves the machine.
- **Conventional commits.** Pre-commit hooks `secret-scan` and `local-path-guard` must pass.
- **Discord application ID** is read from `appsettings.json` (`Discord:ApplicationId`), never hardcoded. The app "ROROROblox" is already registered.

---

### Task 1: `DiscordConfig` + DPAPI-encrypted `DiscordConfigStore`

Supersedes the May design's plaintext `discord-config.json` (spec §3.1): a webhook URL is a bearer credential and belongs in the same envelope as the account vault.

**Files:**
- Create: `src/ROROROblox.Core/Discord/DiscordConfig.cs`
- Create: `src/ROROROblox.Core/Discord/DiscordConfigStore.cs`
- Test: `src/ROROROblox.Tests/Discord/DiscordConfigStoreTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `DiscordConfig` (record: `PresenceEnabled`, `JoinEnabled`, `MineWebhookUrl`, `ClanWebhookUrl`, `DroppedOutDestination`, `MemoryWarningDestination`, `MutedAccountIds`), `AlertDestination` enum (`None`, `Local`, `Mine`, `Clan`), `DiscordConfigStore(string filePath)` with `Task<DiscordConfig> LoadAsync()` and `Task SaveAsync(DiscordConfig)`.

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class DiscordConfigStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rororo-discord-{Guid.NewGuid():N}.dat");

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsDefaultsWithEverythingOff()
    {
        var store = new DiscordConfigStore(_path);

        var config = await store.LoadAsync();

        // For 806 users the safe default is silence.
        Assert.False(config.PresenceEnabled);
        Assert.False(config.JoinEnabled);
        Assert.Null(config.MineWebhookUrl);
        Assert.Equal(AlertDestination.None, config.DroppedOutDestination);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAcrossInstances()
    {
        var store = new DiscordConfigStore(_path);
        await store.SaveAsync(new DiscordConfig
        {
            PresenceEnabled = true,
            MineWebhookUrl = "https://discord.com/api/webhooks/1/abc",
            DroppedOutDestination = AlertDestination.Mine,
        });

        var reloaded = await new DiscordConfigStore(_path).LoadAsync();

        Assert.True(reloaded.PresenceEnabled);
        Assert.Equal("https://discord.com/api/webhooks/1/abc", reloaded.MineWebhookUrl);
        Assert.Equal(AlertDestination.Mine, reloaded.DroppedOutDestination);
    }

    [Fact]
    public async Task SavedFile_DoesNotContainTheWebhookUrlInPlaintext()
    {
        // THE test for this task. Writing the JSON unencrypted makes it fail, and that is
        // exactly what the May implementation did.
        var store = new DiscordConfigStore(_path);
        await store.SaveAsync(new DiscordConfig { MineWebhookUrl = "https://discord.com/api/webhooks/1/SECRET_TOKEN" });

        var raw = await File.ReadAllBytesAsync(_path);
        var asText = System.Text.Encoding.UTF8.GetString(raw);

        Assert.DoesNotContain("SECRET_TOKEN", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("webhooks", asText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        // A stray or wrong-user file must not break app startup. Same rule as ConsentStore.
        await File.WriteAllTextAsync(_path, "this is not a DPAPI envelope");

        var config = await new DiscordConfigStore(_path).LoadAsync();

        Assert.False(config.PresenceEnabled);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~DiscordConfigStoreTests"`
Expected: FAIL — `The type or namespace name 'DiscordConfig' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`DiscordConfig.cs`:

```csharp
namespace ROROROblox.Core.Discord;

/// <summary>Where an alert goes. <see cref="None"/> means the trigger is off entirely.</summary>
public enum AlertDestination
{
    None,
    Local,
    Mine,
    Clan,
}

/// <summary>
/// Discord integration settings. Everything defaults off — nothing leaves the machine until the
/// user turns it on. Webhook URLs are bearer credentials; the store encrypts this whole record
/// with DPAPI (see <see cref="DiscordConfigStore"/>).
/// </summary>
public sealed record DiscordConfig
{
    public bool PresenceEnabled { get; init; }
    public bool JoinEnabled { get; init; }
    public string? MineWebhookUrl { get; init; }
    public string? ClanWebhookUrl { get; init; }
    public AlertDestination DroppedOutDestination { get; init; } = AlertDestination.None;
    public AlertDestination MemoryWarningDestination { get; init; } = AlertDestination.None;
    public IReadOnlyList<Guid> MutedAccountIds { get; init; } = [];
}
```

`DiscordConfigStore.cs` — mirrors `ROROROblox.App/Plugins/ConsentStore.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;

namespace ROROROblox.Core.Discord;

/// <summary>
/// DPAPI-encrypted (per-user, per-machine) Discord settings.
/// <para>
/// The May 2026 design stored this as plaintext JSON, reasoning that a webhook URL was "a
/// clan-shared resource, not a per-user secret." Two things make that wrong: one of the two
/// destinations is a private channel only its owner reads, and a webhook URL is a bearer
/// credential — whoever holds it posts to that channel as you, with no further authentication.
/// Same envelope as accounts.dat.
/// </para>
/// On tamper or a wrong-user envelope, returns defaults rather than throwing: a stray file must
/// not break startup.
/// </summary>
public sealed class DiscordConfigStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public DiscordConfigStore(string filePath)
        => _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

    public async Task<DiscordConfig> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new DiscordConfig();
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<DiscordConfig>(decrypted, JsonOptions) ?? new DiscordConfig();
        }
        catch (CryptographicException) { return new DiscordConfig(); }
        catch (JsonException) { return new DiscordConfig(); }
    }

    public async Task SaveAsync(DiscordConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var json = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllBytesAsync(_filePath, encrypted).ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~DiscordConfigStoreTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Discord/ src/ROROROblox.Tests/Discord/
git commit -m "feat(discord): DPAPI-encrypted config store

Supersedes the May design's plaintext discord-config.json. A webhook URL is a
bearer credential — whoever holds it posts to that channel as you — and one of
the two destinations is a private channel only its owner reads. Same envelope as
accounts.dat; a corrupt or wrong-user file returns defaults rather than breaking
startup."
```

---

### Task 2: `RosterSnapshot` + `PresencePayloadBuilder`

The pure core. What presence *says* is a table of cases, not something to verify by squinting at Discord.

**Files:**
- Create: `src/ROROROblox.Core/Discord/RosterSnapshot.cs`
- Create: `src/ROROROblox.Core/Discord/PresencePayloadBuilder.cs`
- Test: `src/ROROROblox.Tests/Discord/PresencePayloadBuilderTests.cs`

**Interfaces:**
- Consumes: `ServerInstance` (from `ROROROblox.Core`, v1.14).
- Produces: `RosterAccount(Guid AccountId, string DisplayName, bool InGame, string? GameName, ServerInstance? Server, DateTimeOffset? InGameSinceUtc)`, `RosterSnapshot(IReadOnlyList<RosterAccount> Accounts)`, `PresenceFields(string? Details, string? State, DateTimeOffset? StartedAtUtc, ServerInstance? JoinableServer)`, `PresencePayloadBuilder.Build(RosterSnapshot) → PresenceFields?` (null = clear presence).

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class PresencePayloadBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 21, 14, 0, TimeSpan.Zero);
    private static readonly ServerInstance ServerA = new(140403681187145, "job-a");
    private static readonly ServerInstance ServerB = new(140403681187145, "job-b");

    private static RosterAccount InGame(string name, ServerInstance? server, DateTimeOffset? since = null) =>
        new(Guid.NewGuid(), name, InGame: true, GameName: "Pet Simulator 99!", Server: server,
            InGameSinceUtc: since ?? T0);

    [Fact]
    public void Build_NothingRunning_ReturnsNull_SoPresenceIsCleared()
    {
        Assert.Null(PresencePayloadBuilder.Build(new RosterSnapshot([])));
    }

    [Fact]
    public void Build_AllAccountsInOneServer_SaysTheFleetIsTogether()
    {
        // The line only RoRoRo can say. Per-account presence would lose it entirely.
        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", ServerA), InGame("LadyPixel", ServerA), InGame("DoctorDuck", ServerA)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.NotNull(fields);
        Assert.Equal("Pet Simulator 99!", fields.Details);
        Assert.Equal("3 accounts in one server", fields.State);
        Assert.Equal(ServerA, fields.JoinableServer);
    }

    [Fact]
    public void Build_SplitAcrossServers_ReportsHowManyShareTheLargestServer()
    {
        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", ServerA), InGame("LadyPixel", ServerA), InGame("DoctorDuck", ServerB)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.Equal("3 accounts · 2 in this server", fields!.State);
        Assert.Equal(ServerA, fields.JoinableServer);   // the biggest cluster is the joinable one
    }

    [Fact]
    public void Build_SingleAccount_UsesSingularWording()
    {
        var fields = PresencePayloadBuilder.Build(new RosterSnapshot([InGame("CaptainNoodle", ServerA)]));

        Assert.Equal("1 account", fields!.State);
    }

    [Fact]
    public void Build_InGameButNoServerKnown_OffersNoJoin()
    {
        // Privacy or pre-first-poll: we know they are playing, not where. A Join button that
        // cannot work is worse than none.
        var fields = PresencePayloadBuilder.Build(new RosterSnapshot([InGame("CaptainNoodle", server: null)]));

        Assert.Equal("1 account", fields!.State);
        Assert.Null(fields.JoinableServer);
    }

    [Fact]
    public void Build_ElapsedTime_ComesFromTheOldestStillRunningAccount()
    {
        // The run's age, not the newest launch — otherwise the timer resets every time an alt
        // is recycled, which is precisely when it is most interesting.
        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", ServerA, T0),
            InGame("LadyPixel", ServerA, T0.AddMinutes(45))]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.Equal(T0, fields!.StartedAtUtc);
    }

    [Fact]
    public void Build_AccountsOutOfGame_AreNotCounted()
    {
        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", ServerA),
            new RosterAccount(Guid.NewGuid(), "LadyPixel", InGame: false, null, null, null)]);

        Assert.Equal("1 account", PresencePayloadBuilder.Build(snapshot)!.State);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~PresencePayloadBuilderTests"`
Expected: FAIL — `RosterSnapshot` not found.

- [ ] **Step 3: Write minimal implementation**

`RosterSnapshot.cs`:

```csharp
namespace ROROROblox.Core.Discord;

/// <summary>One account as presence sees it. <paramref name="DisplayName"/> is already rendered
/// through streamer mode by the caller — nothing downstream un-masks it.</summary>
public sealed record RosterAccount(
    Guid AccountId,
    string DisplayName,
    bool InGame,
    string? GameName,
    ServerInstance? Server,
    DateTimeOffset? InGameSinceUtc);

/// <summary>The whole roster at one instant. Presence describes the fleet, not one account.</summary>
public sealed record RosterSnapshot(IReadOnlyList<RosterAccount> Accounts);

/// <summary>What Discord should display. Null <see cref="JoinableServer"/> means no Join button.</summary>
public sealed record PresenceFields(
    string? Details,
    string? State,
    DateTimeOffset? StartedAtUtc,
    ServerInstance? JoinableServer);
```

`PresencePayloadBuilder.cs`:

```csharp
namespace ROROROblox.Core.Discord;

/// <summary>
/// Roster snapshot → Discord presence fields. Pure: no clock, no IPC, no I/O, so "what does
/// presence say when three of eight are in one server and five are elsewhere?" is a unit test
/// rather than something to check by eye in Discord.
/// </summary>
public static class PresencePayloadBuilder
{
    public static PresenceFields? Build(RosterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var live = snapshot.Accounts.Where(a => a.InGame).ToList();
        if (live.Count == 0)
        {
            return null;   // nothing running -> clear presence entirely
        }

        // The biggest cluster of accounts sharing one server is what a friend would want to join.
        var biggestCluster = live
            .Where(a => a.Server is not null)
            .GroupBy(a => a.Server!.JobId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        var togetherCount = biggestCluster?.Count() ?? 0;
        var state = live.Count == 1
            ? "1 account"
            : togetherCount == live.Count
                ? $"{live.Count} accounts in one server"
                : togetherCount > 1
                    ? $"{live.Count} accounts · {togetherCount} in this server"
                    : $"{live.Count} accounts";

        return new PresenceFields(
            Details: live.Select(a => a.GameName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
            State: state,
            StartedAtUtc: live.Where(a => a.InGameSinceUtc is not null).Min(a => a.InGameSinceUtc),
            JoinableServer: biggestCluster?.First().Server);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~PresencePayloadBuilderTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Discord/ src/ROROROblox.Tests/Discord/
git commit -m "feat(discord): roster snapshot + pure presence payload builder

Presence describes the fleet, not one account — the one thing only RoRoRo can
say. Elapsed time comes from the oldest still-running account so recycling an alt
does not reset the run timer, and an in-game account with no known server offers
no Join rather than a button that cannot work."
```

---

### Task 3: `JoinSecretCodec`

Lachee caps join secrets at 128 characters — a May-branch discovery that cost a debugging session. The codec is pure and its cap is a test.

**Files:**
- Create: `src/ROROROblox.Core/Discord/JoinSecretCodec.cs`
- Test: `src/ROROROblox.Tests/Discord/JoinSecretCodecTests.cs`

**Interfaces:**
- Consumes: `ServerInstance`, `LaunchTarget`, `PrivateServerCodeKind` (Core).
- Produces: `JoinSecretCodec.Encode(LaunchTarget) → string?`, `JoinSecretCodec.TryDecode(string, out LaunchTarget) → bool`, `JoinSecretCodec.MaxLength = 128`.

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class JoinSecretCodecTests
{
    [Fact]
    public void Encode_GameJob_RoundTripsToTheSameServer()
    {
        var target = new LaunchTarget.GameJob(140403681187145, "fcbe3a36-d655-41da-ba8a-8280f5709568");

        var secret = JoinSecretCodec.Encode(target);
        Assert.NotNull(secret);
        Assert.True(JoinSecretCodec.TryDecode(secret, out var decoded));

        var job = Assert.IsType<LaunchTarget.GameJob>(decoded);
        Assert.Equal(140403681187145, job.PlaceId);
        Assert.Equal("fcbe3a36-d655-41da-ba8a-8280f5709568", job.JobId);
    }

    [Fact]
    public void Encode_PrivateServer_PreservesTheCodeKind()
    {
        // linkCode and accessCode are NOT interchangeable — sending one in the other's slot is
        // permission-denied even on a server you own.
        var target = new LaunchTarget.PrivateServer(8737899170, "SHARE_TOKEN", PrivateServerCodeKind.LinkCode);

        Assert.True(JoinSecretCodec.TryDecode(JoinSecretCodec.Encode(target)!, out var decoded));

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(decoded);
        Assert.Equal(PrivateServerCodeKind.LinkCode, ps.Kind);
        Assert.Equal("SHARE_TOKEN", ps.Code);
    }

    [Fact]
    public void Encode_StaysUnderLacheesSecretCap()
    {
        // Lachee silently rejects SetPresence when a secret exceeds 128 chars — the May branch
        // lost a session to this. A realistic worst case is a long private-server link code.
        var target = new LaunchTarget.PrivateServer(
            long.MaxValue, new string('A', 64), PrivateServerCodeKind.AccessCode);

        var secret = JoinSecretCodec.Encode(target);

        Assert.NotNull(secret);
        Assert.True(secret.Length <= JoinSecretCodec.MaxLength, $"secret was {secret.Length} chars");
    }

    [Fact]
    public void Encode_TargetsWithNoJoinableServer_ReturnNull()
    {
        Assert.Null(JoinSecretCodec.Encode(new LaunchTarget.Home()));
        Assert.Null(JoinSecretCodec.Encode(new LaunchTarget.DefaultGame()));
        Assert.Null(JoinSecretCodec.Encode(new LaunchTarget.Place(8737899170)));  // "any server" is not a server
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("g|notanumber|job")]
    [InlineData("p|123")]              // truncated
    public void TryDecode_Rubbish_ReturnsFalseAndDoesNotThrow(string input)
    {
        Assert.False(JoinSecretCodec.TryDecode(input, out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~JoinSecretCodecTests"`
Expected: FAIL — `JoinSecretCodec` not found.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Globalization;

namespace ROROROblox.Core.Discord;

/// <summary>
/// Encodes a launch target into a Discord join secret and back.
/// <para>
/// Compact by necessity: Lachee's client silently refuses a <c>SetPresence</c> whose secret
/// exceeds 128 characters, which presents as "presence works but Join never appears." Hence
/// pipe-delimited fields rather than JSON.
/// </para>
/// Only targets that name ONE server are encodable. <see cref="LaunchTarget.Place"/> means "this
/// game, any server with room" — joining that is not joining the host.
/// </summary>
public static class JoinSecretCodec
{
    public const int MaxLength = 128;

    public static string? Encode(LaunchTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target switch
        {
            LaunchTarget.GameJob job => $"g|{job.PlaceId}|{job.JobId}",
            LaunchTarget.PrivateServer ps =>
                $"p|{ps.PlaceId}|{(ps.Kind == PrivateServerCodeKind.LinkCode ? "l" : "a")}|{ps.Code}",
            _ => null,
        };
    }

    public static bool TryDecode(string? secret, out LaunchTarget target)
    {
        target = new LaunchTarget.Home();
        if (string.IsNullOrWhiteSpace(secret)) return false;

        var parts = secret.Split('|');
        if (parts.Length < 3) return false;
        if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var placeId) || placeId <= 0)
        {
            return false;
        }

        switch (parts[0])
        {
            case "g" when !string.IsNullOrWhiteSpace(parts[2]):
                target = new LaunchTarget.GameJob(placeId, parts[2]);
                return true;
            case "p" when parts.Length == 4 && !string.IsNullOrWhiteSpace(parts[3]):
                target = new LaunchTarget.PrivateServer(
                    placeId, parts[3],
                    parts[2] == "l" ? PrivateServerCodeKind.LinkCode : PrivateServerCodeKind.AccessCode);
                return true;
            default:
                return false;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~JoinSecretCodecTests"`
Expected: PASS, 8 tests (4 facts + 4 theory cases).

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Discord/JoinSecretCodec.cs src/ROROROblox.Tests/Discord/JoinSecretCodecTests.cs
git commit -m "feat(discord): compact join-secret codec with Lachee's 128-char cap as a test

Lachee silently refuses SetPresence when the secret exceeds 128 characters, which
presents as 'presence works but Join never appears' — the May branch lost a
session to it. Pipe-delimited rather than JSON for that reason, with the cap
asserted against a worst-case private-server code.

Place targets encode to null on purpose: 'this game, any server' is not a server,
and a Join button that matchmakes the clicker elsewhere is worse than no button."
```

---

### Task 4: `IDiscordRpcClient` seam + Lachee adapter

Harvested from the May branch — the seam is what makes the presence service testable without a live Discord pipe.

**Files:**
- Create: `src/ROROROblox.App/Discord/Internal/IDiscordRpcClient.cs`
- Create: `src/ROROROblox.App/Discord/Internal/LacheeDiscordRpcClientAdapter.cs`
- Modify: `src/ROROROblox.App/ROROROblox.App.csproj` (add `Lachee.DiscordRichPresence`)
- Modify: `src/ROROROblox.App/appsettings.json` (add `Discord:ApplicationId`)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `IDiscordRpcClient` (`IsInitialized`, `Initialize()`, `Deinitialize()`, `SetPresence(DiscordPresencePayload)`, `ClearPresence()`, events `JoinRequested(string secret)`, `ConnectionFailed`, `Ready`, `Errored(string)`), `DiscordPresencePayload(string? State, string? Details, DateTimeOffset? StartedAtUtc, string? LargeImageKey, string? LargeImageText, DiscordPresenceParty? Party)`, `DiscordPresenceParty(string PartyId, string JoinSecret, int Size, int MaxSize)`.

- [ ] **Step 1: Add the package and the application id**

```bash
dotnet add src/ROROROblox.App/ROROROblox.App.csproj package Lachee.DiscordRichPresence
```

Add to `src/ROROROblox.App/appsettings.json`:

```json
{
  "Discord": {
    "ApplicationId": ""
  }
}
```

Leave it empty. Task 6 treats an unset id as "feature unavailable" — never a crash.

- [ ] **Step 2: Write the interface (no test — it is a declaration)**

```csharp
namespace ROROROblox.App.Discord.Internal;

/// <summary>
/// Test seam over Lachee's concrete <c>DiscordRpcClient</c>, which is IPC-bound and unfakeable.
/// The presence service consumes this interface so its tests can drive connect, drop, reconnect,
/// and Join without touching the local Discord pipe.
/// </summary>
internal interface IDiscordRpcClient : IDisposable
{
    bool IsInitialized { get; }
    void Initialize();
    void Deinitialize();
    void SetPresence(DiscordPresencePayload payload);
    void ClearPresence();

    /// <summary>Discord forwarded a Join click. Payload is the join secret.</summary>
    event EventHandler<string>? JoinRequested;

    /// <summary>The IPC pipe dropped, or the initial connect failed.</summary>
    event EventHandler? ConnectionFailed;

    /// <summary>A successful (re)connect.</summary>
    event EventHandler? Ready;

    /// <summary>Discord rejected something — bad payload, missing asset key, rate limit.</summary>
    event EventHandler<string>? Errored;
}

/// <summary>DTO so the seam stays free of Lachee types.</summary>
internal sealed record DiscordPresencePayload(
    string? State,
    string? Details,
    DateTimeOffset? StartedAtUtc,
    string? LargeImageKey,
    string? LargeImageText,
    DiscordPresenceParty? Party);

internal sealed record DiscordPresenceParty(string PartyId, string JoinSecret, int Size, int MaxSize);
```

- [ ] **Step 3: Write the adapter**

```csharp
using DiscordRPC;
using DiscordRPC.Message;
using Microsoft.Extensions.Logging;

namespace ROROROblox.App.Discord.Internal;

/// <summary>
/// Maps <see cref="IDiscordRpcClient"/> onto Lachee's client.
/// <para>
/// Two non-obvious requirements, both learned the hard way on the May branch: the client must
/// <c>Subscribe(EventType.Join)</c> or Discord never delivers the join command however correct
/// the presence looks, and the <c>roblox-rororo:</c> URI scheme must be registered BEFORE
/// Discord will accept a presence carrying secrets at all.
/// </para>
/// </summary>
internal sealed class LacheeDiscordRpcClientAdapter : IDiscordRpcClient
{
    private readonly string _applicationId;
    private readonly ILogger _log;
    private DiscordRpcClient? _client;

    public LacheeDiscordRpcClientAdapter(string applicationId, ILogger log)
    {
        _applicationId = applicationId;
        _log = log;
    }

    public bool IsInitialized => _client?.IsInitialized == true;

    public event EventHandler<string>? JoinRequested;
    public event EventHandler? ConnectionFailed;
    public event EventHandler? Ready;
    public event EventHandler<string>? Errored;

    public void Initialize()
    {
        if (_client is not null) return;

        _client = new DiscordRpcClient(_applicationId);
        _client.OnReady += (_, _) => Ready?.Invoke(this, EventArgs.Empty);
        _client.OnConnectionFailed += (_, _) => ConnectionFailed?.Invoke(this, EventArgs.Empty);
        _client.OnClose += (_, _) => ConnectionFailed?.Invoke(this, EventArgs.Empty);
        _client.OnError += (_, e) => Errored?.Invoke(this, e.Message);
        _client.OnJoin += (_, JoinMessage e) => JoinRequested?.Invoke(this, e.Secret);

        _client.Initialize();
        // Without this the Join button renders and its click is never delivered.
        _client.Subscribe(EventType.Join);
    }

    public void Deinitialize()
    {
        _client?.Deinitialize();
        _client?.Dispose();
        _client = null;
    }

    public void SetPresence(DiscordPresencePayload payload)
    {
        if (_client is null) return;
        _client.SetPresence(new RichPresence
        {
            State = payload.State,
            Details = payload.Details,
            Timestamps = payload.StartedAtUtc is { } t ? new Timestamps(t.UtcDateTime) : null,
            Assets = new Assets
            {
                LargeImageKey = payload.LargeImageKey,
                LargeImageText = payload.LargeImageText,
            },
            Party = payload.Party is { } p ? new Party { ID = p.PartyId, Size = p.Size, Max = p.MaxSize } : null,
            Secrets = payload.Party is { } s ? new Secrets { JoinSecret = s.JoinSecret } : null,
        });
    }

    public void ClearPresence() => _client?.ClearPresence();

    public void Dispose() => Deinitialize();
}
```

- [ ] **Step 4: Verify it builds**

Run: `dotnet build ROROROblox.slnx`
Expected: 0 errors. (Close `ROROROblox.App` first if it is running.)

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Discord/ src/ROROROblox.App/ROROROblox.App.csproj src/ROROROblox.App/appsettings.json
git commit -m "feat(discord): Lachee IPC adapter behind a test seam

Harvested from feat/discord-clan-coordination. Two requirements that are invisible
until they bite: Subscribe(EventType.Join) or the join command is never delivered
however correct the presence looks, and the URI scheme must be registered before
Discord accepts a presence carrying secrets at all.

Application id comes from appsettings.json and is empty by default; an unset id
means the feature is unavailable, never a crash."
```

---

### Task 5: `DiscordPresenceService`

**Files:**
- Create: `src/ROROROblox.App/Discord/DiscordPresenceService.cs`
- Test: `src/ROROROblox.Tests/Discord/DiscordPresenceServiceTests.cs`

**Interfaces:**
- Consumes: `IDiscordRpcClient` (Task 4), `PresencePayloadBuilder`/`RosterSnapshot` (Task 2), `JoinSecretCodec` (Task 3), `DiscordConfig` (Task 1).
- Produces: `DiscordPresenceService(IDiscordRpcClient client, Func<RosterSnapshot> rosterProvider, ILogger log)` with `Task ApplyAsync(DiscordConfig config)`, `void Refresh()`, `event EventHandler<LaunchTarget>? JoinRequested`, `string StatusLine { get; }`.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.App.Discord.Internal;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class DiscordPresenceServiceTests
{
    private sealed class FakeRpcClient : IDiscordRpcClient
    {
        public List<DiscordPresencePayload> Presences { get; } = [];
        public int ClearCount { get; private set; }
        public bool IsInitialized { get; private set; }
        public void Initialize() => IsInitialized = true;
        public void Deinitialize() => IsInitialized = false;
        public void SetPresence(DiscordPresencePayload p) => Presences.Add(p);
        public void ClearPresence() => ClearCount++;
        public void Dispose() { }
        public event EventHandler<string>? JoinRequested;
        public event EventHandler? ConnectionFailed;
        public event EventHandler? Ready;
        public event EventHandler<string>? Errored;
        public void RaiseJoin(string secret) => JoinRequested?.Invoke(this, secret);
        public void RaiseConnectionFailed() => ConnectionFailed?.Invoke(this, EventArgs.Empty);
        public void RaiseReady() => Ready?.Invoke(this, EventArgs.Empty);
    }

    private static readonly ServerInstance ServerA = new(140403681187145, "job-a");

    private static RosterSnapshot Roster(params RosterAccount[] accounts) => new(accounts);

    private static RosterAccount Live(string name) =>
        new(Guid.NewGuid(), name, InGame: true, "Pet Simulator 99!", ServerA, DateTimeOffset.UtcNow);

    [Fact]
    public async Task ApplyAsync_PresenceDisabled_NeverInitializesTheClient()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("CaptainNoodle")), NullLogger.Instance);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = false });

        Assert.False(rpc.IsInitialized);
        Assert.Empty(rpc.Presences);
    }

    [Fact]
    public async Task ApplyAsync_PresenceEnabled_PushesTheRosterState()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A"), Live("B")), NullLogger.Instance);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });

        var pushed = Assert.Single(rpc.Presences);
        Assert.Equal("Pet Simulator 99!", pushed.Details);
        Assert.Equal("2 accounts in one server", pushed.State);
        Assert.NotNull(pushed.Party);
    }

    [Fact]
    public async Task ApplyAsync_JoinDisabled_PublishesPresenceWithoutAJoinSecret()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = false });

        Assert.Null(Assert.Single(rpc.Presences).Party);
    }

    [Fact]
    public async Task Refresh_NothingRunning_ClearsPresenceRatherThanShowingStaleState()
    {
        var rpc = new FakeRpcClient();
        var roster = Roster(Live("A"));
        var svc = new DiscordPresenceService(rpc, () => roster, NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        roster = Roster();          // everything closed
        svc.Refresh();

        Assert.Equal(1, rpc.ClearCount);
    }

    [Fact]
    public async Task JoinRequested_DecodesTheSecretIntoALaunchTarget()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });
        LaunchTarget? received = null;
        svc.JoinRequested += (_, t) => received = t;

        rpc.RaiseJoin("g|140403681187145|job-a");

        var job = Assert.IsType<LaunchTarget.GameJob>(received);
        Assert.Equal("job-a", job.JobId);
    }

    [Fact]
    public async Task JoinRequested_UndecodableSecret_IsIgnoredNotThrown()
    {
        // A malformed secret from anywhere must not take the app down.
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });
        var fired = false;
        svc.JoinRequested += (_, _) => fired = true;

        rpc.RaiseJoin("not-a-secret");

        Assert.False(fired);
    }

    [Fact]
    public async Task ConnectionFailed_ReportsItInTheStatusLineWithoutThrowing()
    {
        // Discord not running is the common case, not an error state.
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        rpc.RaiseConnectionFailed();

        Assert.Contains("isn't running", svc.StatusLine, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~DiscordPresenceServiceTests"`
Expected: FAIL — `DiscordPresenceService` not found.

- [ ] **Step 3: Write minimal implementation**

```csharp
using Microsoft.Extensions.Logging;
using ROROROblox.App.Discord.Internal;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// Owns the Discord IPC connection and keeps presence in step with the roster.
/// <para>
/// Everything here is degrade-safe by contract: Discord not being installed or not running is the
/// normal case, not an error, and no Discord failure may affect a Roblox launch. Presence is a
/// passenger.
/// </para>
/// </summary>
public sealed class DiscordPresenceService : IDisposable
{
    private readonly IDiscordRpcClient _client;
    private readonly Func<RosterSnapshot> _roster;
    private readonly ILogger _log;
    private DiscordConfig _config = new();

    public DiscordPresenceService(IDiscordRpcClient client, Func<RosterSnapshot> rosterProvider, ILogger log)
    {
        _client = client;
        _roster = rosterProvider;
        _log = log;
        _client.JoinRequested += OnJoinRequested;
        _client.ConnectionFailed += (_, _) => StatusLine = "Discord isn't running — presence starts when it does.";
        _client.Ready += (_, _) => StatusLine = "Connected to Discord.";
        _client.Errored += (_, msg) => _log.LogDebug("Discord rejected a presence update: {Message}", msg);
    }

    /// <summary>Plain-language state for the Settings panel. Never a stack trace.</summary>
    public string StatusLine { get; private set; } = "Presence is off.";

    /// <summary>Fires when a clan member clicks Join. The target is already decoded.</summary>
    public event EventHandler<LaunchTarget>? JoinRequested;

    public Task ApplyAsync(DiscordConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        try
        {
            if (!config.PresenceEnabled)
            {
                if (_client.IsInitialized) { _client.ClearPresence(); _client.Deinitialize(); }
                StatusLine = "Presence is off.";
                return Task.CompletedTask;
            }

            if (!_client.IsInitialized) { _client.Initialize(); }
            Refresh();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Discord presence apply failed; continuing without presence.");
            StatusLine = "Discord isn't running — presence starts when it does.";
        }
        return Task.CompletedTask;
    }

    /// <summary>Recompute and push. Safe to call from any roster-changing event.</summary>
    public void Refresh()
    {
        if (!_config.PresenceEnabled || !_client.IsInitialized) return;

        try
        {
            var fields = PresencePayloadBuilder.Build(_roster());
            if (fields is null) { _client.ClearPresence(); return; }

            DiscordPresenceParty? party = null;
            if (_config.JoinEnabled && fields.JoinableServer is { } server)
            {
                var secret = JoinSecretCodec.Encode(new LaunchTarget.GameJob(server.PlaceId, server.JobId));
                if (secret is not null)
                {
                    party = new DiscordPresenceParty($"rororo-{server.JobId}", secret, 1, 8);
                }
            }

            _client.SetPresence(new DiscordPresencePayload(
                fields.State, fields.Details, fields.StartedAtUtc,
                LargeImageKey: "active_large", LargeImageText: "RoRoRo", Party: party));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Discord presence refresh failed; leaving the last state in place.");
        }
    }

    private void OnJoinRequested(object? sender, string secret)
    {
        if (!JoinSecretCodec.TryDecode(secret, out var target))
        {
            _log.LogDebug("Ignoring an undecodable Discord join secret.");
            return;
        }
        JoinRequested?.Invoke(this, target);
    }

    public void Dispose() => _client.Dispose();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~DiscordPresenceServiceTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Discord/DiscordPresenceService.cs src/ROROROblox.Tests/Discord/DiscordPresenceServiceTests.cs
git commit -m "feat(discord): presence service over the IPC seam

Degrade-safe by contract: Discord missing or closed is the normal case and shows
as a plain-language status line, never a dialog. An undecodable join secret is
ignored rather than thrown, and every push is wrapped — no Discord failure may
affect a Roblox launch."
```

---

### Task 6: Roster wiring + streamer-mode masking

Where the roster snapshot actually comes from, and the trap the product should catch: a streamer who flips streamer mode on and still broadcasts real alt names.

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs` (add `BuildRosterSnapshot()`; call `Refresh()` from the presence/process handlers)
- Test: `src/ROROROblox.Tests/Discord/RosterSnapshotProjectionTests.cs`

**Interfaces:**
- Consumes: `RosterAccount`/`RosterSnapshot` (Task 2), `AccountSummary` (App), `IStreamerIdentityProvider` (Core).
- Produces: `MainViewModel.BuildRosterSnapshot() → RosterSnapshot` (internal, for tests).

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class RosterSnapshotProjectionTests
{
    [Fact]
    public void BuildRosterSnapshot_UsesRenderName_SoStreamerModeIsHonoredOutbound()
    {
        // THE test for this task. Streamer mode hides names INSIDE RoRoRo; if presence read
        // DisplayName instead of RenderName, a streamer would flip it on, feel covered, and
        // broadcast their real alt names to everyone watching their Discord. Same promise,
        // honored on the way out the door.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");

        var snapshot = vm.BuildRosterSnapshot();

        var account = Assert.Single(snapshot.Accounts);
        Assert.Equal("CaptainNoodle", account.DisplayName);
        Assert.DoesNotContain("este_real", account.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRosterSnapshot_CarriesTheServerFromPresence()
    {
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.CurrentServer = new ServerInstance(140403681187145, "job-a");

        var account = Assert.Single(vm.BuildRosterSnapshot().Accounts);

        Assert.Equal("job-a", account.Server!.JobId);
    }

    [Fact]
    public void BuildRosterSnapshot_OutOfGameAccounts_AreMarkedNotInGame()
    {
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.PresenceState = UserPresenceType.Offline;

        Assert.False(Assert.Single(vm.BuildRosterSnapshot().Accounts).InGame);
    }
}
```

Add the harness alongside it (`src/ROROROblox.Tests/Discord/DiscordTestHarness.cs`) — a trimmed copy of `MainViewModelTests.Build()` that returns the VM plus one wired row with a streamer identity attached. Copy the fake set from `MainViewModelTests`; do not try to share its private nested classes.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~RosterSnapshotProjectionTests"`
Expected: FAIL — `BuildRosterSnapshot` not found.

- [ ] **Step 3: Write minimal implementation**

In `MainViewModel`:

```csharp
    /// <summary>
    /// Project the live rows into the shape Discord presence consumes. Internal so the projection
    /// — especially the streamer-mode rule — is unit-testable without a Discord pipe.
    /// <para>
    /// Names come from <see cref="AccountSummary.RenderName"/>, never <c>DisplayName</c>: streamer
    /// mode has to hold on the way OUT of the app, or it is a promise that only covers the window
    /// the user is already looking at.
    /// </para>
    /// </summary>
    internal RosterSnapshot BuildRosterSnapshot() => new(
        Accounts.Select(a => new RosterAccount(
            a.Id,
            a.RenderName,
            a.InGame,
            a.CurrentGameName,
            a.CurrentServer,
            a.InGameSinceUtc)).ToList());
```

Then call `_discordPresence?.Refresh()` at the end of `ApplyPresence`, `OnProcessAttached`, and `OnProcessExited` — the three places roster state changes.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~RosterSnapshotProjectionTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.Tests/Discord/
git commit -m "feat(discord): roster projection honoring streamer mode outbound

Presence reads RenderName, never DisplayName. Streamer mode hides names inside
RoRoRo; without this a streamer flips it on, feels covered, and broadcasts real
alt names to everyone watching their Discord. Same promise, honored on the way
out the door."
```

---

### Task 7: URI scheme registration + inbound join

**Files:**
- Create: `src/ROROROblox.App/Discord/JoinUriScheme.cs`
- Create: `src/ROROROblox.App/Discord/JoinUriParser.cs`
- Modify: `src/ROROROblox.App/AppLifecycle/SingleInstanceGuard.cs` (relay the URI to the running instance)
- Modify: `src/ROROROblox.App/App.xaml.cs` (register on startup; handle the relayed URI)
- Test: `src/ROROROblox.Tests/Discord/JoinUriParserTests.cs`

**Interfaces:**
- Consumes: `JoinSecretCodec` (Task 3).
- Produces: `JoinUriScheme.Register(string exePath)`, `JoinUriScheme.SchemeName = "roblox-rororo"`, `JoinUriParser.TryParse(string[] args, out LaunchTarget target) → bool`.

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.App.Discord;
using ROROROblox.Core;

namespace ROROROblox.Tests.Discord;

public class JoinUriParserTests
{
    [Fact]
    public void TryParse_JoinUri_ExtractsTheTarget()
    {
        var args = new[] { "ROROROblox.App.exe", "roblox-rororo://join/g%7C140403681187145%7Cjob-a" };

        Assert.True(JoinUriParser.TryParse(args, out var target));

        Assert.Equal("job-a", Assert.IsType<LaunchTarget.GameJob>(target).JobId);
    }

    [Fact]
    public void TryParse_NormalStartupArgs_ReturnsFalse()
    {
        Assert.False(JoinUriParser.TryParse(["ROROROblox.App.exe"], out _));
        Assert.False(JoinUriParser.TryParse(["ROROROblox.App.exe", "--tray"], out _));
    }

    [Fact]
    public void TryParse_EmptyArgs_ReturnsFalseInsteadOfThrowing()
    {
        // The registry entry historically shipped without %1, so the app received NO argument
        // where it expected a URI. That regression must not crash startup.
        Assert.False(JoinUriParser.TryParse([], out _));
    }

    [Fact]
    public void TryParse_UriWithGarbagePayload_ReturnsFalse()
    {
        Assert.False(JoinUriParser.TryParse(["exe", "roblox-rororo://join/not-a-secret"], out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~JoinUriParserTests"`
Expected: FAIL — `JoinUriParser` not found.

- [ ] **Step 3: Write minimal implementation**

`JoinUriParser.cs`:

```csharp
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// Pulls a join target out of process arguments. Discord launches us with
/// <c>roblox-rororo://join/&lt;url-encoded secret&gt;</c> when a clan member clicks Join.
/// </summary>
public static class JoinUriParser
{
    private const string Prefix = "roblox-rororo://join/";

    public static bool TryParse(string[] args, out LaunchTarget target)
    {
        target = new LaunchTarget.Home();
        if (args is null || args.Length == 0) return false;

        foreach (var arg in args)
        {
            if (arg is null || !arg.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var secret = Uri.UnescapeDataString(arg[Prefix.Length..].TrimEnd('/'));
            return JoinSecretCodec.TryDecode(secret, out target);
        }
        return false;
    }
}
```

`JoinUriScheme.cs`:

```csharp
using Microsoft.Win32;

namespace ROROROblox.App.Discord;

/// <summary>
/// Registers the <c>roblox-rororo:</c> scheme under HKCU (no elevation needed).
/// <para>
/// Two things that look optional and are not: the command value must end in <c>"%1"</c> or
/// Windows launches us with no argument at all and every inbound join silently does nothing;
/// and Discord refuses to accept a presence carrying join secrets unless the scheme is
/// registered first, so this runs before the first SetPresence.
/// </para>
/// </summary>
public static class JoinUriScheme
{
    public const string SchemeName = "roblox-rororo";

    public static void Register(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SchemeName}");
        key.SetValue("", $"URL:{SchemeName}");
        key.SetValue("URL Protocol", "");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{exePath}\" \"%1\"");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~JoinUriParserTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Discord/ src/ROROROblox.Tests/Discord/JoinUriParserTests.cs
git commit -m "feat(discord): URI scheme registration + inbound join parsing

The command value must end in \"%1\" or Windows launches us with no argument and
every inbound join silently does nothing — a May-branch regression with a test of
its own now. Registration also has to happen before the first SetPresence, since
Discord refuses presences carrying secrets from an unregistered scheme."
```

---

### Task 8: Private-server join warning + launch dispatch

**Files:**
- Create: `src/ROROROblox.App/Modals/JoinRequestWindow.xaml` + `.xaml.cs`
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs` (`HandleDiscordJoinAsync`)
- Test: `src/ROROROblox.Tests/Discord/JoinDispatchTests.cs`

**Interfaces:**
- Consumes: `DiscordPresenceService.JoinRequested` (Task 5), `ServerInstanceTargeting` (Core, v1.14).
- Produces: `MainViewModel.HandleDiscordJoinAsync(LaunchTarget target, Func<string, bool> confirm) → Task<bool>`.

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core;

namespace ROROROblox.Tests.Discord;

public class JoinDispatchTests
{
    [Fact]
    public async Task HandleDiscordJoinAsync_PrivateServer_WarnsBeforeLaunching()
    {
        // Este's call: private servers are joinable, and the joiner is told they may bounce.
        // Roblox does the permission check server-side, so a mystery failure is the alternative.
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        string? shown = null;
        var target = new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode);

        await vm.HandleDiscordJoinAsync(target, msg => { shown = msg; return true; });

        Assert.NotNull(shown);
        Assert.Contains("denied entry", shown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleDiscordJoinAsync_PublicServer_LaunchesWithoutAWarning()
    {
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        var confirmed = false;

        await vm.HandleDiscordJoinAsync(
            new LaunchTarget.GameJob(140403681187145, "job-a"), _ => { confirmed = true; return true; });

        Assert.False(confirmed);
    }

    [Fact]
    public async Task HandleDiscordJoinAsync_UserDeclinesTheWarning_LaunchesNothing()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var target = new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode);

        var ok = await vm.HandleDiscordJoinAsync(target, _ => false);

        Assert.False(ok);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task HandleDiscordJoinAsync_NoAccountsConfigured_IsAnEmptyStateNotAnError()
    {
        var (vm, _) = DiscordTestHarness.VmWithNoAccounts();

        var ok = await vm.HandleDiscordJoinAsync(new LaunchTarget.GameJob(1, "j"), _ => true);

        Assert.False(ok);   // nothing to launch, and no exception
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~JoinDispatchTests"`
Expected: FAIL — `HandleDiscordJoinAsync` not found.

- [ ] **Step 3: Write minimal implementation**

In `MainViewModel`:

```csharp
    /// <summary>
    /// A clan member clicked Join in Discord. Private servers get a warning first: Roblox checks
    /// permission server-side, so someone not on that server's list gets bounced, and saying so up
    /// front beats a mystery failure. <paramref name="confirm"/> is injected so the decision is
    /// testable without showing a window.
    /// </summary>
    internal async Task<bool> HandleDiscordJoinAsync(LaunchTarget target, Func<string, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target is LaunchTarget.PrivateServer &&
            !confirm("This is a private server — you may be denied entry if you're not on its list. Try anyway?"))
        {
            return false;
        }

        var row = Accounts.FirstOrDefault(a => !a.SessionExpired && !a.IsRunning)
                  ?? Accounts.FirstOrDefault(a => !a.SessionExpired);
        if (row is null)
        {
            StatusBanner = "Nothing to join with — add an account first.";
            return false;
        }

        await LaunchAccountAsync(row, overrideTarget: target).ConfigureAwait(true);
        return true;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~JoinDispatchTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/ src/ROROROblox.Tests/Discord/JoinDispatchTests.cs
git commit -m "feat(discord): inbound join dispatch with a private-server warning

Roblox checks private-server permission server-side, so a clan member without
access gets bounced — telling the joiner up front beats a mystery failure. The
confirm callback is injected so the decision is testable without a window."
```

---

### Task 9: Settings panel — presence toggle, Join toggle, status line

**Files:**
- Modify: `src/ROROROblox.App/Preferences/PreferencesWindow.xaml` (Discord block)
- Modify: `src/ROROROblox.App/Preferences/PreferencesWindow.xaml.cs`
- Modify: `src/ROROROblox.App/App.xaml.cs` (DI wiring, scheme registration, service start)

**Interfaces:**
- Consumes: everything above.
- Produces: no new public API. Plan 2 extends this same panel.

- [ ] **Step 1: Add the Discord block to `PreferencesWindow.xaml`**

Follow the existing bordered-block pattern (`Border` + `RowBgBrush` + `CornerRadius="8"` + `Padding="14"`), same shape as the always-show-Recycle block:

```xml
<Border Background="{DynamicResource RowBgBrush}" CornerRadius="8" Padding="14" Margin="0,0,0,10">
    <StackPanel>
        <CheckBox x:Name="DiscordPresenceToggle"
                  Foreground="{DynamicResource WhiteBrush}" FontSize="13" FontWeight="SemiBold"
                  Click="OnDiscordPresenceToggle">
            <TextBlock Text="Show what I'm playing on Discord." TextWrapping="Wrap" />
        </CheckBox>
        <TextBlock FontSize="11" Foreground="{DynamicResource MutedTextBrush}" TextWrapping="Wrap"
                   Margin="22,4,0,8"
                   Text="Your Discord status shows the game, how many accounts you have in it, and how long you've been going. Nothing to set up — it uses the Discord app already on this PC." />
        <CheckBox x:Name="DiscordJoinToggle"
                  Foreground="{DynamicResource WhiteBrush}" FontSize="12"
                  Margin="22,0,0,0" Click="OnDiscordJoinToggle">
            <TextBlock Text="Let friends join my server from Discord" TextWrapping="Wrap" />
        </CheckBox>
        <TextBlock x:Name="DiscordStatusLine" FontSize="11"
                   Foreground="{DynamicResource CyanBrush}" TextWrapping="Wrap" Margin="22,8,0,0" />
    </StackPanel>
</Border>
```

- [ ] **Step 2: Wire the handlers in `PreferencesWindow.xaml.cs`**

Mirror `OnAlwaysShowRecycleToggle`: read at `OnLoaded` under `_suppressClickHandlers`, write on click, restore the checkbox from the store on failure, and push the change into the live service. Set `DiscordStatusLine.Text` from `DiscordPresenceService.StatusLine` on open and after each toggle.

- [ ] **Step 3: Wire startup in `App.xaml.cs`**

Register `roblox-rororo:` (Task 7) before constructing the presence service, build `LacheeDiscordRpcClientAdapter` from `appsettings.json`'s `Discord:ApplicationId`, and skip the whole feature when that id is empty. Subscribe `DiscordPresenceService.JoinRequested` to `MainViewModel.HandleDiscordJoinAsync`, passing a real modal for `confirm`.

- [ ] **Step 4: Verify build and full suite**

Run: `dotnet build ROROROblox.slnx` then `dotnet test ROROROblox.slnx`
Expected: 0 errors; all tests pass including the ~33 added here.

- [ ] **Step 5: Manual smoke (cannot be automated)**

With Discord running: enable presence → Discord shows the game and account count → launch a second account → the count updates within 15 s → close everything → presence clears. Then with Discord closed: enable presence → status line reads "Discord isn't running", and no dialog appears.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/
git commit -m "feat(discord): presence + Join settings, startup wiring

Presence needs no account, server, webhook, or permission — one toggle, and the
copy says so. An empty Discord:ApplicationId disables the feature rather than
crashing; the status line reports the connection in plain words."
```

---

## Self-review

**Spec coverage:** §4 architecture → Tasks 1-5, 7. §5.1 roster presence → Task 2. §5.2 Join + private warning → Tasks 3, 7, 8. §7.1 streamer mode → Task 6. §7.3 DPAPI → Task 1. §8 error handling → Tasks 5, 7, 8. Alerts (§5.3, §5.4) are plan 2 by design. §9 reviewer disclosure is a release-time doc task, carried in plan 2's final task since that is the last one before submission.

**Types:** `RosterAccount`/`RosterSnapshot`/`PresenceFields` defined Task 2, consumed Tasks 5-6. `DiscordConfig`/`AlertDestination` defined Task 1, consumed Task 5 and all of plan 2. `IDiscordRpcClient`/`DiscordPresencePayload`/`DiscordPresenceParty` defined Task 4, consumed Task 5. `JoinSecretCodec` defined Task 3, consumed Tasks 5 and 7. `DiscordTestHarness` is introduced in Task 6 and reused in Task 8.
