# Metric Alerts Close-Out Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close out external metric alerts: fix the concurrency bug the feature's second producer introduced, make the rules file a shippable permanent source, and stop eight places promising a signed manifest that is not coming.

**Architecture:** No new subsystem. This finishes what is already built.

**Tech Stack:** .NET 10, C# 14, xUnit.

**Spec:** [docs/superpowers/specs/2026-09-09-external-metric-alerts-design.md](../specs/2026-09-09-external-metric-alerts-design.md).

## The decision this plan records

**The signed 626-hosted manifest is dropped.** Este's call, 2026-09-11, on two findings:

1. **The spec contradicts itself in the two clauses that justify the manifest.** §1.5 says it "names URLs the plugin will call" and that this is precisely why it must be signed. §1.6 says "the endpoint URL … entered by the user in plugin settings." If the user supplies the URL, the manifest names none, and the stated reason for signing evaporates.
2. **The manifest's job dissolved when the plugin/core split landed.** Its four payloads are a discovery request, a fetch template, an extract path and rule parameters. After "the plugin fetches, core decides," the first three belong wholly to the plugin — which the user builds and can update whenever they like — and the fourth duplicates the local rules file that already ships. "Change it without a rebuild" was the whole justification, and nothing vendor-specific remains in RoRoRo to rebuild.

Dropping it is also the strongest form of the separation §1.6 is protecting: 626 Labs then hosts nothing about any game at all, rather than hosting a file that describes one.

**What this costs.** A field-path change now means every user updates their own plugin, instead of one re-signed file reaching everyone. That is the real trade, and it is acceptable because the plugin is the user's own and is not distributed by 626 Labs.

## Global Constraints

- **The shipped binary names no vendor, no endpoint and no field path.** Task 3 makes this executable. Note the wrinkle it creates: a test asserting a string is absent has to contain that string. That is fine — it lives in test code, never in the shipped binary — but say so in the fence's own comment so the next reader does not "tidy" it.
- **Nothing on the report path may throw or block.** `LocalFileMetricRuleSource` and the alert dispatcher are both reached from a gRPC handler thread.
- **Never commit** `dev-cert.pfx`/`.cer`, `accounts.dat`, `consent.dat`, `discord.dat`, `notify.dat`, `webview2-data/`, `/plugins/`, `spike/`, or any `.ROBLOSECURITY` value. A pre-commit hook and CI reject cookie prefixes, key files, and any absolute user-profile path in a committed file.
- **Don't rewrite the canonical spec on drift; banner-correct it at the top of the affected part.** That rule governs Task 4.
- **Always name the solution explicitly: `ROROROblox.slnx`.**
- Baseline: `dotnet test ROROROblox.slnx -c Release` gives **2130 unit + 27 harness pass, 1 harness skip by design**.

---

### Task 1: The alert dispatcher has two producers and an unsynchronised dictionary

**Files:**
- Modify: `src/ROROROblox.App/Discord/AlertDispatcher.cs`
- Test: `src/ROROROblox.Tests/Discord/AlertDispatcherTests.cs` (add to the existing class)

**The bug, stated plainly.** `AlertDispatcher._lastSent` is a plain `Dictionary`, read at `AlertDispatcher.cs:67` and written at `:114`. `DispatchAsync` now has **two** producers wired fire-and-forget in `App.xaml.cs`: the view model at `:1862`, raising on the UI thread, and the metric sink at `:1878`, raising on whatever gRPC handler thread served the plugin's report. Two threads can therefore be inside `DispatchAsync` at once, and a `Dictionary` write racing a read can corrupt its bucket chain — the classic symptom being a read that never returns.

Before metric alerts had a destination, the metric path never reached the write, so the view model was the only producer and this could not happen. It went live the moment the kind got a default destination.

**What to fix and what NOT to fix.** Make the dictionary safe for concurrent access. `ConcurrentDictionary<(Guid, AlertKind), DateTimeOffset>` implements `IReadOnlyDictionary<,>`, which is exactly what `AlertRouter.Route` takes at `AlertRouter.cs:36`, so the router needs no change.

Do **not** change when the cooldown stamp is written. There is a second, smaller issue here — the stamp lands *after* the sends complete, so two concurrent dispatches can both pass the cooldown check and both send. Moving the stamp earlier would close that, and would also change behaviour for four already-shipped alert kinds in a change whose job is a data race. Record it in the code as a known, narrower issue; do not fix it here.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public async Task TwoProducersDispatchingConcurrently_DoNotCorruptTheCooldownMap()
    {
        // The dispatcher has two fire-and-forget producers: the view model on the UI thread and
        // the metric sink on a gRPC handler thread. Before metric alerts had a destination the
        // view model was the only one, and an unsynchronised Dictionary was safe.
        var sut = BuildDispatcher();

        var work = Enumerable.Range(0, 64).Select(i => Task.Run(() =>
            sut.DispatchAsync([Trigger(Guid.NewGuid(), (AlertKind)(i % 4))])));

        // Against a plain Dictionary this throws or hangs; a hang is the characteristic symptom,
        // so the timeout is the assertion as much as the absence of an exception is.
        var all = Task.WhenAll(work);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(all, finished);
        await all;
    }
