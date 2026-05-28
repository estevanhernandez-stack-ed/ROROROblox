# RoRoRo — Config-driven singleton mutex name (close the §7.1 drift)

> **Status:** Approved for implementation planning
> **Cycle:** Competitive-parity build queue item #1 (see [`docs/competitive/2026-05-28-parity-and-outdo-plan.md`](../../competitive/2026-05-28-parity-and-outdo-plan.md)). Single-seam spec — wires the already-present `mutexName` config field into the already-present `MutexHolder(string)` ctor, with a hardened resolver in between.
> **Design inputs:** Banner-correction at the top of [`docs/superpowers/specs/2026-05-03-rororoblox-design.md`](2026-05-03-rororoblox-design.md) §7.1 (the drift this spec closes) · decision `Tqzz9qwJ6N6mYZsxIX1c` (already logged).
> **Forward hook:** prereq for the later "Handle64 add-to-already-running" sideload plugin — the resolved name must be queryable by the plugin host.

## Why this exists

**The mutex name is a hardcoded const today; CLAUDE.md and spec §7.1 claim it's config-driven. That's a lie in the code, banner-corrected on 2026-05-03, and this spec makes the docs true.** `MutexHolder.DefaultMutexName = @"Local\ROBLOX_singletonEvent"` is a `public const` (`MutexHolder.cs:22`). The DI registration constructs `new MutexHolder()` — the parameterless ctor that delegates to the const (`App.xaml.cs:96` resolve, ctor at `MutexHolder.cs:34`). The startup log even prints the literal const (`App.xaml.cs:113`). The `mutexName` field already exists on the config record (`RobloxCompatConfig.cs:10`) and is already deserialized — but **nothing reads it.** `RobloxCompatChecker.CheckAsync` only consumes `KnownGoodVersionMin` / `KnownGoodVersionMax` for the version-drift banner (`RobloxCompatChecker.cs:54-71`). The field is dead wiring.

Why it matters: the singleton mutex name is a **Roblox-owned contract.** When Roblox renames `Local\ROBLOX_singletonEvent` — and they have moved kernel-object names before — multi-instance breaks for every install at once. Today the fix is *rebuild the binary, sign the MSIX, push a Velopack release, wait for users to update.* With the name config-sourced, the fix is *push one string to `roblox-compat.json` in a GitHub Release* and the existing fetch picks it up on next launch. **Hours, not a build cycle.** That recovery-speed delta is the whole payoff.

This is also the **Handle64 prereq.** A later sideload plugin will "add an already-running Roblox to the managed set" by closing the same singleton handle the app resolved. That plugin must be handed the *resolved* name — whatever the app actually bound, default or override. So the resolution can't stay buried inside `MutexHolder` construction; it has to surface to the plugin host.

**Store-policy guard, load-bearing:** `roblox-compat.json` stays remote **data** (a string the app substitutes after validation), never remote **code.** No eval, no download-and-run, no behavior fetched over the wire — just a name string flowing into a `CreateMutex` lpName. That keeps the whole mechanism Store-policy-10.2.2-clean, same posture as the version-range field that already ships there.

## What the host already has

Most of this is exposing and hardening machinery that's already landed. The heavy lifting exists:

- **The config field.** `RobloxCompatConfig.MutexName` (`RobloxCompatConfig.cs:10`) — already a plain `string`, already deserialized under the camelCase policy (`RobloxCompatChecker.cs:23-26`). The JSON key is currently `mutexName`.
- **The injection seam.** `MutexHolder(string mutexName)` (`MutexHolder.cs:36-45`) already exists and already **throws `ArgumentException` on null/empty/whitespace** (`MutexHolder.cs:38-41`). The parameterless ctor delegates to it with the const (`MutexHolder.cs:34`). Nothing inside `MutexHolder` changes — only what string the composition root passes in.
- **The hard fallback.** `MutexHolder.DefaultMutexName` (`MutexHolder.cs:22`) is the last-resort default the resolver lands on.
- **The fetch.** `RobloxCompatChecker.FetchConfigAsync` (`RobloxCompatChecker.cs:74-86`) — 8-second-bounded `GetFromJsonAsync`, **fail-quiet to `null` on any throw.** No cache, no last-known-good — fetches fresh every startup.

What's missing and what this spec adds: (1) a **resolver** that turns config-or-null into a *guaranteed-valid* name, never throwing; (2) **last-known-good caching** so a network failure doesn't lose a previously-fetched override; (3) a **queryable surface** for the resolved name (`IMutexHolder.MutexName`) so the Handle64 host can read it; (4) the **startup-ordering fix** so resolution feeds `mutex.Acquire()` instead of racing it.

## Components

