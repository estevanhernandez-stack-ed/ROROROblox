# Contrast Token-Pair Gate (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Assert that every colour pair declared inline in the app's XAML clears WCAG AA under all three themes, so a failing pair becomes a red test instead of a finding somebody has to notice.

**Architecture:** A static scan collects element start tags carrying both a `Background` and a `Foreground` `DynamicResource`, collapsing 44 elements to 9 distinct token pairs. Each theme is applied to a throwaway `ResourceDictionary` through a new `ThemeService.ApplyTo` seam — resolved brushes, not the theme record, because `ApplySlot` silently keeps the old brush on an unparseable hex. Ratios come from the app's own `ContrastGuard.RatioBetween`.

**Tech Stack:** .NET 10, C# 14, WPF, xUnit. Build `dotnet build ROROROblox.slnx`. Test `dotnet test ROROROblox.slnx`.

## Global Constraints

- **Solution file is `ROROROblox.slnx`.** A gitignored legacy `ROROROblox.sln` stray may exist. Bare `dotnet build` errors MSB1011 while both are present — always pass `ROROROblox.slnx`.
- **Conventional commits:** `feat` / `fix` / `docs` / `refactor` / `test` / `chore` / `build` / `ci`.
- **No emoji** in code or output.
- **No new NuGet packages.** This repo is deliberately dependency-lean and ships to the Microsoft Store with an auth-cookie threat model.
- **No STA thread, no rendering.** Phase 1 is static. `SolidColorBrush` and `ResourceDictionary` are `DispatcherObject`s and construct fine on an xUnit worker thread; only visuals need STA. If you find yourself needing a `Window`, you have left Phase 1.
- **Assertions are relationships, never absolute colours.** A glow invariant states *"a finding that prescribes a color is invalid"* — users ship their own JSON themes.
- **Spec:** `docs/superpowers/specs/2026-08-09-rororo-rendered-contrast-gate-design.md` (Phase 1 sections only; Phase 2 is a later plan).
- **Branch:** `feat/rendered-contrast-gate` (already created; spec committed as `0b20147`).
- Baseline suite: **1396 passing, 1 skipped.**

## File Structure

| File | Responsibility |
|---|---|
| `src/ROROROblox.App/Theming/ThemeService.cs` (modify) | extract `static ApplyTo(ResourceDictionary, Theme, bool?)`; `ApplyToResources` delegates to it |
| `src/ROROROblox.Tests/ContrastPairGateTests.cs` (new) | the scan, the pairs, the assertions, the exemption table and its meta-test |

Two files. The scan, the resolution and the assertions live together because they are one gate with one reason to change; splitting them across files would mean a reader chasing three files to answer "what does this enforce."

---

### Task 1: Extract the theme-application seam

**Files:**
- Modify: `src/ROROROblox.App/Theming/ThemeService.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `internal static void ThemeService.ApplyTo(ResourceDictionary resources, Theme theme, bool? edgeAnswer)` - Task 2 calls it to resolve a theme with no `Application` and no `ThemeService` instance.

Pure refactor. No behaviour change. Existing theme tests staying green is the proof.

- [ ] **Step 1: Read the current shape**

The method is `private void ApplyToResources(Theme theme, bool? edgeAnswer)` at `ThemeService.cs:173`. It:

1. marshals to the UI thread if needed (`dispatcher.Invoke(() => ApplyToResources(theme, edgeAnswer))`, ~line 181) and returns
2. reads `var resources = Application.Current?.Resources;` and returns when null (~line 185)
3. makes ten `ApplySlot(resources, ThemeSlots.X, theme.X)` calls
4. derives `InteractiveEdge` via `EdgeRemediation.Decide(...)`, which consumes the `edgeAnswer` parameter

Read the whole method before editing. **Steps 3 and 4 use no instance state** - every call in them is static - which is what makes the extraction static.

- [ ] **Step 2: Extract steps 3 and 4 into a static method**

```csharp
    /// <summary>
    /// Writes one theme's slots into a resource dictionary, including the derived interactive edge.
    /// <para>
    /// Static and dictionary-taking so a theme can be resolved with no <see cref="Application"/> and
    /// no <see cref="ThemeService"/> instance. The contrast gate (ContrastPairGateTests) measures the
    /// brushes this produces rather than the raw <see cref="Theme"/> record, because
    /// <c>ApplySlot</c> returns early when a hex will not parse and leaves the previous brush in
    /// place - so the record can say one thing while the app shows another.
    /// </para>
    /// </summary>
    internal static void ApplyTo(ResourceDictionary resources, Theme theme, bool? edgeAnswer)
    {
        // body: the ten ApplySlot calls and the EdgeRemediation-derived InteractiveEdge, moved verbatim
    }