```

`BuildDispatcher` and `Trigger` are illustrative — read the existing tests in that file and reuse their construction helpers and stub senders rather than writing new ones. The dispatcher must be built with a config that actually routes somewhere, or the write at `:114` is never reached and the test proves nothing. Verify that before trusting a green run.

- [ ] **Step 2: Run and watch it fail or hang**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~AlertDispatcher"`
Expected: fails, hangs, or passes flakily. A race test that passes first time against the unfixed code has not reproduced the bug — increase the iteration count until it does, and say in your report what it took.

- [ ] **Step 3: Fix it**

Change the field to a `ConcurrentDictionary` and adjust the write at `:114` to the concurrent idiom. Replace the field's comment with one that names both producers and says why the type matters:

```csharp
    /// <summary>
    /// Keyed by (account, KIND) — see <see cref="AlertRouter.Route"/> for why the kind belongs in
    /// the key. Concurrent because this dispatcher has TWO fire-and-forget producers: the view
    /// model, raising on the UI thread, and the metric sink, raising on whatever gRPC handler
    /// thread served a plugin's report. A plain Dictionary was safe only while the view model was
    /// the sole producer.
    /// <para>
    /// Known and deliberately not fixed here: the cooldown stamp lands AFTER the sends complete,
    /// so two dispatches racing can both pass the cooldown check and both send. Narrower than the
    /// corruption this type prevents, and moving the stamp would change behaviour for four
    /// already-shipped alert kinds.
    /// </para>
    /// </summary>
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~AlertDispatcher"`
Expected: PASS, with every pre-existing test in the file unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Discord/AlertDispatcher.cs src/ROROROblox.Tests/
git commit -m "fix(alerts): the cooldown map has two producers now, and needs to know it"
```

---

### Task 2: The rules file is the permanent source, so make it one

**Files:**
- Modify: `src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs`
- Test: `src/ROROROblox.Tests/Metrics/LocalFileMetricRuleSourceTests.cs` (add to the existing class)

**Two defects that were acceptable in an interim reader and are not in a shipped one.** Both were parked during the plugin-RPC plan explicitly because a signed manifest was going to replace this class. It is not.

1. **One malformed row drops every rule.** The whole array goes through a single `Deserialize`, so a row with a type-mismatched field — `"threshold": "abc"`, or a null where a number belongs — throws inside the deserializer and the catch returns no rules at all. The class already skips a row with an unknown kind and a row with no metric id, so the principle is established; this is the one path that violates it, and a hand-edited file is exactly where a stray quote happens.
2. **The file is re-read and re-parsed on every report.** No cache. The author guide tells plugin authors never to throttle, so this is on the hot path by design. A malformed file also emits its log line once per report, which reads as spam.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void ARowWithAMistypedField_DropsOnlyThatRow()
    {
        // The whole point: a hand-edited file is where a stray quote happens, and losing every
        // rule because of one is indistinguishable from the feature being broken.
        Write("""
        [
          { "metricId": "good", "kind": "Rate", "threshold": 1, "windowMinutes": 5 },
          { "metricId": "bad",  "kind": "Rate", "threshold": "abc", "windowMinutes": 5 },
          { "metricId": "alsogood", "kind": "Event" }
        ]
        """);

        var rules = Sut().CurrentRules();

        Assert.Equal(2, rules.Count);
        Assert.DoesNotContain(rules, r => r.MetricId == "bad");
    }

    [Fact]
    public void ANullWhereANumberBelongs_DropsOnlyThatRow()
    {
        Write("""
        [
          { "metricId": "good", "kind": "Event" },
          { "metricId": "bad",  "kind": "Rate", "threshold": null, "windowMinutes": 5 }
        ]
        """);

        Assert.Equal("good", Assert.Single(Sut().CurrentRules()).MetricId);
    }

    [Fact]
    public void AnUnchangedFile_IsNotReparsedOnEveryCall()
    {
        // Called once per reported metric, on the gRPC path, for the life of the process.
        var sut = Sut();
        Write("""[ { "metricId": "a", "kind": "Event" } ]""");

        var first = sut.CurrentRules();
        var second = sut.CurrentRules();

        // Same instance, not merely equal: proves the parse was skipped, which an equality
        // assertion would not.
        Assert.Same(first, second);
    }

    [Fact]
    public void AnEditedFile_IsStillPickedUpWithoutRestart()
    {
        // The cache must not cost the live-reload behaviour an existing test already pins.
        var sut = Sut();
        Write("""[ { "metricId": "a", "kind": "Event" } ]""");
        Assert.Single(sut.CurrentRules());

        Write("""[ { "metricId": "a", "kind": "Event" }, { "metricId": "b", "kind": "Event" } ]""");
        Assert.Equal(2, sut.CurrentRules().Count);
    }
```

