# Metric Alert Routing Control Implementation Plan

> **SUPERSEDED IN PART (2026-09-11):** every line below that treats the signed manifest as still coming — the ledger sentence in Task 4, the rules-file editor deferred in Self-Review — is out of date; the manifest was dropped the same day and the hand-edited JSON file is the permanent rule source. See [2026-09-11-metric-alerts-close-out.md](2026-09-11-metric-alerts-close-out.md). Everything else shipped as written; the body is left as the record.

---

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give metric alerts a Settings section, so a breach can reach a phone instead of only the desktop toast.

**Architecture:** The four older alert kinds already have a row of four routing checkboxes each, read and written through one handler. This adds a fifth row for `MetricBreach` plus the opt-in toggle that gates the feature, following that pattern exactly. `AlertStatusLine` then stops excluding the kind, taking the gate as a parameter so a fresh install with the shipped default does not claim alerts are configured.

**Tech Stack:** .NET 10, C# 14, WPF with WPF-UI, resx localization across seven languages, xUnit.

**Spec:** [docs/superpowers/specs/2026-09-09-external-metric-alerts-design.md](../specs/2026-09-09-external-metric-alerts-design.md). This work is not a section of that spec — it is a gap its plan 2 review surfaced: §1.1 promises a breach rides the existing rails to any destination, and nothing in the app could point it at one.

**Predecessors:** Plan 1 (`9f39630`) built the core. Plan 2 (`673f93b`) built the RPC and defaulted the destination to the desktop toast. Read `AlertStatusLine.cs`'s comment above the `routed` list before Task 1 — it names this work as the thing that should undo its exclusion.

**Smoke list:** [../smoke-metric-alerts.md](../smoke-metric-alerts.md). Task 4 updates it. This plan moves four rows from not-runnable to runnable, including the live clan-battle one.

## Global Constraints

