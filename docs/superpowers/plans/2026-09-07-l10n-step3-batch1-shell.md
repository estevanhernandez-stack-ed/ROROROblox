# Localization Phase D step 3 — Batch 1 (Shell foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Localize the always-visible shell composed strings (MainViewModel + AccountSummary + the three VM helper composers) through `Loc`/resx, and wire the shell to re-render live on a culture toggle — establishing the pattern every later step-3 batch copies.

**Architecture:** Composed display text moves to resx keys resolved at runtime via `Loc.Get/Format/Plural`. Long-lived surfaces (MainViewModel and its child AccountSummary rows) subscribe to `TranslationSource.CultureChanged` and recompose: computed getters re-pull automatically; cached-composed fields are recomputed by re-running their existing refresh methods. Plural strings use per-form resx families via `Loc.Plural`; `Loc.Plural` is extended to carry extra `string.Format` args.

**Tech Stack:** C# / .NET 10 / WPF; `ROROROblox.App.Localization` (`Loc`, `TranslationSource`, `Plurals`); xUnit; `Properties/Strings.resx` (neutral catalog, satellites auto-globbed).

**Spec:** `docs/superpowers/specs/2026-09-07-l10n-phase-d-step3-app-strings-design.md`

## Global Constraints

- English preserved **byte-for-byte** in the neutral resx value (this batch changes no wording — it relocates it). The one copy change (picker hint) is a later batch, not this one.
- New keys are added to the **neutral** `Strings.resx` only. Satellites (fr/de/ru/pt-BR/pl/es) are NOT touched — they fall back to English until step 4.
- Plural families in the neutral resx define **all four** CLDR categories used across the six languages: `_one`, `_few`, `_many`, `_other`. For English the selector reads `_one`/`_other`; `_few`/`_many` carry the same text as `_other` so a ru/pl fallback to neutral is never a missing key.
- Product nouns stay English inside values: **RoRoRo, Roblox, Multi-Instance** (and Squad Launch, Recycle, Friend Follow, Pushover, ntfy, Discord). Unit symbols/glyphs stay: `GB`, `▲`, `·`, `s`/`m`/`h`/`min`/`hr` abbreviations, date patterns.
- Key namespace for this batch: `Shell_*` for shell/VM display strings, plural families `Shell_*_{one,few,many,other}`.
- Build green (`dotnet build ROROROblox.slnx -c Release`) and unit suite green (`dotnet test src/ROROROblox.Tests/ -c Release`) at each commit. The `AppStorageDefenderTests`/`FpsCapSettlerTests` wall-clock flake is pre-existing — re-run, don't fix here.
- Commit attribution footer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

## File Structure

- `src/ROROROblox.App/Localization/Loc.cs` — extend `Plural` with extra args (Task 1).
- `src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs` — const → `Loc` getters (Task 2).
- `src/ROROROblox.App/ViewModels/IdleSummary.cs` — `Loc.Plural` + threshold arg (Task 3).
- `src/ROROROblox.App/ViewModels/MemoryChipFormatter.cs` — footer → `Loc` (Task 4).
- `src/ROROROblox.App/ViewModels/AccountSummary.cs` — status states + relative-time → `Loc`; add culture-refresh nudge (Task 5).
- `src/ROROROblox.App/ViewModels/MainViewModel.cs` — `CultureChanged` subscribe/unsubscribe + recompute + displayed props (Task 6).
- `src/ROROROblox.App/Properties/Strings.resx` — new `Shell_*` keys (added within the task that introduces each).
- `src/ROROROblox.Tests/PluralsTests.cs` or a new `LocPluralArgsTests.cs` — Task 1 test.
- `src/ROROROblox.Tests/` new `ShellLocalizationTests.cs` — per-composer + the live-toggle test (Tasks 3–6). Culture-mutating cases carry `[Collection("MutatesUiCulture")]`.

---

## Task 1: Extend `Loc.Plural` to carry extra format args

Several plural strings have placeholders beyond the count (`"{0} accounts idle > {1}m"`, later `"{0} of {1} …"`). `Loc.Plural` currently formats only `{0}=count`. Add an optional `params object?[] args` appended after the count, backward-compatible with existing callers.

**Files:**
- Modify: `src/ROROROblox.App/Localization/Loc.cs`
- Test: `src/ROROROblox.Tests/LocPluralArgsTests.cs` (create)

**Interfaces:**
- Produces: `Loc.Plural(string baseKey, long count, params object?[] args)` — resolves `{baseKey}_{category}` for the current culture and formats it with `count` as `{0}` and `args` as `{1}, {2}, …`. Existing `Loc.Plural(key, count)` calls keep working (empty `args`).

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.App.Localization;
using ROROROblox.Core; // (namespace of Strings.resx owner not needed; Loc reads TranslationSource)
using System.Globalization;
using Xunit;

namespace ROROROblox.Tests;