The last test may need the file's write timestamp to actually differ. If your cache keys on last-write-time and the test writes twice inside the filesystem's timestamp resolution, it will fail for a reason that is not a real bug — handle that in the cache design (key on length as well, or on content) rather than by sleeping in the test.

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~LocalFileMetricRuleSource"`
Expected: the two row tests FAIL (all rules dropped), and the cache test FAILS (a fresh list each call).

- [ ] **Step 3: Parse per row**

Deserialize to a shape that lets one row fail without taking the array with it — a `List<JsonElement>`, or a `JsonDocument` walked element by element — and convert each row inside its own try. Keep every existing behaviour: no file means no rules, malformed JSON overall still means no rules and no throw, an unknown kind and a missing metric id still drop their row, and nothing throws out of `CurrentRules`.

- [ ] **Step 4: Cache the parse**

Skip the re-read when the file has not changed. Whatever you key on, the live-reload behaviour must survive, and the log for a malformed file must stop repeating per report.

- [ ] **Step 5: Correct the class's own documentation**

Its summary calls itself "the INTERIM source: plan 3 replaces it with a signed manifest." That is no longer true and this class is now the permanent rule source. Rewrite the summary to say what it is, and keep the reasoning about why it is unsigned — a rule names no URL, so there is no exfiltration primitive in a threshold — because that reasoning is still correct and is why dropping the manifest was safe.

- [ ] **Step 6: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~LocalFileMetricRuleSource"`
Expected: PASS, the existing cases plus the four new ones.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs src/ROROROblox.Tests/
git commit -m "fix(metrics): one bad row costs one rule, and an unchanged file costs nothing"
```

---

### Task 3: The no-vendor fence

**Files:**
- Create: `src/ROROROblox.Tests/NoVendorNameFenceTests.cs`

**What this is for.** The spec's §1.6 ends with a test to apply to any future change: *does the shipped binary name Big Games? If yes, the separation is a fig leaf.* Every plan so far has honoured that by review. This makes it executable, which is the only form that survives a reviewer having an off day.

**The wrinkle, which must go in the fence's own comment.** A test asserting a string is absent has to contain that string. That is fine — the fence lives in the test project, which is never shipped — but without a comment saying so, a future reader will "tidy" the very thing the fence exists to check. Say it plainly in the file.

**Scope it honestly.** Scan the source of the three projects that ship: Core, App and PluginContract. Do not scan the test projects, the docs, or the plan and spec files — those discuss the vendor by necessity and always will.

- [ ] **Step 1: Write the fence**

Model it on an existing source-scanning fence — `BrandNameFenceTests` and `CoreStringBoundaryFenceTests` both walk the tree from disk and are the pattern to follow for locating the source root.

```csharp
namespace ROROROblox.Tests;

/// <summary>
/// The shipped binary names no vendor. This is the spec's own closing test made executable:
/// "does the shipped binary name Big Games? If yes, the separation is a fig leaf."
///
/// <para>
/// <b>Yes, this file contains the very strings it forbids, and that is not an oversight.</b> A
/// fence asserting a string is absent has to name it. This file is in the test project and never
/// ships, so the forbidden terms reach no user, no package and no plugin author. Do not "tidy"
/// them away — they are the fence.
/// </para>
/// <para>
/// Why it matters enough to fence: the whole architecture rests on RoRoRo being a generic metric
/// watcher rather than a client for one company's API. The person calling that API is a user on
/// their own machine, and their plugin — not this binary — is where the endpoint lives. The day
/// a hostname or a product name appears in one of these three projects, that argument is over.
/// </para>
/// </summary>
public class NoVendorNameFenceTests
{
    // Lower-cased and matched case-insensitively. Keep this list short and specific: it must
    // catch a real leak without firing on ordinary English.
    private static readonly string[] Forbidden =
    [
        "biggames", "big games", "bgsi",
        "ps99", "petsim", "pet sim", "pet simulator",
    ];
}
```

Write the test body yourself: enumerate `.cs`, `.proto`, `.resx` and `.xaml` under the three shipping project directories, read each, and fail naming the file, the line and the term. Exclude `bin` and `obj`.

- [ ] **Step 2: Run it and watch it pass, then prove it bites**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~NoVendorNameFence"`
Expected: PASS.

A fence only ever seen passing is not a fence. Temporarily add one of the forbidden terms to a comment in a Core file, confirm the test fails and names that file and line, then remove it. Report what you saw.