- **`MetricAlertsEnabled` lives in `settings.json`; the routing destinations live in the encrypted `discord.dat`.** They are two different stores with two different save paths. A control that writes one and forgets the other half-works.
- **Off by default stays off by default.** The opt-in toggle defaults false and nothing here may change that. A user upgrading into this release must not start getting alerts.
- **Themed brushes are replaced, not mutated.** Reference them and the type-ladder tokens with `DynamicResource` only.
- **A button may not paint itself**, and hover/pressed are sheens. Not expected to bite here — these are checkboxes — but the fences are live.
- **No raw English in XAML.** `NoRawXamlProseFenceTests` rejects a raw Text/Content/ToolTip/AutomationProperties.Name; every string goes through `{loc:Loc Key}`. `LocKeyParityFenceTests` then requires every key to resolve.
- **`AccessibleNamingFenceTests` has an unnamed ceiling of 1, asserted as equality.** A new control without an accessible name breaks it, and the ceiling moves only in the same commit as the change that justifies it.
- **`gen-culture-resx.py` refuses an incomplete catalog.** All six languages or none.
- **Never commit** `dev-cert.pfx`/`.cer`, `accounts.dat`, `consent.dat`, `discord.dat`, `notify.dat`, `webview2-data/`, `/plugins/`, `spike/`, or any `.ROBLOSECURITY` value. A pre-commit hook and CI reject cookie prefixes, key files, and any absolute user-profile path in a committed file.
- **Always name the solution explicitly: `ROROROblox.slnx`.** A gitignored legacy `.sln` sits beside it.
- Baseline: `dotnet test ROROROblox.slnx -c Release` gives **2121 unit + 27 harness pass, 1 harness skip by design**.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/ROROROblox.App/Discord/AlertStatusLine.cs` | Stops excluding `MetricBreach`; takes the gate as a parameter. |
| `src/ROROROblox.App/Preferences/SettingsPage.xaml` | The toggle and the four routing checkboxes. |
| `src/ROROROblox.App/Preferences/SettingsPage.xaml.cs` | Paint, read, save, and refresh the running gate on toggle. |
| `src/ROROROblox.App/App.xaml.cs` | Expose the gate so Settings can update it without waiting 30 seconds. |
| `src/ROROROblox.App/Properties/Strings.resx` + six `docs/store/translations/ui-*.json` | The strings. |
| `src/ROROROblox.Tests/SettingsReachabilityTests.cs` | The exemption comes out. |

---

### Task 1: `AlertStatusLine` stops lying in both directions

**Files:**
- Modify: `src/ROROROblox.App/Discord/AlertStatusLine.cs`
- Modify: `src/ROROROblox.Tests/Discord/AlertStatusLineTests.cs` (find the real path; it is the file whose tests call `Compose(new DiscordConfig())`)

**Interfaces:**
- Produces: `AlertStatusLine.Compose(..., bool metricAlertsEnabled = false)` — a TRAILING optional parameter, so every existing call site compiles untouched.

**The problem this solves, stated before the code.** `MetricBreach` was excluded from the composed sentence in plan 2 because its destinations default to the desktop toast while nothing could change them — counting it would have made a fresh install claim alerts were configured. That exclusion is correct only while the kind has no control. Once it has one, the exclusion under-reports: a user who configures ONLY a metric destination would be told "No alerts yet".

The fix is not to flip the exclusion but to make the sentence depend on the thing that actually decides whether a breach can fire. That is `MetricAlertsEnabled`, which lives in `settings.json`, not in the config this composer receives — so it arrives as a parameter.

- [ ] **Step 1: Write the failing tests**

Add to the existing `AlertStatusLine` test class, matching its conventions:

```csharp
    [Fact]
    public void AFreshInstall_StillSaysNoAlertsYet_EvenThoughMetricBreachDefaultsToDesktop()
    {
        // The regression the plan-2 exclusion existed to prevent. MetricBreachDestinations ships
        // as [Local] so a breach has somewhere to go; that is a default, not a user choice, and
        // this sentence reports back what the user chose.
        Assert.Equal("No alerts yet", AlertStatusLine.Compose(new DiscordConfig()));
    }

    [Fact]
    public void WithMetricAlertsOff_TheMetricDestinationIsNotCounted()
    {
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Local] };
        Assert.Equal("No alerts yet", AlertStatusLine.Compose(config, metricAlertsEnabled: false));
    }

    [Fact]
    public void WithMetricAlertsOn_TheMetricDestinationIsCounted()
    {
        // The under-reporting this task fixes: before it, a user who configured ONLY a metric
        // destination was told nothing was configured.
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Local] };

        var line = AlertStatusLine.Compose(config, metricAlertsEnabled: true);

        Assert.NotEqual("No alerts yet", line);
    }

    [Fact]
    public void TheGateDefaultsOff_SoExistingCallersAreUnaffected()
    {
        // The parameter is trailing and optional on purpose. Every pre-existing call site passes
        // nothing and must behave exactly as it did.
        var config = new DiscordConfig { MetricBreachDestinations = [AlertDestination.Phone] };
        Assert.Equal(AlertStatusLine.Compose(config), AlertStatusLine.Compose(config, metricAlertsEnabled: false));
    }
```

The literal `"No alerts yet"` is illustrative — read what the existing tests actually assert (the string is localized through `Loc`) and match them exactly rather than hardcoding English.

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~AlertStatusLine"`
Expected: FAIL to compile — `Compose` has no `metricAlertsEnabled` parameter.

- [ ] **Step 3: Implement**

Add `bool metricAlertsEnabled = false` as the LAST parameter of `Compose`. Replace the exclusion comment with one that says what is true now, and include the kind conditionally:

```csharp
        var routed = config.DestinationsFor(AlertKind.AccountDroppedOut)
            .Concat(config.DestinationsFor(AlertKind.MemoryWarning))
            .Concat(config.DestinationsFor(AlertKind.Recycled))
            .Concat(config.DestinationsFor(AlertKind.UptimeMark))
            // MetricBreach counts only when the feature is switched on. Its destinations default
            // to the desktop toast so a breach has somewhere to go, which is a shipped default
            // rather than a user's choice — and this sentence reports back what the user chose.
            // The switch lives in settings.json, which this composer cannot read, so it arrives
            // as a parameter from the one caller that holds it.
            .Concat(metricAlertsEnabled ? config.DestinationsFor(AlertKind.MetricBreach) : [])
            .ToArray();
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~AlertStatusLine"`
Expected: PASS, including every pre-existing case in that file unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Discord/AlertStatusLine.cs src/ROROROblox.Tests/
git commit -m "fix(alerts): count the metric destination once the user can choose it"
```

---

### Task 2: The Settings section

**Files:**
- Modify: `src/ROROROblox.App/Preferences/SettingsPage.xaml`
- Modify: `src/ROROROblox.App/Preferences/SettingsPage.xaml.cs`
- Modify: `src/ROROROblox.App/App.xaml.cs`
- Modify: `src/ROROROblox.App/Properties/Strings.resx`
- Modify: `src/ROROROblox.Tests/SettingsReachabilityTests.cs`

**Interfaces:**
- Consumes: `AlertStatusLine.Compose(..., bool metricAlertsEnabled)` from Task 1.
- Produces: an `App` member that lets Settings update the running gate immediately.

**Read these before writing anything.** The four existing routing rows in the XAML around the uptime-mark row, and `SetRoutingChecks` / `ReadChecks` / `OnAlertRoutingChanged` in the code-behind. Match them exactly — naming, margins, the `Checked`/`Unchecked` handler wiring, the `_suppressClickHandlers` guard. This task adds a fifth row of the same shape plus one toggle; it invents nothing.

**The two-store trap, which is the thing to get right.** The on/off toggle writes `MetricAlertsEnabled` through `IAppSettings`, into `settings.json`. The four routing checkboxes write `MetricBreachDestinations` through the Discord config, into the encrypted `discord.dat`. They are different stores with different save paths. Wire the toggle to whatever handler pattern the page already uses for an `IAppSettings`-backed checkbox, NOT to `OnAlertRoutingChanged`.

**Why the gate needs a nudge.** `App` re-reads the setting on a 30-second tick, and only while a rules file exists. A user who ticks the toggle and reports immediately would otherwise wait up to 30 seconds, or forever if they have not written a rules file yet. Give `App` a way to be told the gate changed, and call it from the toggle handler. Keep it as small as the existing surface allows — a static method or an internal setter beside the existing volatile field, not a new event bus.

**The singular-field mirror does NOT apply.** `OnAlertRoutingChanged` maintains singular `*Destination` fields as a rollback mirror for older binaries. `MetricBreach` postdates those fields entirely and has no singular counterpart — the existing comment in that handler says so about the two newest kinds. Do not invent one.

- [ ] **Step 1: Write the failing test**

The exemption removal is the test. In `src/ROROROblox.Tests/SettingsReachabilityTests.cs`, delete the whole `new("MetricAlertsEnabled", ...)` entry from the `Exemptions` array. `EveryPersistedSettingIsReachableOrExemptedWithAReason` then fails until a control named after the setting exists, and a second test in that file fails if an exemption names a property that no longer needs one — so removing it before the control exists gives you a red test that goes green when the work is done.

- [ ] **Step 2: Run and watch fail**

Run: `dotnet test src/ROROROblox.Tests/ -c Release --filter "FullyQualifiedName~SettingsReachability"`
Expected: FAIL, naming `MetricAlertsEnabled` as unreachable.

Read that failure message before continuing. It states the naming rule the control must satisfy — the fence matches a control by name against the setting, and guessing the convention instead of reading it is how this task takes three rounds.

- [ ] **Step 3: Add the English strings**

Add to `src/ROROROblox.App/Properties/Strings.resx`. Five keys, following the naming of the uptime-mark row's keys (`SettingsPage_UptimeMarksEvery2Hours`, `SettingsPage_SendUptimeMarksToMy_2` and friends):

| key | value |
| --- | --- |
| `SettingsPage_MetricAlerts` | `Metric alerts` |
| `SettingsPage_MetricAlertsHint` | `Alert me when a number a plugin reports crosses a threshold I set. Needs a plugin that reports one, and a rules file.` |
| `SettingsPage_SendMetricAlertsToThe` | `Send metric alerts to the desktop` |
| `SettingsPage_SendMetricAlertsToMy` | `Send metric alerts to my channel` |
| `SettingsPage_SendMetricAlertsToThe_2` | `Send metric alerts to the clan channel` |
| `SettingsPage_SendMetricAlertsToMy_2` | `Send metric alerts to my phone` |

Voice check: the hint is second person, sentence case, says what the user gets and what it needs, and names no service. The four accessible names follow the uptime row's phrasing exactly so a screen-reader user hears a consistent pattern down the column.

Reuse the existing `SettingsPage_Desktop`, `SettingsPage_MyChannel`, `SettingsPage_ClanChannel` and `SettingsPage_MyPhone` keys for the checkbox content — do not add new ones.

- [ ] **Step 4: Add the XAML**

Add the toggle and the routing row beside the four existing rows. Every string goes through `{loc:Loc Key}`; every checkbox carries an `AutomationProperties.Name`; the four routing checkboxes wire `Checked` and `Unchecked` to `OnAlertRoutingChanged`; the toggle wires to its own handler.

- [ ] **Step 5: Wire the code-behind**

Four edits, each mirroring what the four existing kinds do:
1. `SetRoutingChecks` — four lines painting the new checkboxes from `config.DestinationsFor(AlertKind.MetricBreach)`.
2. `ReadChecks` — a new `AlertKind.MetricBreach` switch arm listing the four boxes.
3. `OnAlertRoutingChanged` — read the new row on the UI thread with the others, and add `MetricBreachDestinations` to the `with` expression. No singular mirror.
4. The toggle's handler — write the setting through `IAppSettings`, then tell `App` the gate changed.

Also paint the toggle from the saved setting wherever the page populates its other `IAppSettings`-backed controls, under the same `_suppressClickHandlers` guard those use.

- [ ] **Step 6: Pass the gate to the status line**

Find the single call site of `AlertStatusLine.Compose` and pass the current value of the metric-alerts setting. That site already awaits `IAppSettings` accessors for other values, so reading one more is in keeping.

- [ ] **Step 7: Build and run everything**

```bash
dotnet build ROROROblox.slnx -c Release && dotnet test ROROROblox.slnx -c Release --no-build
```

Expect fences to have opinions. `AccessibleNamingFenceTests` asserts its unnamed ceiling as EQUALITY, so if your controls are all named the count does not move and it stays green; if it fails, the fix is a missing accessible name, not a ceiling bump. `NoRawXamlProseFenceTests` fails on any raw English. `LocKeyParityFenceTests` fails on a key that does not resolve. Fix the cause, never the fence, unless the fence's own ceiling comment tells you it moves with the change.

- [ ] **Step 8: Commit**

```bash
git add src/ROROROblox.App/Preferences/ src/ROROROblox.App/App.xaml.cs \
        src/ROROROblox.App/Properties/ src/ROROROblox.Tests/