### 1. Schema: rename the field to `singletonMutexName`, back-compat both directions

The field is named `MutexName` in the record. **Rename the JSON key to `singletonMutexName`** — more specific, won't collide if a future config grows a second mutex concept. Keep the C# property named for clarity, map it explicitly.

```csharp
public sealed record RobloxCompatConfig(
    string KnownGoodVersionMin,
    string KnownGoodVersionMax,
    [property: JsonPropertyName("singletonMutexName")] string? SingletonMutexName,
    DateTimeOffset GeneratedAt);
```

Two changes from today's record (`RobloxCompatConfig.cs:7-11`): the property is `string?` (nullable — a config that omits it is valid), and it carries an explicit `[JsonPropertyName]` so the wire key is `singletonMutexName` regardless of the camelCase default.

**Back-compat, both directions — this is the constraint, not a nice-to-have:**

- **Old client, new config field.** v1.x clients before this ships deserialize `roblox-compat.json` into the *old* record. `System.Text.Json` **ignores unknown JSON members by default** — an extra `singletonMutexName` key is silently dropped. Old clients keep using their hardcoded const. No crash, no behavior change. Safe to ship the field to the live config *before* a single new client exists.
- **New client, old config (field absent).** `SingletonMutexName` deserializes to `null`. The resolver (component 2) treats `null` exactly like a fetch failure: **fall to last-known-good, then to `DefaultMutexName`.** No throw.

Before (live `roblox-compat.json` today):

```json
{
  "knownGoodVersionMin": "0.659.0",
  "knownGoodVersionMax": "0.671.0",
  "mutexName": "Local\\ROBLOX_singletonEvent",
  "generatedAt": "2026-05-20T00:00:00+00:00"
}
```

After (the key the new resolver reads — old `mutexName` may stay for one release as a no-op, then drop):

```json
{
  "knownGoodVersionMin": "0.659.0",
  "knownGoodVersionMax": "0.671.0",
  "singletonMutexName": "Local\\ROBLOX_singletonEvent",
  "generatedAt": "2026-05-20T00:00:00+00:00"
}
```

The dichotomy with the version-range fields: those *warn* on drift (cosmetic banner, `RobloxCompatChecker.cs:67-69`). This one *acts* — a bad value here doesn't show a banner, it decides whether multi-instance works at all. So the resolver is held to a stricter fail-open bar than the banner path.

### 2. The resolver: `IRobloxCompatChecker.ResolveMutexNameAsync()` — validate, never throw

Add one method to the compat-checker interface. It owns the entire decision and **guarantees a valid, non-empty return** — the caller (the composition root) never sees `null` and never has to defend against `ArgumentException` from the ctor.

**Interface-drift note.** `IRobloxCompatChecker` (`IRobloxCompatChecker.cs`) exposes exactly one member today: `Task<CompatCheckResult> CheckAsync()`. Adding `ResolveMutexNameAsync` means every implementer and every test double must add it. There is no test double and no `RobloxCompatCheckerTests` file today, and a grep should confirm `RobloxCompatChecker` is the only implementer before the method lands — if a mock surfaces later, it inherits the new member. Adding the resolver puts a second responsibility (fetch + cache + validate) on a type named "compat check"; that's acceptable for v1 — both methods are config-fetch-shaped — but note the consequence: **`CheckAsync` and `ResolveMutexNameAsync` each call `FetchConfigAsync` independently, so a cold startup fetches the same URL twice** (resolver's 2s budget, then the banner's 8s). That redundancy is accepted (component 4 — sharing fetch state across the sync/async boundary is more coupling than the saved round-trip is worth); the only hard requirement is that each call honors its **own** `CancellationTokenSource` so the 2s and 8s budgets don't bleed into each other.

The method returns a **tuple of `(string Name, MutexNameSource Source)`**, not a bare string. The source tier is load-bearing for the startup diagnostic — see component 5 — because an LKG-cache hit and a fresh-remote hit both return a name that isn't `DefaultMutexName`, and the compat-event log must not conflate "the live config served this" with "a stale disk cache served this." A new enum carries it:

```csharp
public enum MutexNameSource { RemoteConfig, LastKnownGood, Default }
```

```csharp
/// <summary>
/// Resolves the singleton mutex name to bind at startup. Precedence:
/// (1) a valid <c>singletonMutexName</c> from the freshly-fetched remote config;
/// (2) the last-known-good name cached on disk from a prior successful fetch;
/// (3) <see cref="MutexHolder.DefaultMutexName"/> as the hard fallback.
/// NEVER throws and NEVER returns null/empty/whitespace — a garbage or missing
/// config value degrades to the next precedence tier, it does not break multi-instance.
/// Returns the source tier so the caller logs the REAL origin (remote vs cache vs default).
/// Bounded by a 2s timeout enforced via its OWN <see cref="CancellationTokenSource"/>
/// (NOT HttpClient.Timeout — see below), so it can run before mutex.Acquire() without
/// holding first paint hostage to the network. Persists a valid fresh fetch as the new LKG.
/// MUST run fully off the UI thread: every await inside uses ConfigureAwait(false).
/// </summary>
Task<(string Name, MutexNameSource Source)> ResolveMutexNameAsync(
    CancellationToken cancellationToken = default);
```