```

`ApplyToResources` keeps its dispatcher marshalling and its null guard, and ends with:

```csharp
        ApplyTo(resources, theme, edgeAnswer);
```

Move the body **verbatim** - same slot order, same comments, no renamed locals. A pure move is what makes "existing tests still green" real evidence rather than a hope.

- [ ] **Step 3: Build**

Run: `dotnet build ROROROblox.slnx`
Expected: `0 Error(s)`

- [ ] **Step 4: Run the theme tests**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~Theme"`
Expected: PASS, same count as before your change.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test ROROROblox.slnx`
Expected: 1396 passing, 1 skipped. Unchanged - this task adds no tests.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/Theming/ThemeService.cs
git commit -m "refactor(theming): extract a static ApplyTo so a theme resolves without an Application"
```

---

### Task 2: The token-pair gate

**Files:**
- Create: `src/ROROROblox.Tests/ContrastPairGateTests.cs`

**Interfaces:**
- Consumes: `static ThemeService.ApplyTo(ResourceDictionary, Theme, bool?)` (Task 1); `XamlStyleScanner.EnumerateAppXamlFiles()` returning `XamlFile(string FullPath, string Label)`; `ContrastGuard.RatioBetween(string?, string?)` returning `double?`; `ThemeStore(string userThemesFolder)` with `ListAsync()` returning `Task<IReadOnlyList<Theme>>`; `XamlStyleScanner.FindRepoRoot()`.
- Produces: nothing later tasks consume.

- [ ] **Step 1: Write the test file**

Create `src/ROROROblox.Tests/ContrastPairGateTests.cs`:

```csharp
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

/// <summary>
/// Every colour pair the app declares inline must clear WCAG AA under every theme.
/// <para>
/// Until this existed, every contrast ratio in every glow finding was arithmetic over theme JSON
/// that nothing re-checked. F-031's 1.26:1, F-032's 1.00:1, F-050's 3.79:1 — all correct, none
/// guarded. A pair that regresses, or a new pair somebody adds without measuring, had nothing to
/// stop it.
/// </para>
/// <para>
/// SCOPE, deliberately narrow: only elements declaring BOTH halves inline. When an element states
/// its own fill and its own foreground, the surface being contrasted against is in the markup and
/// needs no inference. An element that inherits its fill from an ancestor is out of scope — working
/// out what is actually behind it is the composition problem, and a guess would produce a confident
/// wrong number, which is worse than an acknowledged gap.
/// </para>
/// <para>
/// This measures RESOLVED brushes, not the <see cref="Theme"/> record. <c>ThemeService.ApplySlot</c>
/// returns early when a hex will not parse, leaving the previous brush in place — so the record can
/// say one thing while the app shows another. Resolving through <c>ApplyTo</c> is what makes this a
/// gate rather than JSON linting.
/// </para>
/// <para>
/// What it CANNOT see, because it is arithmetic and not a pixel: a control template overriding a
/// setter, a DynamicResource that fails to resolve at runtime, alpha compositing. That is Phase 2's
/// job — see the spec. A green run here does not mean "contrast is verified."
/// </para>
/// </summary>
public class ContrastPairGateTests
{
    /// <summary>WCAG AA for body text. The app's body size is 11px, so the large-text allowance does not apply.</summary>
    private const double AaThreshold = 4.5;

    /// <summary>Measured 2026-08-09: 44 elements across 18 files, collapsing to 9 distinct pairs.</summary>
    private const int MinimumElements = 30;
    private const int MinimumPairs = 6;

    private static readonly Regex ElementTag = new(@"<\s*[A-Za-z:]+\b[^>]*?>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BackgroundToken = new(@"Background\s*=\s*""\{DynamicResource (\w+)\}""", RegexOptions.Compiled);
    private static readonly Regex ForegroundToken = new(@"Foreground\s*=\s*""\{DynamicResource (\w+)\}""", RegexOptions.Compiled);

    /// <summary>
    /// A pair that is allowed to fail, and the register row that says why. Each entry is contrast
    /// DEBT, not permission — <see cref="NoExemptionOutlivesItsFinding"/> deletes it for you when the
    /// finding closes.
    /// </summary>
    private static readonly (string Fill, string Text, string Finding)[] Exemptions =
    [
        // F-050: white on magenta measures 3.79:1 brand / 2.99:1 flatline, and the best
        // theme-derived foreground reaches only 4.40:1 — under AA even after the obvious fix.
        // 8 elements use this pair. Open at the time of writing.
        ("MagentaBrush", "WhiteBrush", "F-050"),
    ];

    private sealed record Pair(string Fill, string Text, int Sites);

    private static IReadOnlyList<Pair> ScanPairs(out int elementCount)
    {
        var counts = new Dictionary<(string Fill, string Text), int>();
        elementCount = 0;

        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            var text = File.ReadAllText(file.FullPath);
            foreach (Match tag in ElementTag.Matches(text))
            {
                var bg = BackgroundToken.Match(tag.Value);
                var fg = ForegroundToken.Match(tag.Value);
                if (!bg.Success || !fg.Success) continue;

                elementCount++;
                var key = (bg.Groups[1].Value, fg.Groups[1].Value);
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
        }

        return counts.Select(kv => new Pair(kv.Key.Fill, kv.Key.Text, kv.Value)).ToList();
    }

    /// <summary>Resolves every theme slot to its #RRGGBB string, through the app's own apply path.</summary>
    private static Dictionary<string, string> ResolveTheme(Theme theme)
    {
        var resources = new ResourceDictionary();

        // edgeAnswer: null means "the user has not been asked about edge remediation", which is the
        // default state and the one that lets a built-in theme derive its edge. Phase 1 does not
        // measure the edge, but ApplyTo writes it and passing the real default keeps this honest.
        ROROROblox.App.Theming.ThemeService.ApplyTo(resources, theme, edgeAnswer: null);

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in resources.Keys)
        {
            if (key is string name && resources[key] is SolidColorBrush brush)
            {
                var c = brush.Color;
                resolved[name] = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
        }

        return resolved;
    }

    /// <summary>
    /// The app's real built-in themes. Deliberately the REAL <see cref="ThemeStore"/>, not a fake -
    /// the whole point is measuring the values the app actually ships. It is pointed at a throwaway
    /// folder rather than the default so user themes sitting in %LOCALAPPDATA% on a dev box cannot
    /// contaminate the result; built-ins are hardcoded and need no filesystem.
    /// </summary>
    private static IReadOnlyList<Theme> BuiltInThemes()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-contrast-gate-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn)
            .ToList();

        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
        catch (IOException) { }

        Assert.True(themes.Count >= 3,
            $"Expected at least the 3 built-in themes (brand, magenta-heat, flatline); got {themes.Count}.");

        return themes;
    }

    [Fact]
    public void TheScanSeesTheAppItClaimsTo()
    {
        // A scan matching nothing passes every assertion below while checking nothing. Floors are
        // set well under the 2026-08-09 measurement (44 elements / 9 pairs) so ordinary churn does
        // not trip them, and well above zero so a broken scan does.
        var pairs = ScanPairs(out var elements);

        Assert.True(elements >= MinimumElements,
            $"Found only {elements} elements declaring both Background and Foreground inline; "
            + $"expected at least {MinimumElements} (44 measured 2026-08-09). The scan is broken, not the app.");
        Assert.True(pairs.Count >= MinimumPairs,
            $"Found only {pairs.Count} distinct token pairs; expected at least {MinimumPairs} (9 measured 2026-08-09).");
    }

    [Fact]
    public void EveryDeclaredPairClearsAaUnderEveryTheme()
    {
        var pairs = ScanPairs(out _);
        var failures = new List<string>();

        foreach (var theme in BuiltInThemes())
        {
            var slots = ResolveTheme(theme);

            foreach (var pair in pairs)
            {
                if (Exemptions.Any(e => e.Fill == pair.Fill && e.Text == pair.Text)) continue;

                Assert.True(slots.ContainsKey(pair.Fill), $"Theme '{theme.Id}' resolved no brush for {pair.Fill}.");
                Assert.True(slots.ContainsKey(pair.Text), $"Theme '{theme.Id}' resolved no brush for {pair.Text}.");

                var ratio = ContrastGuard.RatioBetween(slots[pair.Fill], slots[pair.Text]);

                // A null means a resolved brush would not parse. That is a real defect, not a
                // reason to skip the pair — coercing it to a passing number is how a gate goes blind.
                Assert.True(ratio.HasValue,
                    $"Theme '{theme.Id}': could not compute a ratio for {pair.Text} on {pair.Fill} "
                    + $"({slots[pair.Fill]} / {slots[pair.Text]}).");

                if (ratio!.Value < AaThreshold)
                {
                    failures.Add($"{theme.Id}: {pair.Text} on {pair.Fill} = {ratio.Value:F2}:1 "
                        + $"(needs {AaThreshold}:1, {pair.Sites} site(s))");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Colour pairs below WCAG AA. Fix the pair, or add an exemption naming the register row "
            + "that justifies it:\n  " + string.Join("\n  ", failures));
    }

    [Fact]
    public void NoExemptionOutlivesItsFinding()
    {
        // An exemption list that survives its justification is how a gate becomes decoration.
        var register = Path.Combine(
            XamlStyleScanner.FindRepoRoot()!,
            "docs", "superpowers", "research", "2026-08-04-rororo-settings-ui-audit-findings.md");
        Assert.True(File.Exists(register), $"Register not found at {register}.");

        var statuses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(register))
        {
            var m = Regex.Match(line, @"^\|\s*(F-\d+)\s*\|");
            if (!m.Success) continue;

            var cells = line.Trim().Trim('|').Split('|');
            statuses[m.Groups[1].Value] = cells[^1].Trim();
        }

        var stale = new List<string>();
        foreach (var (fill, text, finding) in Exemptions)
        {
            Assert.True(statuses.ContainsKey(finding),
                $"Exemption for {text} on {fill} names {finding}, which is not a row in the register.");

            if (statuses[finding] != "open")
            {
                stale.Add($"{text} on {fill} cites {finding}, now '{statuses[finding]}'");
            }
        }

        Assert.True(stale.Count == 0,
            "An exemption's finding is no longer open. Remove the exemption and let the gate "
            + "tighten:\n  " + string.Join("\n  ", stale));
    }
}
```