git commit -m "feat(alerts): a metric-alerts section, so a breach can reach a phone"
```

---

### Task 3: The six translations

**Files:**
- Modify: `docs/store/translations/ui-fr.json`, `ui-de.json`, `ui-ru.json`, `ui-pt-BR.json`, `ui-pl.json`, `ui-es.json`

**Interfaces:**
- Consumes: the six keys Task 2 added to the neutral resx.

**Why this is its own task.** Translated copy is the one thing in this repo nobody can proofread by eye at review time unless it is the only thing in the diff. It was kept separate in the plugin-RPC plan for the same reason, and the reviewer there caught a mistyped Spanish word.

- [ ] **Step 1: Add the keys to all six catalogs**

Add each of Task 2's six keys. Translate the hint and the four accessible names; keep `RoRoRo` verbatim if it appears.

| lang | `SettingsPage_MetricAlerts` | `SettingsPage_MetricAlertsHint` |
| --- | --- | --- |
| fr | `Alertes de mesure` | `M'alerter quand un nombre transmis par une extension franchit un seuil que je définis. Nécessite une extension qui en transmet un, et un fichier de règles.` |
| de | `Messwert-Warnungen` | `Benachrichtige mich, wenn eine von einem Plugin gemeldete Zahl einen von mir gesetzten Schwellenwert überschreitet. Braucht ein Plugin, das eine meldet, und eine Regeldatei.` |
| ru | `Оповещения по показателям` | `Уведомлять меня, когда число, переданное плагином, пересекает заданный мной порог. Нужен плагин, который его передаёт, и файл правил.` |
| pt-BR | `Alertas de métrica` | `Me avisar quando um número enviado por um plugin cruzar um limite que eu definir. Precisa de um plugin que envie um, e de um arquivo de regras.` |
| pl | `Alerty o wskaźnikach` | `Powiadamiaj mnie, gdy liczba przesłana przez wtyczkę przekroczy ustawiony przeze mnie próg. Wymaga wtyczki, która ją przesyła, oraz pliku reguł.` |
| es | `Alertas de métricas` | `Avísame cuando un número enviado por un complemento cruce un umbral que yo defina. Necesita un complemento que lo envíe y un archivo de reglas.` |