**The 2s budget is enforced by the resolver's own `CancellationTokenSource`, not by `HttpClient.Timeout`.** The typed `HttpClient` registered for the compat checker (`App.xaml.cs:298-303`) sets **no** `Timeout`, so it defaults to 100s — and that client is *shared* with the 8s banner path. The resolver must create a fresh `new CancellationTokenSource(TimeSpan.FromSeconds(2))`, link it with the incoming token, and pass *that* token into `GetFromJsonAsync` — exactly as `FetchConfigAsync` already does for its 8s budget (`RobloxCompatChecker.cs:78`). Do **not** "fix" the timeout by setting `client.Timeout = 2s`: that would clobber the 8s banner path sharing the same typed client. The two budgets are independent per-call CTSs over one client; that is correct and intended.

**`ConfigureAwait(false)` is mandatory *inside the resolver* (deadlock precedent, load-bearing).** `App.xaml.cs:65-69` documents a PRIOR DEADLOCK on exactly the compat path: a `GetActiveThemeIdAsync` call continued on the UI thread (via `ConfigureAwait(true)`) while a `.GetAwaiter().GetResult()` **blocked** the UI thread, and the two deadlocked against each other. We avoid that two ways. First, we never block: `OnStartup` goes `async void` and `await`s (component 3) — a true `await` yields the UI thread to the message pump instead of blocking it, so the deadlock cannot form. Second, the resolver keeps its IO off the UI thread: `FetchConfigAsync` already uses `ConfigureAwait(false)` (`RobloxCompatChecker.cs:80`), and every `await` *inside* the new resolver (the fetch, the cache file IO) **must** use `ConfigureAwait(false)` too. The one place we deliberately do **not** use it is the thin top-level `OnStartup` await of `ResolveMutexNameAsync()` — that continuation must return to the UI thread for the `Dispatcher`-affined `tray.Show()` / `mainWindow.Show()`, and it is safe precisely because nothing blocks (component 3).

Resolution logic — the precedence ladder, top wins:

1. **Fresh remote config, valid name.** Fetch (reusing `FetchConfigAsync`'s pattern, but with a 2s CTS, not 8 — see ordering, component 4). If `config?.SingletonMutexName` passes `IsValidMutexName` → **persist it as last-known-good** and return `(name, RemoteConfig)`.
2. **Last-known-good cache.** Fetch failed, returned `null`, timed out, or returned an invalid/missing name → read `%LOCALAPPDATA%\ROROROblox\last-known-mutex.txt`. If present and valid → return `(name, LastKnownGood)`. (This is the tier that lets a rename survive an offline restart: once a good name is fetched even once, it sticks across network failures.)
3. **Hard fallback.** Nothing valid anywhere → return `(MutexHolder.DefaultMutexName, Default)`.

`IsValidMutexName` is the gate that makes the "never throw" promise real, because `MutexHolder(string)` *does* throw on bad input (`MutexHolder.cs:38-41`). The resolver validates *before* the ctor ever sees the string:

```csharp
private static bool IsValidMutexName(string? name)
{
    if (string.IsNullOrWhiteSpace(name)) return false;        // matches the ctor's own reject
    if (name.Length > 250) return false;                      // conservative margin under MAX_PATH (260) — see note
    if (name.Contains('\0')) return false;                    // embedded null terminates lpName early
    // Backslash IS allowed — it's the namespace separator (Local\, Global\, Session\N\).
    return true;
}
```

On the length ceiling: the real Win32 `CreateMutex` `lpName` limit is `MAX_PATH` (**260** chars), and a backslash is only *required* as the leading namespace separator — an interior backslash past the prefix is actually permitted by `CreateMutex`. **250 is a deliberate conservative margin, not the hard limit.** It's documentation-precision, not a safety hole: an over-length name still fails validation and falls back, so the fail-open guarantee holds either way. We pick 250 over 260 to leave a few chars of headroom against any prefix-counting edge and to keep the value obviously a chosen margin. The validator is a **strict superset** of the ctor's own reject (`MutexHolder.cs:38-41` rejects only null/empty/whitespace), which is what makes the never-throw promise provable: any string the validator passes, the ctor accepts.

The cache file is a single line of UTF-8 text, no JSON, no structure — written only on a confirmed-valid fresh fetch, read fail-quiet (missing file / IO error → treat as no cache). It lives next to `last-update-check.txt` under `%LOCALAPPDATA%\ROROROblox\` and is **gitignored** like every other runtime-state file (CLAUDE.md file rules). Do **not** conflate it with `last-update-check.txt` — that's the Velopack update-check debounce stamp (`UpdateChecker.cs:18-22`), unrelated.

### 3. Composition root: feed the resolved name to `MutexHolder` (two ordering BLOCKERs, both named)

Two hard constraints in the existing startup make the naive "resolve, then register" impossible, and both have to be solved explicitly:

**Constraint A — the container is sealed before resolution can run.** `_services = services.BuildServiceProvider()` runs at `App.xaml.cs:63`. `IMutexHolder` is registered at `App.xaml.cs:263` as a factory closure that hardcodes the parameterless ctor: `services.AddSingleton<IMutexHolder>(_ => new MutexHolder())`. The `StartupGate` (where resolution slots in) runs at `App.xaml.cs:86-93`, and the singleton is first resolved at `App.xaml.cs:96` via `GetRequiredService<IMutexHolder>()`. **You cannot re-register into an already-built `ServiceProvider`** — line 63 sealed it — and the singleton is materialized on first resolve at line 96. So "register the constructed instance later" is not implementable as written in the draft.

**Constraint B — `OnStartup` is synchronous and the compat path has a documented deadlock.** The override is `protected override void OnStartup(StartupEventArgs e)` (`App.xaml.cs:36`) — **not** `async`. Blocking it with `.GetAwaiter().GetResult()` on the resolver is exactly the pattern `App.xaml.cs:65-69` documents as a PRIOR DEADLOCK on this same compat path: `GetActiveThemeIdAsync` continued on the UI thread (`ConfigureAwait(true)`) and `GetResult()` on the UI thread deadlocked against it. A bare `.GetResult()` on `ResolveMutexNameAsync` would deadlock identically the instant any continuation marshals back to the UI thread.

**The mechanism, both constraints solved:**

*Constraint A — a mutable name-holder the factory reads (chicken-and-egg resolved):* introduce a tiny mutable seam that the factory closure captures and the resolver populates *before* line 96's `GetRequiredService` call. The factory defers name capture until first resolve, so it reads whatever the resolver wrote:

```csharp
// New: a one-field mutable holder, registered as a singleton, captured by the IMutexHolder
// factory. Resolution writes it BEFORE the IMutexHolder singleton is first materialized.
public sealed class ResolvedMutexName { public string Value = MutexHolder.DefaultMutexName; }

// ConfigureServices (replaces App.xaml.cs:263):
services.AddSingleton<ResolvedMutexName>();
services.AddSingleton<IMutexHolder>(sp =>
    new MutexHolder(sp.GetRequiredService<ResolvedMutexName>().Value));
```

`IRobloxCompatChecker` is itself in the same sealed container — that chicken-and-egg is fine, because the checker has no dependency on `IMutexHolder`; we resolve the checker, write the holder, *then* resolve `IMutexHolder`. Because the `IMutexHolder` factory is lazy (a singleton isn't constructed until first `GetRequiredService`), the holder's value is read at construction time, which we guarantee happens after the write. No re-registration, no rebuilt container.

*Constraint B — `async void OnStartup` with a UI-thread continuation:* change the override to `protected override async void OnStartup(StartupEventArgs e)`. `async void` is the sanctioned shape for a top-level event-handler override (which `OnStartup` is). The top-level `await compat.ResolveMutexNameAsync()` does **not** use `ConfigureAwait(false)`: the continuation that writes the holder, calls `mutex.Acquire()`, then `tray.Show()` / `mainWindow.Show()` must resume on the **UI thread**, because the window/tray shows are `Dispatcher`-affined and throw if created off-thread. This does not deadlock — a true `await` *yields* the UI thread back to the message pump instead of blocking it, so when the resolve completes the dispatcher runs the continuation on the UI thread. The lines 65-69 precedent deadlocked because it *blocked* the UI thread with `.GetResult()` while the continuation tried to marshal back; going truly async removes the block entirely, which is exactly why we can keep the continuation on the UI thread here. The network/disk IO still never touches the UI thread, because the resolver uses `ConfigureAwait(false)` *internally* (component 2) — only the thin `OnStartup` continuation returns to the UI thread. `base.OnStartup(e)` stays first, before the await, exactly as today (`App.xaml.cs:38`).

The resolution block, slotted after the `StartupGate` block (`App.xaml.cs:86-93`) and **before** the first `IMutexHolder` resolve at line 96:

```csharp
// Resolve the singleton mutex name from remote config (data-only) with a hard fallback.
// 2s-bounded via the resolver's own CTS; degrades to last-known-good then to the hardcoded
// default. NEVER throws. THREADING: this top-level await does NOT use ConfigureAwait(false) —
// the continuation must resume on the UI thread, because the work that follows (mutex.Acquire
// is thread-agnostic, but the later tray.Show()/mainWindow.Show() are Dispatcher-affined and
// throw off-thread). No deadlock: a true await YIELDS the UI thread to the message pump rather
// than blocking it; the lines 65-69 deadlock was a BLOCKING .GetResult(), which we've removed.
// The resolver's INTERNAL network/disk IO uses ConfigureAwait(false) (component 2), so the UI
// thread is never touched by the IO — only the thin continuation comes back to it.
var compat = _services.GetRequiredService<IRobloxCompatChecker>();
string resolvedMutexName;
MutexNameSource nameSource;
try
{
    (resolvedMutexName, nameSource) = await compat.ResolveMutexNameAsync();
}
catch (Exception ex)
{
    // Defense in depth — the resolver promises no-throw, but if it ever does, fall hard.
    _log.LogWarning(ex, "Mutex-name resolve threw; falling back to default.");
    (resolvedMutexName, nameSource) = (MutexHolder.DefaultMutexName, MutexNameSource.Default);
}

// Write the holder BEFORE the IMutexHolder singleton is first materialized at line 96.
_services.GetRequiredService<ResolvedMutexName>().Value = resolvedMutexName;
```

Then the acquire log must stop printing the literal const — it currently lies by logging `MutexHolder.DefaultMutexName` regardless of what was bound (`App.xaml.cs:113`). Fix it to log the actual bound name **and the real source tier** (component 5). The source comes straight from the resolver's returned enum — do not re-derive it by comparing the string to `DefaultMutexName`, because an LKG-cache hit returns a non-default name and would be mislabeled `remote-config`:

```csharp
_log.LogInformation(
    "Mutex acquire at startup: name={Name}, source={Source}, acquired={Acquired}. Multi-instance is {State}.",
    mutex.MutexName,
    nameSource,                          // RemoteConfig | LastKnownGood | Default — the REAL tier
    acquired,
    acquired ? "ON" : "ERROR");
```

### 4. The startup-ordering fix (the crux)

The naive read — "let `RunStartupChecksAsync` fetch the config, then build the mutex from it" — is a **sequencing inversion.** Today the compat fetch is fire-and-forget *after* `mainWindow.Show()` (`App.xaml.cs:123` → `vm.LoadCompatBannerAsync()` → `CheckAsync` at `MainViewModel.cs:747`), while `mutex.Acquire()` runs synchronously much earlier (`App.xaml.cs:110`). By the time the async banner fetch returns, the mutex is already held under whatever name was passed at construction. You can't feed a value that arrives after the consumer already ran.

Resolution: **move the name decision in front of acquire** (via `async void OnStartup` + a 2s-bounded resolve awaited on the UI thread, with the resolver's IO on `ConfigureAwait(false)` internally — component 3), with its own short-bounded fetch, separate from the banner fetch.

```text
async void OnStartup                                 App.xaml.cs:36 (was: void)
  │
  ├─ base.OnStartup(e)                               App.xaml.cs:38 (stays first, pre-await)
  ├─ BuildServiceProvider() ── container sealed ──▶  App.xaml.cs:63
  ├─ ApplyAtStartup (theme/brush, sync)              App.xaml.cs:72
  ├─ StartupGate.ShouldProceed()  ── hard-block ──▶  App.xaml.cs:86-93
  │        (Roblox-already-running → modal → exit)
  │
  ├─ await ResolveMutexNameAsync()  ◀── NEW, 2s CTS, UI-thread continuation ──┐
  │     1. fetch remote config (own 2s CTS, not HttpClient.Timeout)          │ data-only
  │     2. valid singletonMutexName? ─yes─▶ persist LKG, (name, RemoteConfig)
  │        └─no─▶ last-known-mutex.txt? ─yes─▶ (name, LastKnownGood)
  │              └─no─▶ (DefaultMutexName, Default)                          │
  │                                                                          ▼
  ├─ ResolvedMutexName.Value = resolvedMutexName  ── BEFORE first IMutexHolder resolve ──
  ├─ GetRequiredService<IMutexHolder>()              App.xaml.cs:96  ◀ factory reads holder → bound name
  ├─ mutex.Acquire()                                 App.xaml.cs:110 ◀ uses resolved name; log real source
  ├─ tray.Show() / mainWindow.Show()                 App.xaml.cs:118-120 (post-await continuation)
  │
  └─ RunStartupChecksAsync (fire-and-forget)         App.xaml.cs:123
        └─ vm.LoadCompatBannerAsync()  ── version-drift banner, unchanged, 8s ──
```

Why a **separate 2-second** fetch rather than reusing the 8s banner fetch: the banner can afford 8 seconds because it runs *after* first paint and only updates a text label. The mutex name gates acquire, which gates first-launch multi-instance — it can't sit behind 8 seconds of network on a cold start. 2 seconds is the budget: enough for a CDN round-trip on a normal connection, short enough that an offline cold-start falls to last-known-good / default fast and the window still paints promptly. On the happy path (good network, valid config) the user pays ~200-400ms once at startup; on the slow/offline path they pay 2s and get the cached or default name.

Note the two fetches are independent and both fail-quiet — there's deliberate redundancy (the resolver fetches the same config the banner later fetches again). That's accepted: the alternative is sharing fetch state across a sync-then-async boundary, which is more coupling than the saved round-trip is worth. **Antirez rule — ship the smallest move that pays.** A second 2s fetch is cheap; a shared-cache-with-invalidation is not.

### 5. Forward hook: expose the resolved name via `IMutexHolder.MutexName`

The Handle64 plugin needs the name the app *actually bound*. Today `_mutexName` is private (`MutexHolder.cs:27`) and `IMutexHolder` (`IMutexHolder.cs:8-14`) exposes only `IsHeld` / `Acquire` / `Release` / `MutexLost`. Add a getter:

```csharp
public interface IMutexHolder
{
    bool IsHeld { get; }
    string MutexName { get; }   // ← NEW: the resolved name this holder is bound to.
    bool Acquire();
    void Release();
    event EventHandler? MutexLost;
}
```

Implementation is a one-liner — `public string MutexName => _mutexName;` (`MutexHolder.cs`). This is the *resolved* name (default, last-known-good, or remote), because the resolver already decided it before construction.

The plugin-facing surface follows. `MutexHostStateAdapter` (`src/ROROROblox.App/Plugins/Adapters/MutexHostStateAdapter.cs`) already wraps `IMutexHolder` for the plugin host's `IPluginHostStateProvider`. When Handle64 lands, that adapter (or a sibling) exposes `MutexName` to the plugin host so the plugin closes the *same* handle the app resolved — not a guessed-at const. **Adding the getter now, ahead of Handle64, keeps the resolved name out of ctor-only state** so the later work is a read, not a refactor. It stays data-only: a string property, no behavior crossing the plugin boundary, 10.2.2-clean.

## Stack

No new dependencies. Reuses what's landed:

- `RobloxCompatChecker` + its `HttpClient` ctor seam (`RobloxCompatChecker.cs:30-33`) and `FetchConfigAsync` pattern.
- `System.Text.Json` `[JsonPropertyName]` for the explicit wire-key mapping — already referenced.
- `MutexHolder(string)` ctor (`MutexHolder.cs:36-45`) — the injection point, unchanged.
- `%LOCALAPPDATA%\ROROROblox\` runtime-state dir (already home to `last-update-check.txt`) for the LKG cache file.

## Testing

Unit + reconciliation; no E2E against real roblox.com (CLAUDE.md rule). The mutex *name* never touches a real Roblox process in test — resolution is string-in / string-out plus an IO cache, all stubbable. `MutexHolder`'s real-Win32 behavior is already covered by `MutexHolderTests.cs` with GUID-unique names (`UniqueName()` helper, `MutexHolderTests.cs:13`); this spec adds nothing to that surface and must not regress `Constructor_RejectsEmptyOrWhitespaceName` (`MutexHolderTests.cs:104-109`) — the resolver guarantees the ctor never sees a bad string.

New test file `src/ROROROblox.Tests/RobloxCompatCheckerTests.cs` (NEW), copying the `StubHttpHandler` pattern from `RobloxUpdateProbeTests.cs` (`RobloxCompatCheckerTests` injects `new HttpClient(new StubHttpHandler())`, enqueues JSON via `stub.EnqueueResponse(Json(...))`). `RobloxCompatChecker` already takes an `HttpClient` ctor arg, so the pattern drops in. Cases, every fallback branch named:

Every case asserts the returned **tuple** — both `Name` and `Source` — since the source tier is now load-bearing for the diagnostic (a test that only checks the string can't catch the LKG-vs-remote mislabel the review flagged):

- **`Resolve_ValidRemoteName_ReturnsItAndPersistsLkg`** — config with `singletonMutexName: "Local\\ROBLOX_newName"` → returns `(name, RemoteConfig)`; asserts the LKG cache file now holds that value.
- **`Resolve_MissingField_FallsToCacheThenDefault`** — config JSON without the key (old-config back-compat) → `SingletonMutexName` is null → returns `(lkg, LastKnownGood)` if cached, else `(DefaultMutexName, Default)`.
- **`Resolve_EmptyString_FallsBackNeverThrows`** — `singletonMutexName: ""` → `IsValidMutexName` false → falls back; **never throws** (guards the ctor-throws-on-empty contract).
- **`Resolve_WhitespaceName_FallsBack`** — `singletonMutexName: "   "` → falls back (matches `MutexHolder.cs:38` whitespace reject, caught *before* the ctor).
- **`Resolve_GarbageName_FallsBack`** — embedded `\0` / over-length string → `IsValidMutexName` false → falls back.
- **`Resolve_NetworkFail_FallsToLastKnownGood`** — stub throws / non-200 → fetch returns null → returns `(lkg, LastKnownGood)`, the previously-cached name (the rename-survives-offline guarantee). **Asserts `Source == LastKnownGood`, not `RemoteConfig`** — this is the exact mislabel the source enum exists to prevent.
- **`Resolve_NetworkFail_NoCache_FallsToDefault`** — fetch null AND no cache file → returns `(MutexHolder.DefaultMutexName, Default)`.
- **`Resolve_BoundedTimeout_DoesNotHang`** — slow stub > 2s → resolver returns within budget on the fallback tier (asserting the 2s CTS fires, *not* the 100s HttpClient default), doesn't block past its own timeout.
- **`MutexName_Getter_ReturnsResolvedName`** — `new MutexHolder("Local\\X").MutexName == "Local\\X"` (forward-hook surface; add to `MutexHolderTests.cs`).

Manual smoke: on a clean Win11 box, point a local `roblox-compat.json` (served via a stubbed URL or a hosts-file redirect — not real roblox.com) at a deliberately different `singletonMutexName`, confirm the startup log prints `source=RemoteConfig` and the resolved name, and confirm two clients still launch. Then with the cache now populated, kill network and restart — confirm `source=LastKnownGood` and the *override* name still binds (proves the cache tier). Finally wipe `last-known-mutex.txt`, kill network, restart — confirm `source=Default` and multi-instance still works. Three restarts, three distinct source labels: this is exactly the assertion a 2-way `default-or-fallback` label could not make.

## Out of scope (deliberate)

- **Hot-reloading the mutex name on a live config push.** Resolution happens once at startup. A rename pushed mid-session doesn't re-bind until next launch — re-acquiring under a new name while holding the old one is a Handle64-adjacent concern, not this spec's. Acquire-then-reconcile (release + re-acquire on a name change) was considered and **rejected for v1**: it briefly holds the wrong name and adds a window where multi-instance is in an indeterminate state, for a benefit (sub-restart rename recovery) nobody's asked for. Restart-to-pick-up is the v1 trade.
- **A full structured cache (JSON, multiple fields, TTL).** The LKG cache is one line of text — the *name only*. Caching the whole config (version range too) is a separate, larger change with its own staleness semantics; not needed to make rename-recovery work.
- **Removing the hardcoded `DefaultMutexName` const.** It stays as the hard fallback floor (`MutexHolder.cs:22`). A config-only design with no compiled default would brick multi-instance on first-run-offline-no-cache — exactly the case the const exists to cover.
- **Handle64 plugin itself.** This spec ships the *queryable surface* (`IMutexHolder.MutexName`) the plugin will read. The plugin — closing an already-running Roblox's handle, the sideload packaging, the capability gate — is its own queue item.
- **Any change to the version-drift banner path.** `CheckAsync` (`RobloxCompatChecker.cs:35-72`) is untouched; the banner keeps its 8s fetch and fail-quiet-to-no-drift behavior.

## Risks / open questions

- **Roblox-side compat axis (the whole point).** The singleton mutex name `Local\ROBLOX_singletonEvent` is a Roblox-owned kernel-object name — log it as a compat-risk axis. This spec is the *mitigation*: a rename now recovers via a config push + Velopack release in hours instead of a binary rebuild. Degrades gracefully — if Roblox changes the name and we haven't pushed config yet, multi-instance breaks (as it does today), but the version-drift banner already points users at the issue tracker, and recovery is a one-string config push.
- **A bad config value could brick multi-instance — IF the fallback is wrong.** This is the load-bearing risk and the reason fallback hardening is non-negotiable. A typo'd `singletonMutexName` in a pushed config would, *without* validation, either throw at the ctor (crash on launch) or bind the wrong name (silent multi-instance failure). Mitigation: `IsValidMutexName` rejects empty/whitespace/garbage *before* the ctor so it never throws; an *invalid* name falls to LKG/default. The residual hole: a *syntactically valid but semantically wrong* name (e.g. a real-looking but incorrect mutex) passes validation and binds — that's a config-authoring discipline problem, not a code problem, mitigated by the same manual-smoke gate we run before any config push. **Accepted with the smoke-test gate.**
- **LKG cache poisoning.** If a wrong-but-valid name gets fetched and persisted as last-known-good, it sticks across offline restarts until a good fetch overwrites it. Mitigation: LKG is only written on a *successful, validated* fetch; the same config-authoring discipline that prevents the push prevents the poison. Low likelihood, recoverable by the next good push. **Accepted.**
- **2-second startup budget.** Adds up to 2s to cold-start on a slow/offline connection (happy path is ~200-400ms). Mitigation: bounded by `CancellationTokenSource`, falls through to cache/default on timeout — the window still paints. If 2s proves too long in clan testing, it's a one-constant tune. **Accepted, tunable.**
- **Open question: keep the legacy `mutexName` key alive for one release?** Shipping `singletonMutexName` while old clients still read `mutexName` means the live config carries both for a transition window (old clients read `mutexName`, new clients read `singletonMutexName`, old clients ignore the unknown key). Leaning yes — costs one duplicate string in the JSON, buys zero-risk rollout. Drop `mutexName` once telemetry/Store shows the old-client tail is gone. **Resolve before the first config push.**

## Decisions to log (626 Labs Dashboard)

Decision `Tqzz9qwJ6N6mYZsxIX1c` is already logged (this spec's parent decision). On completion, log the implementation-shape specifics:

- **Made the singleton mutex name config-sourced from `roblox-compat.json` (`singletonMutexName`) wired through the existing `MutexHolder(string)` ctor, with a hardened resolver in front, instead of leaving it a hardcoded const** — because a Roblox-side mutex rename then recovers via a config push + Velopack release in hours rather than a binary rebuild. Closes the §7.1 drift banner-corrected on the 2026-05-03 spec.
- **NEW Roblox-side compat dependency:** the singleton mutex name `Local\ROBLOX_singletonEvent` is now config-tracked, not just compiled-in. When Roblox renames it, the recovery is a `roblox-compat.json` push — log the rename event itself when it happens (this is the event that shifts the config, per CLAUDE.md compat-event rule).
- **Kept the resolved name fail-open with a three-tier precedence (valid remote → last-known-good cache → hardcoded `DefaultMutexName`) and a pre-ctor validator, instead of trusting config input to the throwing ctor** — because a missing/empty/garbage config value must never break multi-instance or crash launch. Added a one-line `last-known-mutex.txt` LKG cache so a rename survives an offline restart.
- **Resolved the name in front of `mutex.Acquire()` via `async void OnStartup` awaiting a 2s-bounded resolver (whose IO runs under `ConfigureAwait(false)` internally; the `OnStartup` continuation itself stays on the UI thread for the `Dispatcher`-affined window shows), instead of feeding it from the existing fire-and-forget 8s compat fetch (or blocking with `.GetAwaiter().GetResult()`)** — because the banner fetch returns after acquire already ran (sequencing inversion), and a `.GetResult()` block on the compat path would re-trigger the documented UI-thread deadlock (`App.xaml.cs:65-69`). A separate short-bounded fetch with its IO off the UI thread keeps first paint prompt while still picking up a pushed rename.
- **Bound the resolved name through a mutable `ResolvedMutexName` holder the `IMutexHolder` factory reads, instead of re-registering or rebuilding the sealed `ServiceProvider`** — because the container is built at `App.xaml.cs:63` before resolution can run and the `IMutexHolder` singleton is materialized lazily at first resolve (`App.xaml.cs:96`); writing the holder before that first resolve lets the lazy factory pick up the resolved name with no container rebuild. Enforced the 2s budget via the resolver's own `CancellationTokenSource` (not `HttpClient.Timeout`, which is shared with the 8s banner path and unset → 100s default), and surfaced the resolution source as a `MutexNameSource` enum so the compat-event log distinguishes a live-config hit from a stale last-known-good cache.
- **Exposed the resolved name as `IMutexHolder.MutexName` (data-only getter) ahead of the Handle64 plugin, instead of leaving it ctor-private** — because the future "add-to-already-running" sideload plugin must close the same singleton handle the app resolved, and the resolved name has to be queryable by the plugin host. Stays Store-10.2.2-clean: a string property, no code crossing the plugin boundary.
- **Kept `roblox-compat.json` data-only (a validated string, never fetched code/eval)** — because remote *data* is Store-policy-10.2.2-clean while remote *code* is a rejection; the mutex name flows as a `CreateMutex` lpName string after validation, same posture as the already-shipping version-range fields.

---
**A 626 Labs product · *Imagine Something Else*.**