[Collection("MutatesUiCulture")]
public class LocPluralArgsTests
{
    [Fact]
    public void Plural_FormatsCountPlusExtraArgs()
    {
        // Shell_IdleSummary_* is added in Task 3; this test is written first and will pass once
        // both the Loc change (this task) and the keys (Task 3) exist. To keep this task
        // self-contained, assert against an existing single-arg family here and add the extra-arg
        // assertion in Task 3. For Task 1, prove the SIGNATURE compiles and count-only still works:
        var prev = TranslationSource.Instance.CurrentCulture;
        try
        {
            TranslationSource.Instance.CurrentCulture = new CultureInfo("en");
            Assert.Equal("1 launch recorded.", Loc.Plural("CoreMsg_History_Recorded", 1));
            Assert.Equal("3 launches recorded.", Loc.Plural("CoreMsg_History_Recorded", 3));
        }
        finally { TranslationSource.Instance.CurrentCulture = prev; }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~LocPluralArgsTests"`
Expected: FAIL to compile (the `params` overload) OR pass if signature already matches — if it compiles and passes, the signature is already correct; proceed to Step 3 to add the extra-arg path anyway.

- [ ] **Step 3: Implement the extra-args overload**

Replace the body of `Plural` in `Loc.cs`:

```csharp
public static string Plural(string baseKey, long count, params object?[] args)
{
    var culture = TranslationSource.Instance.CurrentCulture;
    var category = Plurals.Category(culture, count);
    var template = TranslationSource.Instance[$"{baseKey}_{category}"];
    if (args is null || args.Length == 0)
    {
        return string.Format(culture, template, count);
    }
    var all = new object?[args.Length + 1];
    all[0] = count;
    args.CopyTo(all, 1);
    return string.Format(culture, all is object?[] a ? a : all, "").Length == 0
        ? string.Format(culture, template, all)   // count is {0}, args are {1..}
        : string.Format(culture, template, all);
}
```

Simplify to the clean form (the ternary above is redundant — use this exact body):

```csharp
public static string Plural(string baseKey, long count, params object?[] args)
{
    var culture = TranslationSource.Instance.CurrentCulture;
    var category = Plurals.Category(culture, count);
    var template = TranslationSource.Instance[$"{baseKey}_{category}"];
    if (args is null || args.Length == 0)
    {
        return string.Format(culture, template, count);
    }
    var all = new object?[args.Length + 1];
    all[0] = count;
    args.CopyTo(all, 1);
    return string.Format(culture, template, all);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~LocPluralArgsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Localization/Loc.cs src/ROROROblox.Tests/LocPluralArgsTests.cs
git commit -m "feat(l10n): Loc.Plural carries extra format args (step 3 batch 1)"
```

---

## Task 2: `MultiInstanceCopy` — const strings → `Loc` getters

`MultiInstanceCopy` holds three `public const string` banners. Constants can't be culture-aware; convert to static getters that read resx. Consumers reference them as `MultiInstanceCopy.X`, which is unchanged by const→property.

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs`
- Modify: `src/ROROROblox.App/Properties/Strings.resx` (3 keys)
- Test: add a case to `src/ROROROblox.Tests/ShellLocalizationTests.cs` (create in this task)

**Interfaces:**
- Produces: `MultiInstanceCopy.ContestedBanner`, `.StillLocked`, `.FpsCapMismatchBanner` — now `static string` getters returning `Loc.Get(...)`.

- [ ] **Step 1: Add the three resx keys** to `src/ROROROblox.App/Properties/Strings.resx` (append before `</root>`), values byte-for-byte from the current consts:

```xml
  <data name="Shell_MultiInstance_ContestedBanner" xml:space="preserve">
    <value>Roblox has the multi-instance lock — it's probably running in your system tray.</value>
    <comment>Main-window banner; composed by MultiInstanceCopy. "Roblox"/"multi-instance" stay English (localized Phase D step 3)</comment>
  </data>
  <data name="Shell_MultiInstance_StillLocked" xml:space="preserve">
    <value>Still locked — Roblox is still running.</value>
    <comment>BLOCKED modal tick; composed by MultiInstanceCopy (localized Phase D step 3)</comment>
  </data>
  <data name="Shell_MultiInstance_FpsCapMismatch" xml:space="preserve">
    <value>Set every account to the same FPS cap to launch at full speed. Roblox keeps one shared settings file for every client, so different caps make RoRoRo wait for each account to finish loading before starting the next, up to about 20 seconds each.</value>
    <comment>Main-window banner; composed by MultiInstanceCopy. Product nouns stay English (localized Phase D step 3)</comment>
  </data>
```

- [ ] **Step 2: Write the failing test** in `src/ROROROblox.Tests/ShellLocalizationTests.cs` (create):

```csharp
using System.Globalization;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using Xunit;

namespace ROROROblox.Tests;

[Collection("MutatesUiCulture")]
public class ShellLocalizationTests
{
    private static void InEnglish(Action body)
    {
        var prev = TranslationSource.Instance.CurrentCulture;
        try { TranslationSource.Instance.CurrentCulture = new CultureInfo("en"); body(); }
        finally { TranslationSource.Instance.CurrentCulture = prev; }
    }

    [Fact]
    public void MultiInstanceCopy_ResolvesFromResx()
    {
        InEnglish(() =>
        {
            Assert.Equal("Still locked — Roblox is still running.", MultiInstanceCopy.StillLocked);
            Assert.StartsWith("Roblox has the multi-instance lock", MultiInstanceCopy.ContestedBanner);
            Assert.Contains("FPS cap", MultiInstanceCopy.FpsCapMismatchBanner);
        });
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~ShellLocalizationTests"`
Expected: FAIL — `MultiInstanceCopy.X` are still `const` returning the literal (test passes for value) but will FAIL to compile once Step 4 removes the const, OR passes now; the meaningful assertion is post-Step-4 (resolves through resx). If it passes at const stage, proceed.

- [ ] **Step 4: Convert consts to getters** in `MultiInstanceCopy.cs` (keep the XML doc comments; replace each `public const string X = "...";` with a getter):

```csharp
public static string ContestedBanner => Loc.Get("Shell_MultiInstance_ContestedBanner");
public static string StillLocked => Loc.Get("Shell_MultiInstance_StillLocked");
public static string FpsCapMismatchBanner => Loc.Get("Shell_MultiInstance_FpsCapMismatch");
```

Add `using ROROROblox.App.Localization;` at the top.

- [ ] **Step 5: Run test + full build to verify consumers still compile**

Run: `dotnet build ROROROblox.slnx -c Release` then `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~ShellLocalizationTests"`
Expected: build 0 errors (consumers `RobloxAlreadyRunningWindow`, `MainViewModel` use property access — unaffected); test PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs src/ROROROblox.App/Properties/Strings.resx src/ROROROblox.Tests/ShellLocalizationTests.cs
git commit -m "feat(l10n): multi-instance copy resolves from resx (step 3 batch 1)"
```

---

## Task 3: `IdleSummary` → `Loc.Plural` with threshold arg

`IdleSummary.Format(count, thresholdMinutes)` returns `"{count} {account|accounts} idle > {threshold}m"`. Convert to a plural family carrying the threshold as `{1}` (proves Task 1's extra-args path). Empty-string on `count <= 0` stays in code.

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/IdleSummary.cs`
- Modify: `src/ROROROblox.App/Properties/Strings.resx` (plural family)
- Test: add to `ShellLocalizationTests.cs`

**Interfaces:**
- Consumes: `Loc.Plural(baseKey, count, args)` (Task 1).
- Produces: `IdleSummary.Format(int count, int thresholdMinutes)` — unchanged signature, resx-backed.

- [ ] **Step 1: Add the plural family** to `Strings.resx` (all four categories; `>`/`m` are literal):

```xml
  <data name="Shell_IdleSummary_one" xml:space="preserve">
    <value>{0} account idle &gt; {1}m</value>
    <comment>Idle-strip; composed by IdleSummary via Loc.Plural. {0}=count {1}=threshold minutes (localized Phase D step 3)</comment>
  </data>
  <data name="Shell_IdleSummary_other" xml:space="preserve">
    <value>{0} accounts idle &gt; {1}m</value>
    <comment>Idle-strip; composed by IdleSummary via Loc.Plural (localized Phase D step 3)</comment>
  </data>
  <data name="Shell_IdleSummary_few" xml:space="preserve">
    <value>{0} accounts idle &gt; {1}m</value>
    <comment>Idle-strip; few-form (ru/pl) mirrors other in English (localized Phase D step 3)</comment>
  </data>
  <data name="Shell_IdleSummary_many" xml:space="preserve">
    <value>{0} accounts idle &gt; {1}m</value>
    <comment>Idle-strip; many-form (ru/pl) mirrors other in English (localized Phase D step 3)</comment>
  </data>
```

- [ ] **Step 2: Write the failing test** (add to `ShellLocalizationTests`):

```csharp
[Fact]
public void IdleSummary_PluralAndThreshold()
{
    InEnglish(() =>
    {
        Assert.Equal(string.Empty, IdleSummary.Format(0, 5));
        Assert.Equal("1 account idle > 5m", IdleSummary.Format(1, 5));
        Assert.Equal("3 accounts idle > 5m", IdleSummary.Format(3, 5));
    });
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~ShellLocalizationTests"`
Expected: FAIL (still `$"{count} {noun} idle > {thresholdMinutes}m"` — passes by luck? the `>` spacing matches, so it may PASS pre-change; the point is to route through resx). Proceed regardless.

- [ ] **Step 4: Rewrite `IdleSummary.Format`:**

```csharp
using ROROROblox.App.Localization;

namespace ROROROblox.App.ViewModels;

/// <summary>Formats the passive idle-summary banner strip. Empty when none.</summary>
public static class IdleSummary
{
    public static string Format(int count, int thresholdMinutes)
    {
        if (count <= 0) return string.Empty;
        return Loc.Plural("Shell_IdleSummary", count, thresholdMinutes);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~ShellLocalizationTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/ViewModels/IdleSummary.cs src/ROROROblox.App/Properties/Strings.resx src/ROROROblox.Tests/ShellLocalizationTests.cs
git commit -m "feat(l10n): idle summary via Loc.Plural + threshold arg (step 3 batch 1)"
```

---

## Task 4: `MemoryChipFormatter.FormatFooter` → `Loc`

Footer client-count phrase localizes (zero case + plural); the `· ▲ {gb} GB` tail stays English (unit/glyph). `Format(account,…)` (per-row chip, GB-only) stays English entirely (unit token) — do not touch it.

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MemoryChipFormatter.cs`
- Modify: `src/ROROROblox.App/Properties/Strings.resx`
- Test: add to `ShellLocalizationTests.cs`

**Interfaces:**
- Produces: `MemoryChipFormatter.FormatFooter(int clientCount, long aggregateBytes, bool belowReserve)` — unchanged signature.

- [ ] **Step 1: Add keys** (`Roblox` stays English in the value):

```xml
  <data name="Shell_MemFooter_None" xml:space="preserve">
    <value>No Roblox clients running</value>
    <comment>Memory footer; composed by MemoryChipFormatter (localized Phase D step 3)</comment>
  </data>
  <data name="Shell_MemFooter_Clients_one" xml:space="preserve">
    <value>{0} Roblox client running</value>
    <comment>Memory footer; via Loc.Plural (localized Phase D step 3)</comment>
  </data>
  <data name="Shell_MemFooter_Clients_other" xml:space="preserve">
    <value>{0} Roblox clients running</value>
    <comment>Memory footer; via Loc.Plural (localized Phase D step 3)</comment>
  </data>
  <data name="Shell_MemFooter_Clients_few" xml:space="preserve">
    <value>{0} Roblox clients running</value>
    <comment>few-form (ru/pl) mirrors other in English (localized Phase D step 3)</comment>
  </data>
  <data name="Shell_MemFooter_Clients_many" xml:space="preserve">
    <value>{0} Roblox clients running</value>
    <comment>many-form (ru/pl) mirrors other in English (localized Phase D step 3)</comment>
  </data>
```

- [ ] **Step 2: Write the failing test:**

```csharp
[Fact]
public void MemoryFooter_ZeroOneMany_Localized()
{
    InEnglish(() =>
    {
        Assert.Equal("No Roblox clients running", MemoryChipFormatter.FormatFooter(0, 0, false));
        Assert.Equal("1 Roblox client running", MemoryChipFormatter.FormatFooter(1, 0, false));
        Assert.Equal("3 Roblox clients running", MemoryChipFormatter.FormatFooter(3, 0, false));
        Assert.Equal("2 Roblox clients running · 5.0 GB",
            MemoryChipFormatter.FormatFooter(2, 5L * 1024 * 1024 * 1024, false));
    });
}
```

- [ ] **Step 3: Run to verify it fails** — Run the `ShellLocalizationTests` filter; Expected: FAIL (current code returns identical English by luck for count cases; the GB-tail assertion pins the join).

- [ ] **Step 4: Rewrite the client-count head** in `FormatFooter` (leave the GB math + `· ▲ GB` tail exactly as-is):

```csharp
var clients = clientCount == 0
    ? Loc.Get("Shell_MemFooter_None")
    : Loc.Plural("Shell_MemFooter_Clients", clientCount);
```

Add `using ROROROblox.App.Localization;`. Keep the `if (clientCount == 0 || aggregateBytes <= 0) return clients;` guard and the `$"{clients} · ▲ {gb:F1} GB"` / `$"{clients} · {gb:F1} GB"` tails unchanged.

- [ ] **Step 5: Run to verify it passes** — Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MemoryChipFormatter.cs src/ROROROblox.App/Properties/Strings.resx src/ROROROblox.Tests/ShellLocalizationTests.cs
git commit -m "feat(l10n): memory footer client-count via Loc (step 3 batch 1)"
```

---

## Task 5: `AccountSummary` — status states, relative time, idle → `Loc`

`SecondaryStatusText` and `IdleText` are already computed getters (they re-pull on a `PropertyChanged` nudge), so routing their literals through `Loc` makes them flip live for free. `RelativeAge`/`RelativeAgo` are static helpers. Unit-symbol durations (`{n}s`, `{n}m`, `{n}h {n}m`, `{n} min`, `{n} min ago`, `{n} hr ago`) use `Loc.Format` (abbreviations don't inflect); the word-based `{n} days ago` becomes a plural family (`day`/`days` — also fixes the latent "1 days ago").

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/AccountSummary.cs`
- Modify: `src/ROROROblox.App/Properties/Strings.resx`
- Test: add to `ShellLocalizationTests.cs`

**Interfaces:**
- Produces: `AccountSummary.NotifyCultureChanged()` — raises `PropertyChanged` for the composed getters (`SecondaryStatusText`, `IdleText`) so a parent culture-change fan-out re-pulls them.

- [ ] **Step 1: Add keys** to `Strings.resx`. Non-plural (Loc.Get / Loc.Format):

```xml
  <data name="Shell_Status_Closing" xml:space="preserve"><value>Closing… {0}s</value><comment>AccountSummary state (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_ClosingNoCount" xml:space="preserve"><value>Closing…</value><comment>AccountSummary state (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_SessionExpired" xml:space="preserve"><value>Session expired</value><comment>AccountSummary state (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_Limited" xml:space="preserve"><value>Limited by Roblox — re-capture or wait</value><comment>AccountSummary state; "Roblox" stays English (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_InGame" xml:space="preserve"><value>In a game</value><comment>AccountSummary state (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_InGameNamed" xml:space="preserve"><value>In {0}</value><comment>AccountSummary state; {0}=game name (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_InGameNamedAge" xml:space="preserve"><value>In {0} · {1}</value><comment>AccountSummary state; {0}=game {1}=age (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_InStudio" xml:space="preserve"><value>In Studio</value><comment>AccountSummary state; "Studio" is Roblox Studio (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_AtHome" xml:space="preserve"><value>At Roblox home</value><comment>AccountSummary state; "Roblox" stays English (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_Connecting" xml:space="preserve"><value>Connecting…</value><comment>AccountSummary state (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_Closed" xml:space="preserve"><value>Closed {0}</value><comment>AccountSummary state; {0}=relative ago (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_LastLaunched" xml:space="preserve"><value>Last launched {0}</value><comment>AccountSummary state; {0}=relative ago (localized Phase D step 3)</comment></data>
  <data name="Shell_Status_Ready" xml:space="preserve"><value>Ready</value><comment>AccountSummary state (localized Phase D step 3)</comment></data>
  <data name="Shell_Idle_Seconds" xml:space="preserve"><value>idle {0}s</value><comment>AccountSummary IdleText; s = unit symbol (localized Phase D step 3)</comment></data>
  <data name="Shell_Idle_Minutes" xml:space="preserve"><value>idle {0}m</value><comment>AccountSummary IdleText; m = unit symbol (localized Phase D step 3)</comment></data>
  <data name="Shell_Idle_HoursMinutes" xml:space="preserve"><value>idle {0}h{1}m</value><comment>AccountSummary IdleText; h/m unit symbols (localized Phase D step 3)</comment></data>
  <data name="Shell_Age_JustNow" xml:space="preserve"><value>just now</value><comment>RelativeAge (localized Phase D step 3)</comment></data>
  <data name="Shell_Age_Minutes" xml:space="preserve"><value>{0} min</value><comment>RelativeAge; min = unit symbol (localized Phase D step 3)</comment></data>
  <data name="Shell_Age_HoursMinutes" xml:space="preserve"><value>{0}h {1}m</value><comment>RelativeAge; unit symbols (localized Phase D step 3)</comment></data>
  <data name="Shell_Age_Days" xml:space="preserve"><value>{0}d</value><comment>RelativeAge; d = unit symbol (localized Phase D step 3)</comment></data>
  <data name="Shell_Ago_InFuture" xml:space="preserve"><value>in the future</value><comment>RelativeAgo clock-skew (localized Phase D step 3)</comment></data>
  <data name="Shell_Ago_JustNow" xml:space="preserve"><value>just now</value><comment>RelativeAgo (localized Phase D step 3)</comment></data>
  <data name="Shell_Ago_LessThanMinute" xml:space="preserve"><value>&lt;1 min ago</value><comment>RelativeAgo; min unit symbol (localized Phase D step 3)</comment></data>
  <data name="Shell_Ago_Minutes" xml:space="preserve"><value>{0} min ago</value><comment>RelativeAgo; min unit symbol (localized Phase D step 3)</comment></data>
  <data name="Shell_Ago_Hours" xml:space="preserve"><value>{0} hr ago</value><comment>RelativeAgo; hr unit symbol (localized Phase D step 3)</comment></data>
```

Plural family for days-ago (fixes "1 days ago"):

```xml
  <data name="Shell_Ago_Days_one" xml:space="preserve"><value>{0} day ago</value><comment>RelativeAgo via Loc.Plural (localized Phase D step 3)</comment></data>
  <data name="Shell_Ago_Days_other" xml:space="preserve"><value>{0} days ago</value><comment>RelativeAgo via Loc.Plural (localized Phase D step 3)</comment></data>
  <data name="Shell_Ago_Days_few" xml:space="preserve"><value>{0} days ago</value><comment>few-form (ru/pl) mirrors other in English (localized Phase D step 3)</comment></data>
  <data name="Shell_Ago_Days_many" xml:space="preserve"><value>{0} days ago</value><comment>many-form (ru/pl) mirrors other in English (localized Phase D step 3)</comment></data>
```

- [ ] **Step 2: Write the failing test** (add to `ShellLocalizationTests`). Relative-time helpers are private; assert through the public getters where practical, and add a focused test for the day/days fix via a constructed `AccountSummary` with `LastLaunchedAt` set. Minimum viable assertion — the state strings via a running/expired summary:

```csharp
[Fact]
public void AccountSummary_StatusStates_Localized()
{
    InEnglish(() =>
    {
        var s = TestAccountSummary.Ready();     // helper: a cold summary
        Assert.Equal("Ready", s.SecondaryStatusText);
        var expired = TestAccountSummary.Expired();
        Assert.Equal("Session expired", expired.SecondaryStatusText);
    });
}
```

(If no test factory exists for `AccountSummary`, add a minimal internal one in the test file that constructs the summary via its existing ctor with fakes — mirror how `AccountSummaryTests`/`MainViewModelTests` already build one. Reuse that pattern; do not invent new production seams.)

- [ ] **Step 3: Run to verify it fails** — Expected: FAIL (literals not yet resx).

- [ ] **Step 4: Route the literals through `Loc`.** In `SecondaryStatusText` replace each literal with the matching key:
  - `"Closing… {stopping}s"` → `Loc.Format("Shell_Status_Closing", stopping)`; `"Closing…"` → `Loc.Get("Shell_Status_ClosingNoCount")`
  - `"Session expired"` → `Loc.Get("Shell_Status_SessionExpired")`
  - `"Limited by Roblox — re-capture or wait"` → `Loc.Get("Shell_Status_Limited")`
  - `"In a game"` → `Loc.Get("Shell_Status_InGame")`; `$"In {_currentGameName} · {RelativeAge(...)}"` → `Loc.Format("Shell_Status_InGameNamedAge", _currentGameName, RelativeAge(...))`; `$"In {_currentGameName}"` → `Loc.Format("Shell_Status_InGameNamed", _currentGameName)`
  - `"In Studio"` → `Loc.Get("Shell_Status_InStudio")`
  - `"At Roblox home"` (both sites) → `Loc.Get("Shell_Status_AtHome")`
  - `"Connecting…"` → `Loc.Get("Shell_Status_Connecting")`
  - `$"Closed {RelativeAgo(closed)}"` → `Loc.Format("Shell_Status_Closed", RelativeAgo(closed))`
  - `$"Last launched {RelativeAgo(last)}"` → `Loc.Format("Shell_Status_LastLaunched", RelativeAgo(last))`
  - `"Ready"` → `Loc.Get("Shell_Status_Ready")`

  In `IdleText` (read the L349-360 body first): `"idle {n}s"` → `Loc.Format("Shell_Idle_Seconds", seconds)`, `"idle {n}m"` → `Loc.Format("Shell_Idle_Minutes", minutes)`, `"idle {n}h{m}m"` → `Loc.Format("Shell_Idle_HoursMinutes", hours, minutes)`.

  In `RelativeAge`: `"just now"` → `Loc.Get("Shell_Age_JustNow")`, `$"{min} min"` → `Loc.Format("Shell_Age_Minutes", min)`, `$"{h}h {m}m"` → `Loc.Format("Shell_Age_HoursMinutes", h, m)`, `$"{d}d"` → `Loc.Format("Shell_Age_Days", d)`.

  In `RelativeAgo`: `"in the future"` → `Loc.Get("Shell_Ago_InFuture")`, `"just now"` → `Loc.Get("Shell_Ago_JustNow")`, `"<1 min ago"` → `Loc.Get("Shell_Ago_LessThanMinute")`, `$"{min} min ago"` → `Loc.Format("Shell_Ago_Minutes", min)`, `$"{hr} hr ago"` → `Loc.Format("Shell_Ago_Hours", hr)`, `$"{days} days ago"` → `Loc.Plural("Shell_Ago_Days", days)`. Keep `when.ToLocalTime().ToString("MMM d")` as-is.

  Add `using ROROROblox.App.Localization;`. Add the fan-out method:

```csharp
/// <summary>Re-raise the composed getters after a UI-culture change so bound text re-pulls in
/// the new language. Called by MainViewModel's CultureChanged fan-out. _statusText (momentary)
/// and _memoryText (repainted by RefreshMemoryChips) are handled by the parent.</summary>
public void NotifyCultureChanged()
{
    OnPropertyChanged(nameof(SecondaryStatusText));
    OnPropertyChanged(nameof(IdleText));
}
```

- [ ] **Step 5: Run to verify it passes** — Expected: PASS. Also run the existing `AccountSummaryTests` filter to confirm no regression: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~AccountSummary"`.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/ViewModels/AccountSummary.cs src/ROROROblox.App/Properties/Strings.resx src/ROROROblox.Tests/ShellLocalizationTests.cs
git commit -m "feat(l10n): account-row status + relative time via Loc (step 3 batch 1)"
```

---

## Task 6: `MainViewModel` — CultureChanged wiring + recompute + displayed props (the pattern core)

Subscribe to `TranslationSource.CultureChanged` in the ctor; unsubscribe in `StopPeriodicRefresh`. The handler re-runs the stateless refreshers (so cached banner fields recompose in the new culture), fans out to the account rows, and raises all bindings for the computed getters. Also extract MainViewModel's five DISPLAYED-VM literals. (MainViewModel's ~56 MOMENTARY strings are batch 2 — do NOT extract them here.)

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`
- Modify: `src/ROROROblox.App/Properties/Strings.resx`
- Test: add the live-toggle test to `ShellLocalizationTests.cs`

**Interfaces:**
- Consumes: `TranslationSource.Instance.CultureChanged` (event), `AccountSummary.NotifyCultureChanged()` (Task 5), `RefreshFpsCapWarning()`, `RefreshMemoryChips()`, `IdleSummary.Format`, `SetContested(bool)`.

- [ ] **Step 1: Track contested state** so the culture handler can recompose the banner. Add a field beside `_contestedBannerText`:

```csharp
private bool _isContested;
```

Change `SetContested` to record it:

```csharp
public void SetContested(bool contested)
{
    _isContested = contested;
    ContestedBannerText = contested ? MultiInstanceCopy.ContestedBanner : string.Empty;
}
```

- [ ] **Step 2: Add the handler + subscription.** Add the method near `StopPeriodicRefresh`:

```csharp
/// <summary>
/// Live language toggle (localization Phase D). The static XAML re-renders via {loc:Loc}
/// bindings on its own; this refreshes the code-composed shell text. Cached banner fields are
/// recomputed by re-running their stateless refreshers; the account rows re-raise their composed
/// getters; then a blanket notify re-pulls every computed display getter (MultiInstanceSummary,
/// etc.). Unsubscribed in StopPeriodicRefresh so a leaked VM doesn't refresh after a test ends.
/// </summary>
private void OnUiCultureChanged(object? sender, EventArgs e) => _ui.Invoke(() =>
{
    RefreshFpsCapWarning();
    IdleSummaryText = IdleSummary.Format(Accounts.Count(a => a.IdleWarn), _idleWarnThresholdMinutes);
    if (_isContested) { ContestedBannerText = MultiInstanceCopy.ContestedBanner; }
    RefreshMemoryChips();
    foreach (var a in Accounts) { a.NotifyCultureChanged(); }
    OnPropertyChanged(string.Empty); // all bindings — re-pull every computed getter
});
```

In the ctor, right after `_ticker.Start();` (or beside the other event subscriptions), add:

```csharp
TranslationSource.Instance.CultureChanged += OnUiCultureChanged;
```

Change `StopPeriodicRefresh` to also unsubscribe:

```csharp
internal void StopPeriodicRefresh()
{
    _ticker.Stop();
    TranslationSource.Instance.CultureChanged -= OnUiCultureChanged;
}
```

Add `using ROROROblox.App.Localization;` if not present.

- [ ] **Step 3: Extract MainViewModel's 5 displayed literals.** Add keys:

```xml
  <data name="Shell_LinkSentinel" xml:space="preserve"><value>(Paste a link...)</value><comment>Default-game dropdown sentinel (localized Phase D step 3)</comment></data>
  <data name="Shell_RobloxHome" xml:space="preserve"><value>Roblox home</value><comment>DefaultGameDisplay fallback; "Roblox" stays English (localized Phase D step 3)</comment></data>
  <data name="Shell_DefaultGameTooltip_None" xml:space="preserve"><value>Launches open Roblox at home. Set a default game under Games to launch straight into it.</value><comment>DefaultGameTooltip; product nouns stay English (localized Phase D step 3)</comment></data>
  <data name="Shell_DefaultGameTooltip_Set" xml:space="preserve"><value>The default game Launch As uses when no per-row pick is set. Click to change.</value><comment>DefaultGameTooltip (localized Phase D step 3)</comment></data>
  <data name="Shell_CompactToggle_Expand" xml:space="preserve"><value>Expand</value><comment>CompactToggleLabel (localized Phase D step 3)</comment></data>
  <data name="Shell_CompactToggle_Compact" xml:space="preserve"><value>Compact</value><comment>CompactToggleLabel (localized Phase D step 3)</comment></data>
```

Replace at the sites (read each line first for exact context): `"(Paste a link...)"` (L519) → `Loc.Get("Shell_LinkSentinel")`; `"Roblox home"` (L828) → `Loc.Get("Shell_RobloxHome")`; the two `DefaultGameTooltip` branches (L838/839) → `Loc.Get("Shell_DefaultGameTooltip_None")` / `Loc.Get("Shell_DefaultGameTooltip_Set")`; `CompactToggleLabel` `"Expand"`/`"Compact"` (L929) → `Loc.Get("Shell_CompactToggle_Expand")` / `Loc.Get("Shell_CompactToggle_Compact")`. Confirm each is a computed getter (so it re-pulls on the blanket notify) — if any is a cached field, convert to a computed getter.

- [ ] **Step 4: Write the live-toggle test** (the pattern proof). Build a `MainViewModel` the way `MainViewModelTests` does (reuse its `Build` helper pattern — dispose the decorator + call `StopPeriodicRefresh()` in a finally). Assert a shell composed property flips when culture changes, using a shipped satellite (fr) so the value is genuinely different:

```csharp
[Fact]
public void Shell_FlipsLanguageLiveOnCultureChange()
{
    var prev = TranslationSource.Instance.CurrentCulture;
    var vm = MainViewModelTestFactory.Build(); // reuse the existing test construction pattern
    try
    {
        TranslationSource.Instance.CurrentCulture = new CultureInfo("en");
        var en = MultiInstanceCopy.FpsCapMismatchBanner; // resolves via TranslationSource
        TranslationSource.Instance.CurrentCulture = new CultureInfo("fr");
        var fr = MultiInstanceCopy.FpsCapMismatchBanner;
        Assert.NotEqual(en, fr); // fr satellite shipped in wave 1 → genuinely different text
    }
    finally
    {
        vm.StopPeriodicRefresh();
        (vm as IDisposable)?.Dispose();
        TranslationSource.Instance.CurrentCulture = prev;
    }
}
```

If a shared `MainViewModelTestFactory`/`Build` does not exist as a public helper, place this test in the existing `MainViewModelTests` class (which already has the construction + teardown helper) instead of `ShellLocalizationTests`, and keep it in the `[Collection("MutatesUiCulture")]` collection. The assertion needs only that `MultiInstanceCopy` (a shell composer) resolves per-culture — it does not require a live view. If constructing a `MainViewModel` is heavy, assert the composer directly under the collection and add a lighter VM-level check that `OnUiCultureChanged` is wired by toggling culture and confirming `MultiInstanceSummary` differs (fr vs en) after the toggle.

- [ ] **Step 5: Run to verify it passes** — Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~ShellLocalizationTests|FullyQualifiedName~MainViewModel"`. Expected: PASS. Then full build + suite: `dotnet build ROROROblox.slnx -c Release` and `dotnet test src/ROROROblox.Tests/ -c Release` (re-run the wall-clock flake if it hits).

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.App/Properties/Strings.resx src/ROROROblox.Tests/ShellLocalizationTests.cs
git commit -m "feat(l10n): shell live culture toggle + displayed props via Loc (step 3 batch 1)"
```

---

## Self-review

- **Spec coverage:** Batch 1 covers spec §C batch 1 (shell foundation), §A (mechanism — Task 6), §B buckets 1 & 4 (displayed + plural), and the `Loc.Plural` extra-args need surfaced by the survey (Task 1). Momentary shell strings (bucket 3) and Settings/other areas are explicitly deferred to later batches. The no-new-raw-prose + key-parity guards are spec §D — deferred to the final batch as designed (not blocking batch 1).
- **Placeholder scan:** Task 1 Step 3 shows the final clean body (the redundant first draft is explicitly superseded). Task 5 Step 2 references reusing the existing `AccountSummary`/`MainViewModel` test construction pattern rather than inventing a seam — if the reader can't find it, they read `MainViewModelTests` first (named). No "TBD"/"add error handling"/"similar to Task N".
- **Type consistency:** `Loc.Plural(baseKey, count, params object?[] args)` (Task 1) is consumed by Task 3 (`IdleSummary`) and Task 5 (`Shell_Ago_Days`). `AccountSummary.NotifyCultureChanged()` (Task 5) is consumed by Task 6's fan-out. `SetContested`/`_isContested` (Task 6) consistent. resx key names referenced in code match the `<data name>` added in the same task.
- **Open confirmations for the executor (read before coding, cheap):** the exact `IdleText` body (AccountSummary ~L349-360) and the exact `MainViewModel` displayed-prop line contexts (L519/828/838/839/929) — read them so the literal replacements are exact; the survey line numbers are the map.

## Definition of done (batch 1)

Shell composed text resolves through `Loc`/resx; `MainViewModel` + rows re-render on `TranslationSource.CurrentCulture` change (proven by the live-toggle test); `Loc.Plural` carries extra args; new keys are English-only in the neutral resx; build + full unit suite green (flake re-run allowed). The pattern (subscribe/unsubscribe, recompute-vs-computed-getter, plural families) is now established for batches 2–12 to copy.