- [ ] **Step 3: Commit**

```bash
git add src/ROROROblox.Tests/NoVendorNameFenceTests.cs
git commit -m "test(metrics): make the no-vendor rule executable instead of reviewed"
```

---

### Task 4: Stop promising a manifest

**Files:**
- Modify: `src/ROROROblox.App/App.xaml.cs` (the rule-source registration comment)
- Modify: `src/ROROROblox.App/Plugins/Adapters/MetricReportSinkAdapter.cs` (the `rules` param doc)
- Modify: `src/ROROROblox.Core/Metrics/IMetricRuleSource.cs` (the interface summary)
- Modify: `src/ROROROblox.Core/Discord/DiscordConfig.cs` (the `MetricBreachDestinations` doc)
- Modify: `docs/superpowers/specs/2026-09-09-external-metric-alerts-design.md`
- Modify: `docs/features.md`
- Modify: `docs/superpowers/smoke-metric-alerts.md`
- Modify: `docs/decisions.md`

**Eight sites say a signed manifest is coming.** Task 2 handles the one in `LocalFileMetricRuleSource`; this task handles the rest. Find them all rather than trusting this list — grep the tree for "manifest" and "plan 3" across `src/` and `docs/` and check each hit.

- [ ] **Step 1: Correct the four code comments**

Each currently frames the local file as temporary and the manifest as its replacement. Say instead that the local file is the rule source, and keep whatever reasoning in each comment is still true. `IMetricRuleSource`'s summary is the interesting one: the interface was created so the source could be swapped without the consumer changing. That is still a good reason for it to exist even though the swap will not happen, so say that rather than deleting the seam's rationale.

- [ ] **Step 2: Banner-correct the spec**

Do NOT rewrite it. The repo's rule is to banner-correct the canonical spec at the top of the affected part, and this spec's §0 is a record of live-verified facts that stays valuable regardless.

Add a dated banner under the title, and one under §1.3 and §1.5. The banner must record: that the manifest is dropped as of 2026-09-11; that §1.5 and §1.6 contradicted each other on whether the manifest carries the endpoint; that the plugin/core split left the manifest's payloads with nothing to do; and that dropping it makes the §1.6 separation stronger rather than weaker, because 626 Labs then hosts nothing about any game at all. Also note the cost honestly: a field-path change now means every user updates their own plugin.

Leave §2's "Unsigned manifest — rejected" entry in place and banner it, because the reasoning there is what makes the LOCAL file's lack of a signature correct.

- [ ] **Step 3: Correct the feature ledger and the smoke list**

`docs/features.md`'s metric-alerts row calls the rule source "interim" and says the signed manifest "has not landed." Say what is true: this is the rule source, and it is unsigned because a rule names no URL.

`docs/superpowers/smoke-metric-alerts.md` has a whole section headed "Not runnable yet — waiting on the signed manifest" with three rows. Two of them (manifest rotation, bad signature) describe something that will never exist — remove them. The third, "No vendor hostname ships," is now covered by Task 3's fence, so change it to say the fence covers it and what a person should still check by eye. Do not tick anything.

- [ ] **Step 4: Log the decision**

Add an entry to `docs/decisions.md` following that file's existing format — read the last few entries and match them. It should carry the two findings that killed the manifest, the trade accepted, and the fact that dropping it strengthens the separation. Also add a short dated correction to the 2026-09-09 metric-alerts entry, which describes the manifest as part of the design; that file is append-only by convention, so correct rather than rewrite.

- [ ] **Step 5: Run the suite**

Run: `dotnet test ROROROblox.slnx -c Release`
Expected: PASS. Docs edits can fail this suite — fence tests read the tree from disk and one parses a research document — so it runs even for a documentation change.

- [ ] **Step 6: Commit**

```bash
git add src/ docs/
git commit -m "docs: the manifest is not coming, and here is why that is better"
```

---

## Self-Review

**Coverage.** Task 1 fixes a live data race that the feature's second producer introduced and no per-task review could have seen, because each half was correct alone. Task 2 turns an interim reader into a shippable one by fixing the two defects that were parked precisely because something else was going to replace it. Task 3 makes the spec's own closing test executable. Task 4 stops eight places promising something that is not coming.

**Two judgement calls an implementer should not silently reverse.** Task 1 fixes the corruption and deliberately leaves the narrower cooldown race alone, because closing it would change behaviour for four shipped alert kinds inside a change about a data race. Task 3's fence deliberately contains the strings it forbids, and its comment has to say so or a future reader will delete the fence's own teeth.

**What this plan does not do.** No reference plugin. Nothing in this repo can ship one without naming the vendor, which is the whole point. A plugin belongs in its own repo, owned by the person calling the API — the position every other RoRoRo plugin already occupies.
