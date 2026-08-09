# F-032 Control Labels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop control labels binding `MutedTextBrush` — the token helper prose uses — so that "quiet secondary" is carried by weight and the F-031 edge instead of by a colour that collapses to 1.00:1 under the flatline theme.

**Architecture:** Two setter edits in the shared style dictionary fix 18 buttons; six control labels that bypass the styles are fixed at their call sites (five in XAML, one built in C#). A four-clause fence test then keeps the token off control labels permanently, with its load-bearing clause watching control *styles* rather than elements — because a `<Style>` is not an interactive element, which is exactly how this defect got centralised in the first place.

**Tech Stack:** .NET 10, C# 14, WPF, WPF-UI, xUnit. Build `dotnet build ROROROblox.slnx`. Test `dotnet test ROROROblox.slnx`.

## Global Constraints

- **Solution file is `ROROROblox.slnx`.** A gitignored legacy `ROROROblox.sln` stray may exist. Bare `dotnet build` errors MSB1011 while both are present — always pass `ROROROblox.slnx`.
- **Conventional commits:** `feat` / `fix` / `docs` / `refactor` / `test` / `chore` / `build` / `ci`.
- **No emoji** in UI copy or code.
- **Do NOT touch the ~104 prose uses of `MutedTextBrush`.** Helper text, empty states, `"Follow:"`, memory chips. Muted is correct there. A diff that changes ~104 sites is wrong — the expected change is exactly 7: 1 style setter, 5 inline XAML labels, 1 code-behind button.
- **Exactly three button recipes must remain distinguishable:** Primary (cyan edge), SecondaryStrong (InteractiveEdge + SemiBold), Secondary (InteractiveEdge + Normal).
- **Spec:** `docs/superpowers/specs/2026-08-09-rororo-f032-control-labels-design.md`.
- **Branch:** `feat/f032-control-labels` (already created; spec committed as `1b0ae10`).
- **Register rule (`CLAUDE.md`):** a PR that closes a register row flips that row in the same PR. Task 4 does this — it is not optional.
- Baseline suite at branch point: **1396 passing, 1 skipped.**

## File Structure

| File | Responsibility |
|---|---|
| `src/ROROROblox.Tests/XamlStyleIntegrityTests.cs` (modify) | `XamlStyleScanner` gains `IsInteractive` so both fences share one definition of "is this a control" |
| `src/ROROROblox.Tests/InteractiveEdgeBindingTests.cs` (modify) | drops its private `IsInteractive`, calls the relocated one |
| `src/ROROROblox.Tests/MutedTextFenceTests.cs` (new) | the four-clause fence |
| `src/ROROROblox.App/Controls/ControlStyles.xaml` (modify) | Secondary → `WhiteBrush`; explicit `FontWeight` on both secondary styles |
| `src/ROROROblox.App/MainWindow.xaml` (modify) | 3 inline control labels |
| `src/ROROROblox.App/Preferences/PreferencesWindow.xaml` (modify) | 2 inline control labels |
| `src/ROROROblox.App/SquadLaunch/SquadLaunchWindow.xaml.cs` (modify) | the Remove button built in C#, which no Style reaches |
| `docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md` (modify) | F-032 row → `clean` |

**Locate edits by anchor, not line number.** Line numbers drift; every anchor below was verified unique at plan time.

---

### Task 1: Share the `IsInteractive` discriminator

**Files:**
- Modify: `src/ROROROblox.Tests/XamlStyleIntegrityTests.cs` (the `XamlStyleScanner` class, which starts at ~line 151)
- Modify: `src/ROROROblox.Tests/InteractiveEdgeBindingTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `internal static bool XamlStyleScanner.IsInteractive(XElement el)` — Task 2's fence calls it.

This is a pure relocation. Behaviour must not change, and `InteractiveEdgeBindingTests` must stay green as proof.

- [ ] **Step 1: Move the method**

Cut this method (and its doc comment) from `InteractiveEdgeBindingTests`:

```csharp
    private static bool IsInteractive(XElement el) =>
        el.Attributes().Any(a =>
            a.Name.LocalName.Contains("Mouse", StringComparison.Ordinal)
            || a.Name.LocalName is "Cursor" && a.Value == "Hand"
            || a.Name.LocalName is "InputBindings");
```

Paste it into `XamlStyleScanner` as `internal static`, keeping the doc comment verbatim and appending one sentence to it:

```csharp
    /// <summary>
    /// A shape type that responds to the mouse is a control, whatever its tag says.
    /// <para>
    /// The first version of this test hard-listed <c>Border</c> as decorative full stop. The wave-5
    /// review gate pointed out that <c>MainWindow.xaml</c> has a <c>Border</c> with
    /// <c>Cursor="Hand"</c> and a click handler — the per-account caption swatch — which IS a UI
    /// component under 1.4.11. The old rule would have failed the build on its own correct fix. Role
    /// decides, not element name.
    /// </para>
    /// <para>
    /// Lives here rather than in one test class because two fences need it — the derived-edge fence
    /// and the prose-token fence (F-032). Two copies of a role rule that disagree with each other is
    /// worse than either copy alone.
    /// </para>
    /// </summary>
    internal static bool IsInteractive(XElement el) =>
        el.Attributes().Any(a =>
            a.Name.LocalName.Contains("Mouse", StringComparison.Ordinal)
            || a.Name.LocalName is "Cursor" && a.Value == "Hand"
            || a.Name.LocalName is "InputBindings");
```

- [ ] **Step 2: Update the call sites**

In `InteractiveEdgeBindingTests.cs`, change every bare `IsInteractive(` call to `XamlStyleScanner.IsInteractive(`. Find them with:

```bash
grep -n "IsInteractive(" src/ROROROblox.Tests/InteractiveEdgeBindingTests.cs
```

If `XamlStyleIntegrityTests.cs` lacks `using System.Xml.Linq;`, add it — `XElement` is required.

- [ ] **Step 3: Run the existing fence to prove nothing changed**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~InteractiveEdgeBindingTests"`
Expected: PASS, same count as before the move (4 tests).

- [ ] **Step 4: Run the full suite**

Run: `dotnet test ROROROblox.slnx`
Expected: 1396 passing, 1 skipped. Unchanged — this task adds no tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Tests/XamlStyleIntegrityTests.cs src/ROROROblox.Tests/InteractiveEdgeBindingTests.cs
git commit -m "refactor(tests): share IsInteractive so both fences use one role rule"
```

---

### Task 2: The fence (RED)

**Files:**
- Create: `src/ROROROblox.Tests/MutedTextFenceTests.cs`

**Interfaces:**
- Consumes: `XamlStyleScanner.IsInteractive(XElement)` (Task 1); `XamlStyleScanner.EnumerateAppXamlFiles()` returning `XamlFile(string FullPath, string Label)`; `XamlStyleScanner.AppSourceDirectory()` returning `string?`.
- Produces: nothing later tasks consume.

**This task ends RED on purpose.** Seven real violations exist — one style setter, five inline XAML labels, and one button built in C# — and Task 3 fixes them. Do not fix any production code here, and do not weaken the fence to make it pass.

- [ ] **Step 1: Write the fence**

Create `src/ROROROblox.Tests/MutedTextFenceTests.cs`:

```csharp
using System.IO;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// Keeps the prose token off control labels (F-032).
/// <para>
/// <c>MutedTextBrush</c> does two jobs. On helper text, empty states and chips it is correct and
/// there are ~104 of those. On a control's label it is the defect: muted-vs-white measures 2.42:1
/// in the brand theme and 1.00:1 under flatline, so under a theme the app supports there is nothing
/// separating a control's label from the paragraph beside it. F-031 already shipped the affordance
/// that does the separating — <c>InteractiveEdgeBrush</c>, derived to clear 3:1 under any theme.
/// </para>
/// <para>
/// This is a role fence, not a token ban. Prose keeps the token. What it may not do is label a
/// control.
/// </para>
/// </summary>
public class MutedTextFenceTests
{
    private const string ProseToken = "{DynamicResource MutedTextBrush}";

    /// <summary>
    /// Element types that ARE controls by type. Anything outside this list can still be a control
    /// by behaviour — see <see cref="IsControl"/>.
    /// </summary>
    private static readonly string[] ControlElements =
    [
        "Button", "MenuItem", "ToggleButton", "CheckBox", "RadioButton",
        "ComboBox", "ComboBoxItem", "ListBoxItem", "TabItem", "Hyperlink",
        "TextBox", "PasswordBox", "Slider", "ToggleSwitch", "RepeatButton",
    ];

    /// <summary>
    /// Control by type OR by behaviour. <c>LocalName</c> ignores the xmlns prefix, so
    /// <c>ui:ToggleSwitch</c> matches "ToggleSwitch".
    /// </summary>
    private static bool IsControl(XElement el) =>
        ControlElements.Contains(el.Name.LocalName) || XamlStyleScanner.IsInteractive(el);

    private static IEnumerable<(XDocument Doc, string Label)> AppXaml()
    {
        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            XDocument doc;
            try { doc = XDocument.Load(file.FullPath, LoadOptions.SetLineInfo); }
            catch (System.Xml.XmlException) { continue; }
            yield return (doc, file.Label);
        }
    }

    private static int LineOf(XObject o) =>
        o is IXmlLineInfo { HasLineInfo: true } info ? info.LineNumber : 0;

    /// <summary>
    /// The type of the object being constructed above <paramref name="index"/>, or null. Used only
    /// by the code-behind clause: an object initialiser sets Foreground several lines below its
    /// `new Type`, so the line itself does not say what it is decorating.
    /// </summary>
    private static string? NearestConstructedType(string[] lines, int index)
    {
        for (var i = index; i >= 0 && i > index - 15; i--)
        {
            var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"new\s+([A-Z][A-Za-z0-9_]*)");
            if (m.Success) return m.Groups[1].Value;
        }

        return null;
    }

    [Fact]
    public void NoControlLabelBindsTheProseToken()
    {
        var offenders = new List<string>();

        foreach (var (doc, label) in AppXaml())
        {
            foreach (var el in doc.Descendants())
            {
                if (!IsControl(el)) continue;

                var fg = el.Attribute("Foreground");
                if (fg is not null && fg.Value == ProseToken)
                {
                    offenders.Add($"{label}:{LineOf(el)} <{el.Name.LocalName}> Foreground={ProseToken}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A control's label may not use the prose token. Bind WhiteBrush and let weight plus "
            + "InteractiveEdgeBrush carry 'secondary':\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoControlStyleSetsTheProseTokenAsForeground()
    {
        // THE LOAD-BEARING CLAUSE. A <Style> is not an interactive element, so the element fence
        // above is structurally blind to it — which is exactly how this defect reached 18 buttons
        // at once when the shared dictionary was written. The failure mode was centralisation, so
        // this watches the centre.
        var offenders = new List<string>();

        foreach (var (doc, label) in AppXaml())
        {
            foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
            {
                var target = style.Attribute("TargetType")?.Value ?? "";
                var targetName = target.Split('.', ':').Last().Trim('}', ' ');
                if (!ControlElements.Contains(targetName)) continue;

                foreach (var setter in style.Descendants().Where(e => e.Name.LocalName == "Setter"))
                {
                    if (setter.Attribute("Property")?.Value == "Foreground"
                        && setter.Attribute("Value")?.Value == ProseToken)
                    {
                        offenders.Add(
                            $"{label}:{LineOf(setter)} Style TargetType={targetName} sets Foreground={ProseToken}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A control style may not set the prose token as its foreground — one setter reaches "
            + "every call site at once:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoCodeBehindControlResolvesTheProseTokenForItsForeground()
    {
        // ControlStyles.xaml names two controls the styles cannot reach: CaptionColorPickerWindow's
        // palette swatches and SquadLaunchWindow's Remove. Both set brushes via FindResource, so a
        // change to a Style does not reach them — which makes them the obvious place for this to
        // regrow.
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.NotNull(appDir);

        var offenders = new List<string>();

        foreach (var cs in Directory.EnumerateFiles(appDir!, "*.cs", SearchOption.AllDirectories))
        {
            if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || cs.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var lines = File.ReadAllLines(cs);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("MutedTextBrush", StringComparison.Ordinal)) continue;
                if (!lines[i].Contains("Foreground", StringComparison.Ordinal)) continue;

                // Prose built in code-behind is legitimate and there is a lot of it — seven
                // `new TextBlock` sites set this token correctly. Only a CONTROL is a violation, so
                // walk back to the nearest object being constructed and judge on that.
                var owner = NearestConstructedType(lines, i);
                if (owner is null || !ControlElements.Contains(owner)) continue;

                offenders.Add($"{Path.GetFileName(cs)}:{i + 1}  new {owner} ... {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A control built in code-behind may not resolve the prose token for its foreground:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheFenceSeesTheAppItClaimsTo()
    {
        // A scan that silently matches nothing passes every clause above while checking nothing.
        // ~109 bindings exist at the time of writing; the floor is deliberately far below that so
        // ordinary churn does not trip it, and far above zero so a broken scan does.
        var bindings = 0;

        foreach (var (doc, _) in AppXaml())
        {
            bindings += doc.Descendants()
                .Count(el => el.Attribute("Foreground")?.Value == ProseToken);
        }

        Assert.True(bindings >= 50,
            $"The fence found only {bindings} prose-token bindings. It is supposed to be scanning an "
            + "app with roughly 109 — a count this low means the scan is broken, not that the app "
            + "changed.");
    }
}
```

- [ ] **Step 2: Run it and confirm it fails for the right reasons**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~MutedTextFenceTests"`

Expected: **3 of 4 FAIL.**

- `NoControlLabelBindsTheProseToken` fails naming **5** sites: three in `MainWindow.xaml`, two in `PreferencesWindow.xaml`.
- `NoControlStyleSetsTheProseTokenAsForeground` fails naming **1** site: `SecondaryButtonStyle` in `ControlStyles.xaml`.
- `NoCodeBehindControlResolvesTheProseTokenForItsForeground` fails naming **exactly 1** site: `SquadLaunchWindow.xaml.cs` around line 229, `new Button ... Content = "Remove"`.
- `TheFenceSeesTheAppItClaimsTo` passes (~109 found).

**The code-behind clause must name one site, not eight.** Seven other files set this token from C# on `new TextBlock` — prose, and correct. If that clause names any `TextBlock` site, `NearestConstructedType` is not discriminating and the clause is broken; fix the helper rather than editing prose.

If the offender counts differ from 5, 1 and 1, stop and report — a fence that over-matches will force wrong edits in Task 3.

- [ ] **Step 3: Commit the RED fence**

Committing a failing test is deliberate here: it makes Task 3's diff show the fence going green, which is the evidence that the fix is complete rather than merely plausible.

```bash
git add src/ROROROblox.Tests/MutedTextFenceTests.cs
git commit -m "test(theming): fence the prose token off control labels (RED)"
```

---

### Task 3: Fix the styles and the five inline labels (GREEN)

**Files:**
- Modify: `src/ROROROblox.App/Controls/ControlStyles.xaml`
- Modify: `src/ROROROblox.App/MainWindow.xaml` (3 sites)
- Modify: `src/ROROROblox.App/Preferences/PreferencesWindow.xaml` (2 sites)

**Interfaces:**
- Consumes: the fence from Task 2.
- Produces: nothing later tasks consume.

- [ ] **Step 1: Fix the two secondary styles**

In `src/ROROROblox.App/Controls/ControlStyles.xaml`, find the style with `x:Key="SecondaryButtonStyle"` and change its `Foreground` setter, then add a `FontWeight` setter:

```xml
    <Style x:Key="SecondaryButtonStyle" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="{DynamicResource NavyBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource WhiteBrush}" />
        <Setter Property="FontWeight" Value="Normal" />
        <Setter Property="BorderBrush" Value="{DynamicResource InteractiveEdgeBrush}" />
        <Setter Property="BorderThickness" Value="1" />
    </Style>
```

Find the style with `x:Key="SecondaryStrongButtonStyle"` and add its `FontWeight` setter (its `Foreground` is already correct):

```xml
    <Style x:Key="SecondaryStrongButtonStyle" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="{DynamicResource NavyBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource WhiteBrush}" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="BorderBrush" Value="{DynamicResource InteractiveEdgeBrush}" />
        <Setter Property="BorderThickness" Value="1" />
    </Style>
```

Both weights are explicit on purpose. Leaving quiet secondary's weight implicit means it inherits whatever the ambient default happens to be, and the distinction this change rests on stops being stated anywhere a reader can find it.

- [ ] **Step 2: Update the comment above `SecondaryButtonStyle`**

That comment currently explains only the border fix. Append a paragraph inside the existing `<!-- ... -->` block, immediately before its closing `-->`:

```
         F-032: the label was MutedTextBrush, the same token helper prose uses. Muted-vs-White is
         2.42:1 in brand and 1.00:1 under flatline, so colour could not carry "quiet" under a theme
         this app supports — a control label and the paragraph beside it became the same thing.
         Weight carries it now, and the derived edge above already marks control-vs-prose. Note the
         label was never illegible: Muted on Navy measures 6.88:1 and passes AA. The defect was
         semantic, not contrast.
```

- [ ] **Step 3: Fix the comment on `SecondaryStrongButtonStyle`**

Its comment says the three recipes are *"Navy+MutedText+Divider (quiet secondary), Navy+White+Cyan (primary), and this one — Navy+White+Divider"*. That is now false for the first. Replace that sentence with:

```
         The sweep that consolidated these found THREE recipes, not two. Since F-032 all three
         share a white label and differ by edge and weight: quiet secondary (derived edge, Normal),
         primary (cyan edge), and this one (derived edge, SemiBold).
```

- [ ] **Step 4: Fix the three `MainWindow.xaml` control labels**

Each is a `<Button>`. Change only the `Foreground` attribute on each, from `{DynamicResource MutedTextBrush}` to `{DynamicResource WhiteBrush}`. Locate each by its unique anchor — **all three anchors were verified unique**:

| anchor | what it is |
|---|---|
| `Content="&#x00D7;"` | the tag-dismiss `×` button |
| `Content="Manage games…"` | the default-game popup's footer button |
| `Content="{Binding CompactToggleLabel}"` | the Compact/Expand toggle in the status bar |

The `Foreground` line sits a few lines below each anchor, inside the same element. Do not use line numbers — they have already drifted once on this branch.

**Do not touch any `<TextBlock>`.** Every `MutedTextBrush` foreground on a `TextBlock` in this file is prose and must survive.

- [ ] **Step 5: Fix the two `PreferencesWindow.xaml` control labels**

Both are `<ToggleButton>`. Anchors, both verified unique: `x:Name="MineWebhookReveal"` and `x:Name="ClanWebhookReveal"`. Each has a `Foreground="{DynamicResource MutedTextBrush}"` attribute inside the element; change it to `{DynamicResource WhiteBrush}`.

- [ ] **Step 6: Fix the Remove button built in C#**

`src/ROROROblox.App/SquadLaunch/SquadLaunchWindow.xaml.cs`, around line 229. Anchor on the object initialiser containing `Content = "Remove"`:

```csharp
        var removeBtn = new Button
        {
            Content = "Remove",
            ...
            Foreground = (Brush)FindResource("MutedTextBrush"),
```

Change that one line to:

```csharp
            Foreground = (Brush)FindResource("WhiteBrush"),
```

This is the control `ControlStyles.xaml`'s own comment warns about: *"two buttons are constructed in C# rather than markup — CaptionColorPickerWindow's palette swatches and SquadLaunchWindow's Remove — and they set their brushes via FindResource, not via a Style. [...] a change made here does NOT reach them."* The style edit in Step 1 cannot fix this one, which is exactly why the fence has a code-behind clause.

**Do not touch the other seven `FindResource("MutedTextBrush")` sites in this and other files.** Every one is a `new TextBlock` — prose, and correct. Verify before and after:

```bash
grep -rn 'FindResource("MutedTextBrush")' src/ROROROblox.App --include=*.cs | wc -l
```

Expected: `8` before your edit, `7` after.

- [ ] **Step 7: Run the fence — it must now be fully green**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~MutedTextFenceTests"`
Expected: PASS, 4/4.

If `NoControlLabelBindsTheProseToken` still names sites, you missed one of the five. If it names a `TextBlock`, you have changed prose — revert that edit.

- [ ] **Step 8: Confirm the diff is the right size**

Run: `git diff --stat`

Expected: 4 files changed. The XAML changes must number exactly **6** (1 style setter + 5 inline labels), and the C# change exactly **1**. Verify both:

```bash
git diff -U0 | grep -c '^-.*Foreground="{DynamicResource MutedTextBrush}"'   # expect 6
git diff -U0 | grep -c '^-.*FindResource("MutedTextBrush")'                  # expect 1
```

If that number is anywhere near 100, prose has been swept — revert and redo. This is the single most important check in the task.

- [ ] **Step 9: Prove the load-bearing clause actually bites**

Temporarily revert `SecondaryButtonStyle`'s `Foreground` back to `{DynamicResource MutedTextBrush}`.

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~NoControlStyleSetsTheProseTokenAsForeground"`
Expected: **FAIL**, naming `ControlStyles.xaml` and `TargetType=Button`.

Then restore `WhiteBrush`, re-run, and confirm PASS. Confirm `git diff` on `ControlStyles.xaml` shows only your intended changes before committing.

A gate nobody has watched fail is a gate nobody knows works — and this repo lost three months to a `Style TargetType` bug hiding behind a green suite.

- [ ] **Step 10: Build and run the full suite**

Run: `dotnet build ROROROblox.slnx && dotnet test ROROROblox.slnx`
Expected: `0 Error(s)`; 1400 passing, 1 skipped (1396 + 4 fence tests).

- [ ] **Step 11: Commit**

```bash
git add src/ROROROblox.App/Controls/ControlStyles.xaml src/ROROROblox.App/MainWindow.xaml src/ROROROblox.App/Preferences/PreferencesWindow.xaml src/ROROROblox.App/SquadLaunch/SquadLaunchWindow.xaml.cs
git commit -m "fix(theming): control labels take the primary text token (F-032)"
```

---

### Task 4: Close the register row

**Files:**
- Modify: `docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md` (the F-032 row)

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

`CLAUDE.md` requires the row to flip in the same PR as the fix. That rule exists because six rows had drifted out of sync by 2026-08-09, and one of them cost a build cycle.

- [ ] **Step 1: Flip the status cell**

Find the row beginning `| F-032 |`. Change its **final** cell from `open` to `clean`. Change nothing else on the line — the other cells record what was found, and the register is a record of the finding, not of the build.

Verify the row still has 10 pipe-delimited cells:

```bash
grep -E "^\| F-032 " docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md | awk -F'|' '{print NF-2, "cells; status:", $(NF-1)}'
```

Expected: `10 cells; status:  clean`

- [ ] **Step 2: Re-derive the status counts**

The doc's Status section states counts derived from the rows. Re-derive rather than incrementing by hand — the doc itself explains why, having been wrong once before by carrying a number forward:

```bash
python -c "
import re
from collections import Counter
p='docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md'
rows=[l for l in open(p,encoding='utf-8') if re.match(r'^\| F-\d+ \|',l)]
print(len(rows),'rows',dict(Counter(l.strip().strip('|').split('|')[-1].strip() for l in rows)))"
```

Expected: `84 rows {'open': 51, 'clean': 32, 'closed': 1}`

Update the Status heading line to read **32 clean · 51 open · 1 closed-as-ruled · 84 total**, and add F-032 to the reconciliation section's clean list with a one-line reason: *the label token moved to `WhiteBrush`, quiet secondary now carried by weight plus the F-031 edge, fenced by `MutedTextFenceTests`.*

- [ ] **Step 3: Manual smoke — needs human eyes**

No test in this repo loads XAML, so nothing here proves the buttons render correctly. Run the app:

```bash
dotnet run --project src/ROROROblox.App/ROROROblox.App.csproj
```

Confirm by eye:

1. Secondary buttons (Settings, Tools, per-row Friends/Remove) show white labels, not grey.
2. They still read as quieter than dialog primaries — the difference should now be weight and edge.
3. Helper prose and empty states are **unchanged** — still muted.
4. Switch to the flatline theme in Preferences → Appearance and confirm control labels remain distinguishable from prose.

Point 3 is the regression to watch: if any helper text turned white, prose was swept.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md
git commit -m "docs(findings): F-032 clean — control labels take the primary text token"
```

- [ ] **Step 5: Push and open the PR**

```bash
git push -u origin feat/f032-control-labels
gh pr create --base main --title "F-032 — control labels stop borrowing the prose token" --body "Implements docs/superpowers/specs/2026-08-09-rororo-f032-control-labels-design.md"
```

---

## Self-Review

**Spec coverage.** Token + weight change → Task 3 steps 1-3. Five inline labels → Task 3 steps 4-5. Prose untouched → Task 3 step 7's diff-size gate, plus the fence's own `TextBlock` exclusion. Fence clause 1 → Task 2 `NoControlLabelBindsTheProseToken`. Clause 2 → `NoControlStyleSetsTheProseTokenAsForeground`. Clause 3 → `NoCodeBehindControlResolvesTheProseTokenForItsForeground`. Clause 4 → `TheFenceSeesTheAppItClaimsTo`. Shared discriminator → Task 1. Proven-by-failing → Task 3 step 8. Register flip → Task 4. Three-recipes-stay-three → Task 3 steps 1 and 3. The spec's dropped "indent device" clause is correctly absent from every task.

**Type consistency.** `XamlStyleScanner.IsInteractive(XElement)` is defined in Task 1 and called in Task 2's `IsControl`. `XamlFile(FullPath, Label)` and `AppSourceDirectory()` are existing API, used as declared. `ProseToken`, `ControlElements`, `IsControl`, `AppXaml`, `LineOf` are defined once in Task 2 and used only within that file.

**Spec gap found during self-review, and corrected here.** The spec says five inline control labels. There are **six** sites the styles do not reach: the five in XAML plus `SquadLaunchWindow.xaml.cs`'s Remove button, built in C# with `FindResource`. `ControlStyles.xaml` names that button as style-proof in its own comment, and the spec's fence clause 3 was written for exactly this case — but the spec's site count did not include it. The plan fixes all seven violations (1 style + 5 XAML + 1 C#). Anyone reading the spec alone will undercount by one.

Related: the plan's clause-3 implementation needed a discriminator the spec did not anticipate. Eight code-behind sites set this token; seven are `new TextBlock` and legitimate. A naive line match would have flagged all eight and driven an implementer to "fix" seven pieces of correct prose. `NearestConstructedType` walks back to the constructed type so only controls are flagged.

**Known deviation from the spec, deliberate:** the spec says clause 4 should assert a floor "e.g. 50"; the plan fixes it at exactly 50 and explains the choice inline. No behavioural difference — recorded so a reviewer does not read the concrete number as drift.