For the four accessible names, translate them to match how each catalog already phrases the uptime-mark row's four equivalents — read those first and mirror their structure rather than translating the English independently, so a screen-reader user hears the same pattern down the column.

- [ ] **Step 2: Regenerate and lint**

```bash
for c in fr de ru pt-BR pl es; do python scripts/gen-culture-resx.py $c; done
python scripts/lint-translations.py
```
Expected: all six clean, key count risen by exactly six, zero parity candidates.

- [ ] **Step 3: Run the suite**

Run: `dotnet test ROROROblox.slnx -c Release`
Expected: PASS, including the per-language `UiCultureTests` theory.

- [ ] **Step 4: Commit**

```bash
git add docs/store/translations/ src/ROROROblox.App/Properties/
git commit -m "i18n(settings): the metric-alerts section in six languages"
```

---

### Task 4: The smoke list and the ledger

**Files:**
- Modify: `docs/superpowers/smoke-metric-alerts.md`
- Modify: `docs/features.md`

- [ ] **Step 1: Move the unblocked rows**

In the smoke list, three rows under "waiting on the routing control" become runnable, and so does the live clan-battle row that was blocked on the phone. Move them into the runnable section and drop the not-runnable heading if it is now empty. Update the setup section: step 1 no longer needs a hand-edited settings file, because there is a toggle.

**Do not tick anything.** This task verifies nothing on a real machine.

- [ ] **Step 2: Correct the feature ledger**

`docs/features.md` says routing beyond the desktop toast needs a later plan. It does not any more. Say what is true: every destination is reachable, and what remains is the signed manifest for the rule source.

- [ ] **Step 3: Run the suite**

Run: `dotnet test ROROROblox.slnx -c Release`
Expected: PASS. Docs edits can fail this suite — fence tests read the tree from disk — so it runs even for a documentation change.

- [ ] **Step 4: Commit**

```bash
git add docs/
git commit -m "docs: the phone is reachable now, and the smoke list says so"
```

---

## Self-Review

**Coverage.** The gap this plan closes is that `DiscordConfig.MetricBreachDestinations` was init-only with no writer, so spec §1.1's promise that a breach rides the existing rails was true of the router and false of the app. Task 2 gives it a writer. Task 1 stops the status line lying in either direction about it. Task 3 makes it readable in the six languages the app ships. Task 4 tells the truth about what is now testable.

**Two judgement calls an implementer should not silently reverse.** The status-line gate is a TRAILING optional parameter defaulting false, so every existing caller is unaffected and the fresh-install sentence is unchanged. And the toggle writes a different store from the routing checkboxes — one handler cannot serve both.

**What this deliberately does not do.** No rules-file editor. Writing rules stays a hand-edited JSON file until the signed manifest replaces it, and a settings editor for a format with a known expiry date is work thrown away twice.

**Type consistency.** `AlertStatusLine.Compose(..., bool metricAlertsEnabled = false)` is the signature in Tasks 1 and 2. `AlertKind.MetricBreach` and `MetricBreachDestinations` already exist and are unchanged. The four checkbox content strings reuse existing keys; only the row label, the hint and the four accessible names are new, which is why Task 3's count is six.