- [ ] **Step 2: Widen `FindRepoRoot`**

`NoExemptionOutlivesItsFinding` needs the repo root to locate the register. `XamlStyleScanner` (bottom of `src/ROROROblox.Tests/XamlStyleIntegrityTests.cs`) has `private static string? FindRepoRoot()` at line 184. Change it to `internal static`, and call it as `XamlStyleScanner.FindRepoRoot()` - drop the `RepoRoot()` alias used in the sketch above and use the real name.

That is the only helper reconciliation this task needs. `ApplyTo` is static (Task 1), so no `ThemeService`, `FakeThemeStore` or `FakeAppSettings` is involved - the two `FakeThemeStore` classes in this project are `private sealed` nested inside other test classes and are not reachable from a new file anyway.

- [ ] **Step 3: Run the gate**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ContrastPairGateTests"`
Expected: PASS, 3 tests.

`EveryDeclaredPairClearsAaUnderEveryTheme` passing on the first run is the expected outcome — under the brand theme only `WhiteBrush` on `MagentaBrush` fails AA, and that one is exempted. If it FAILS, do not widen the exemption list to make it green. Read what it names:

- A failure under `magenta-heat` or `flatline` that does not appear under `brand` is a **real finding**. Stop and report it — it needs a register row before it can be exempted, and inventing an exemption for an unrecorded failure is exactly the drift this gate exists to prevent.
- A failure under `brand` on a pair other than white-on-magenta contradicts the 2026-08-09 measurement. Stop and report; something has changed or the scan is over-matching.

- [ ] **Step 4: Prove the gate bites**

Temporarily add a deliberately failing pair to a XAML file — for example, on any existing element in `src/ROROROblox.App/About/AboutWindow.xaml`, set `Background="{DynamicResource NavyBrush}" Foreground="{DynamicResource DividerBrush}"` (navy on navy-ish divider, far below AA).

Run the filter again. Expected: `EveryDeclaredPairClearsAaUnderEveryTheme` FAILS, naming the theme, the pair, and the computed ratio.

Then revert the XAML edit, re-run, and confirm PASS. Confirm `git diff` on that file is empty before committing.

A gate nobody has watched fail is a gate nobody knows works — this repo lost three months to a bug hiding behind a green suite.

- [ ] **Step 5: Prove the exemption meta-test bites**

Temporarily change the exemption's finding from `"F-050"` to `"F-001"` (which is `clean`).

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~NoExemptionOutlivesItsFinding"`
Expected: FAIL, saying the exemption cites a finding that is no longer open.

Restore `"F-050"`, re-run, confirm PASS. This is the mechanism that stops the exemption list outliving its justification, so it needs the same proof as the gate itself.

- [ ] **Step 6: Run the full suite**

Run: `dotnet build ROROROblox.slnx && dotnet test ROROROblox.slnx`
Expected: `0 Error(s)`; 1399 passing, 1 skipped (1396 + 3).

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.Tests/ContrastPairGateTests.cs src/ROROROblox.Tests/XamlStyleIntegrityTests.cs
git commit -m "test(theming): gate every inline colour pair against WCAG AA under all themes"
```

---

### Task 3: Record the gate in the register

**Files:**
- Modify: `docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md` (the F-050 row)

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

F-050 stays **open** — this gate does not fix it, it measures it. But the row should say a live test now tracks it, so the next reader knows the number is guarded rather than merely written down.

- [ ] **Step 1: Append to F-050's evidence cell**

Find the row beginning `| F-050 |`. Append to its **evidence** cell (index 6 of 10, the one containing the measured ratios) — do not change the status cell, and do not change any other cell:

```
**Gated 2026-08-09:** `ContrastPairGateTests` now measures this pair under all three themes on every test run. It is the one exemption in that gate, citing this row; `NoExemptionOutlivesItsFinding` deletes the exemption automatically when this row stops being `open`.
```

Verify the row still has 10 pipe-delimited cells:

```bash
grep -E "^\| F-050 " docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md | awk -F'|' '{print NF-2, "cells; status:", $(NF-1)}'
```

Expected: `10 cells; status:  open`

- [ ] **Step 2: Confirm counts did not move**

```bash
python -c "
import re
from collections import Counter
p='docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md'
rows=[l for l in open(p,encoding='utf-8') if re.match(r'^\| F-\d+ \|',l)]
print(len(rows),'rows',dict(Counter(l.strip().strip('|').split('|')[-1].strip() for l in rows)))"
```

Expected: `84 rows {'clean': 31, 'open': 52, 'closed': 1}` — unchanged, because no status flipped.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md
git commit -m "docs(findings): F-050 is now gated by a live test, still open"
```

---

## Self-Review

**Spec coverage.** Phase 1 "What it scans" → Task 2 `ScanPairs`, both-tokens-inline only. "What it asserts" → `EveryDeclaredPairClearsAaUnderEveryTheme`, AA 4.5, three themes, `RatioBetween` with a null-fails assertion. "Exemptions" → the `Exemptions` table plus `NoExemptionOutlivesItsFinding`. Vacuity floor → `TheScanSeesTheAppItClaimsTo`. The seam → Task 1. Proven-by-failing → Task 2 steps 4 and 5. Phase 1 acceptance items 1-5 all map. Phase 2 is correctly absent.

**Type consistency.** `ApplyTo(ResourceDictionary, Theme)` defined in Task 1, called in Task 2's `ResolveTheme`. `RatioBetween` used as `double?` with `.HasValue` / `.Value`. `XamlFile(FullPath, Label)` used as declared. `Pair` and `Exemptions` are defined once in Task 2 and used only within that file.

**Corrections made during self-review, from verifying rather than re-reading:**

- The method to extract from is `ApplyToResources(Theme, bool?)` at `ThemeService.cs:173`, not `Apply`. It already takes `edgeAnswer` as a parameter and touches no instance state, so the seam is `internal static` and takes `(ResourceDictionary, Theme, bool?)`.
- That makes `ThemeService` unnecessary in the test, which matters: both `FakeThemeStore` classes in this project are `private sealed` nested inside other test classes and unreachable from a new file. The first draft told the implementer to reuse one. It would not have compiled.
- Themes come from the real `ThemeStore(scratchFolder)`. Built-ins are hardcoded and need no filesystem; the scratch folder stops a dev box's user themes contaminating the measurement.

**Verified signatures:** `ThemeService.ApplyToResources(Theme, bool?)` at `:173`; `ThemeStore(string userThemesFolder)`; `ThemeStore.ListAsync()` returning `Task<IReadOnlyList<Theme>>` with built-ins first; `ContrastGuard.RatioBetween(string?, string?)` returning `double?`; `XamlStyleScanner.FindRepoRoot()` at `:184`, currently private.
