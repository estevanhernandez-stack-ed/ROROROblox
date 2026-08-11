using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

/// <summary>
/// Proves <see cref="ContrastPairGateTests"/> can go red.
/// <para>
/// A gate that has only ever seen passing themes is unproven. Every theme the pair gate measures is
/// a theme the project chose and tuned, so a green run there is consistent with the gate measuring
/// nothing at all. This file feeds it a palette that must fail and asserts it does, by name and by
/// ratio.
/// </para>
/// <para>
/// The fixture is also where three findings' evidence now lives. F-031, F-032, F-050 and F-002 all
/// argue from ratios measured under an adversarial "flatline" theme that was never committed to git
/// and whose JSON is unrecoverable — <c>%LOCALAPPDATA%\ROROROblox\themes\</c> is empty and
/// <c>docs/ui-evidence/</c> holds no flatline round. Shipped <c>flatline</c> is a different theme
/// with different (passing) numbers, so without this fixture those rows would quote numbers nothing
/// can reproduce. The palette below is reconstructed FROM the recorded ratios, and
/// <see cref="ItReproducesTheRegistersRecordedRatios"/> is the check that the reconstruction is
/// faithful rather than invented.
/// </para>
/// <para>
/// WHAT A GREEN RUN HERE MEANS: the gate's arithmetic still separates a bad palette from a good one,
/// and the register's flatline numbers still reproduce. It does not measure the shipped app — that
/// is the sibling file's job.
/// </para>
/// </summary>
public class FlatlineLabGateTests
{
    // Not shipped, not selectable. Reconstructed from the ratios F-031, F-032, F-050 and F-002
    // record, per docs/ui-capture-checklist.md:21-23 — one background, one text colour, one accent.
    //
    // These hexes are not tunable. They are the output of solving the register's recorded ratios,
    // not an input chosen to make this file pass; a fixture adjusted until its own assertions agreed
    // with it would prove nothing. If a measurement below stops matching, the finding is in the
    // measurement path, not in these ten strings.
    private static readonly Theme FlatlineLab = new(
        Id: "flatline-lab", Name: "Flatline Lab",
        Bg: "#0D0D0D", Cyan: "#777777", Magenta: "#777777", White: "#D3D3D3",
        MutedText: "#D3D3D3", Divider: "#0D0D0D", RowBg: "#0D0D0D",
        RowExpiredBg: "#0D0D0D", RowExpiredAccent: "#777777", Navy: "#0D0D0D",
        IsBuiltIn: false);

    /// <summary>WCAG AA for body text, the same threshold the pair gate applies.</summary>
    private const double AaThreshold = 4.5;

    /// <summary>
    /// Mirrors the recorded floor on the pair gate's one exemption (F-050, WhiteBrush on
    /// MagentaBrush). Duplicated rather than shared because that array is private to the gate and
    /// exporting it would make the gate's internals an API for the test project. If the gate ever
    /// moves its floor, this constant has to be re-read against it — the claim this file makes is
    /// specifically that the fixture lands BELOW the exemption's own floor, not merely below AA.
    /// </summary>
    private const double F050ExemptionFloor = 3.20;

    /// <summary>Same floor the pair gate uses, so a broken scan cannot report green here either.</summary>
    private const int MinimumPairs = 6;

    // Deliberate copies of the pair gate's scan regexes, including the left-boundary lookbehind that
    // stops "Background=" matching inside "SelectionBackground=". Not shared for the same reason
    // ExpiredRowRedundancyTests does not share its resolver: a helper reaching across two gate files
    // couples their failure modes, and this file must keep measuring even while the other is edited.
    private static readonly Regex ElementTag = new(@"<\s*[A-Za-z:]+\b[^>]*?>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BackgroundToken = new(@"(?<![A-Za-z.])Background\s*=\s*""\{DynamicResource (\w+)\}""", RegexOptions.Compiled);
    private static readonly Regex ForegroundToken = new(@"(?<![A-Za-z.])Foreground\s*=\s*""\{DynamicResource (\w+)\}""", RegexOptions.Compiled);

    private sealed record Pair(string Fill, string Text, int Sites);

    [Fact]
    public void EveryFixtureSlotResolvesToAMeasurableRatio()
    {
        // FIRST, before any failure is asserted. A fixture that "fails" because a hex will not parse
        // fails for the wrong reason and proves nothing about the gate: ThemeService.ApplySlot
        // returns early on an unparseable value and leaves the previous brush in place, so a typo
        // here would silently measure whatever was in the dictionary before.
        var slots = Resolve(FlatlineLab);

        string[] required =
        [
            ThemeSlots.Bg, ThemeSlots.Cyan, ThemeSlots.Magenta, ThemeSlots.White, ThemeSlots.MutedText,
            ThemeSlots.Divider, ThemeSlots.RowBg, ThemeSlots.RowExpiredBg, ThemeSlots.RowExpiredAccent,
            ThemeSlots.Navy, ThemeSlots.InteractiveEdge,
        ];

        foreach (var slot in required)
        {
            Assert.True(slots.ContainsKey(slot), $"flatline-lab resolved no brush for {slot}.");
        }

        foreach (var slot in required)
        {
            foreach (var other in required)
            {
                Assert.True(ContrastGuard.RatioBetween(slots[slot], slots[other]).HasValue,
                    $"flatline-lab: no ratio computable for {other} on {slot} "
                    + $"({slots[slot]} / {slots[other]}). The fixture is broken, not the gate.");
            }
        }
    }

    [Fact]
    public void ItReproducesTheRegistersRecordedRatios()
    {
        // The reason the fixture exists. Asserted to two decimals and by name so it cannot drift
        // away from the numbers it was built to preserve; a drift here means a register row is now
        // quoting something nothing reproduces, which is the exact defect this cycle is closing.
        var slots = Resolve(FlatlineLab);

        AssertRatio(slots, ThemeSlots.Navy, ThemeSlots.Divider, 1.00, "F-031");
        AssertRatio(slots, ThemeSlots.Navy, ThemeSlots.Cyan, 4.34, "F-031");
        AssertRatio(slots, ThemeSlots.White, ThemeSlots.MutedText, 1.00, "F-032");
        AssertRatio(slots, ThemeSlots.Magenta, ThemeSlots.White, 2.99, "F-050");
        AssertRatio(slots, ThemeSlots.Bg, ThemeSlots.RowBg, 1.00, "F-002");

        // The strongest evidence the reconstruction is faithful. F-031's 4.34 and F-050's 2.99 were
        // recorded months apart in separate findings, against a theme neither author could re-open.
        // Their product, 12.98, is what WhiteBrush-on-NavyBrush has to measure if a single
        // achromatic one-accent palette really produced both. It does. If this number moves, the
        // fixture is wrong and the register is not.
        AssertRatio(slots, ThemeSlots.Navy, ThemeSlots.White, 12.98, "F-031 x F-050 cross-check");
    }

    [Fact]
    public void TheAaMeasurementFailsOnNamedPairs()
    {
        var slots = Resolve(FlatlineLab);

        // Both halves of each claim in one place: the pair measures the ratio the register recorded,
        // AND that ratio is under the bar it has to be under. Split across two facts they can drift
        // apart — a fixture could keep failing while quietly failing at a number nothing recorded,
        // which is how "the gate can go red" degrades into "something is red."
        AssertRatio(slots, ThemeSlots.Magenta, ThemeSlots.White, 2.99, "F-050");
        AssertRatio(slots, ThemeSlots.Cyan, ThemeSlots.Navy, 4.34, "F-031");

        var whiteOnMagenta = ContrastGuard.RatioBetween(slots[ThemeSlots.Magenta], slots[ThemeSlots.White]);
        Assert.True(whiteOnMagenta is not null && whiteOnMagenta.Value < F050ExemptionFloor,
            $"WhiteBrush on MagentaBrush measured {whiteOnMagenta?.ToString("F2") ?? "no ratio"}:1. The "
            + $"fixture has to land below the F-050 exemption's own {F050ExemptionFloor:F2}:1 floor "
            + "(2.99:1 recorded), or it does not exercise the EXEMPTED PAIR GOT WORSE branch at all.");

        var navyOnCyan = ContrastGuard.RatioBetween(slots[ThemeSlots.Cyan], slots[ThemeSlots.Navy]);
        Assert.True(navyOnCyan is not null && navyOnCyan.Value < AaThreshold,
            $"NavyBrush on CyanBrush measured {navyOnCyan?.ToString("F2") ?? "no ratio"}:1, at or above "
            + $"AA's {AaThreshold:F2}:1 (4.34:1 recorded). This is the app's highest-traffic pair — 22 "
            + "sites — and it is the ordinary-AA branch's evidence.");
    }

    [Fact]
    public void ItTripsBothBranchesOfTheGate()
    {
        // Same classification EveryDeclaredPairClearsAaUnderEveryTheme performs, run against the
        // live scan of the app's own XAML rather than a hand-written pair list: an authored list
        // would let this file agree with itself while the app moved underneath it.
        var pairs = ScanPairs();
        Assert.True(pairs.Count >= MinimumPairs,
            $"Found only {pairs.Count} distinct token pairs; expected at least {MinimumPairs}. The scan "
            + "is broken, and a dead scan reports no failures at all — which would read here as the "
            + "gate being unable to fail.");

        var slots = Resolve(FlatlineLab);

        var belowAa = new List<string>();
        var exemptedGotWorse = new List<string>();

        foreach (var pair in pairs)
        {
            var ratio = ContrastGuard.RatioBetween(slots[pair.Fill], slots[pair.Text]);
            Assert.True(ratio.HasValue, $"No ratio for {pair.Text} on {pair.Fill}.");

            var isExempted = pair.Fill == ThemeSlots.Magenta && pair.Text == ThemeSlots.White;
            if (isExempted)
            {
                if (ratio!.Value < F050ExemptionFloor)
                {
                    exemptedGotWorse.Add($"{pair.Text} on {pair.Fill} = {ratio.Value:F2}:1 "
                        + $"(floor {F050ExemptionFloor:F2}:1, {pair.Sites} site(s))");
                }

                continue;
            }

            if (ratio!.Value < AaThreshold)
            {
                belowAa.Add($"{pair.Text} on {pair.Fill} = {ratio.Value:F2}:1 ({pair.Sites} site(s))");
            }
        }

        // Both branches, because they fail for different reasons and a fixture that only trips the
        // ordinary one leaves the exemption's second, lower floor unproven — and that floor is the
        // only thing watching a pair the gate has already forgiven.
        Assert.True(belowAa.Count == 4,
            $"Expected 4 pairs below AA under flatline-lab (measured at /spec, 2026-08-10); got "
            + $"{belowAa.Count}:\n  " + string.Join("\n  ", belowAa)
            + "\nThe fixture's palette is fixed. A different count means the app's declared pair set "
            + "moved, so re-measure and record the new number with its direction.");

        Assert.True(exemptedGotWorse.Count == 1,
            "Expected the F-050 exempted pair to fall below its recorded floor exactly once; got "
            + $"{exemptedGotWorse.Count}:\n  " + string.Join("\n  ", exemptedGotWorse));
    }

    [Fact]
    public void IsBuiltInFalseChangesNothingMeasurable()
    {
        // The fixture is IsBuiltIn: false because it is not built in. That routes it down
        // EdgeRemediation's AskFirst arm instead of DeriveSilently, which sounds like it should
        // change what gets rendered — Resolve derives the same edge for both, so it does not. Stated
        // as a claim in the spec; measured here, because "the code says it cannot matter" is how it
        // matters.
        var fixtureSlots = Resolve(FlatlineLab, out var asFixture);
        var builtInSlots = Resolve(FlatlineLab with { IsBuiltIn = true }, out var asBuiltIn);

        Assert.Equal(EdgeRemediation.Decision.AskFirst, asFixture);
        Assert.Equal(EdgeRemediation.Decision.DeriveSilently, asBuiltIn);

        Assert.Equal(builtInSlots.Count, fixtureSlots.Count);
        foreach (var (key, value) in builtInSlots)
        {
            Assert.True(fixtureSlots.TryGetValue(key, out var mine) && mine == value,
                $"{key} resolved to {fixtureSlots.GetValueOrDefault(key) ?? "nothing"} as a fixture but "
                + $"{value} as a built-in. IsBuiltIn is supposed to change the decision, not the paint.");
        }
    }

    [Fact]
    public async Task TheFixtureIsNotReachableFromTheApp()
    {
        // "Test fixture" is a claim about distribution, not about intent, so it is checked
        // mechanically. A palette this bad reaching the picker would be a shipped accessibility
        // regression wearing the word "lab".
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-flatline-lab-" + Guid.NewGuid().ToString("N"));
        var themes = await new ThemeStore(scratch).ListAsync();
        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
        catch (IOException) { }

        Assert.DoesNotContain(themes, t => t.Id == FlatlineLab.Id);

        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Repo-root walk failed, so this test checked nothing.");

        var shipped = new[] { "ROROROblox.App", "ROROROblox.Core" }
            .Select(p => Path.Combine(root!, "src", p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains(FlatlineLab.Id, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root!, f))
            .ToList();

        Assert.True(shipped.Count == 0,
            $"'{FlatlineLab.Id}' appears in shipped source. It is a test fixture and belongs in no "
            + "assembly a user installs:\n  " + string.Join("\n  ", shipped));
    }

    private static void AssertRatio(
        Dictionary<string, string> slots, string fill, string text, double expected, string source)
    {
        var ratio = ContrastGuard.RatioBetween(slots[fill], slots[text]);
        Assert.True(ratio.HasValue, $"{source}: no ratio for {text} on {fill}.");
        Assert.True(Math.Round(ratio!.Value, 2) == expected,
            $"{source}: {text} on {fill} measured {ratio.Value:F4}:1, recorded {expected:F2}:1 "
            + $"({slots[fill]} / {slots[text]}). The hexes are the reconstruction's output and are "
            + "not tunable — a mismatch is a finding about the register or the measurement path.");
    }

    private static Dictionary<string, string> Resolve(Theme theme) => Resolve(theme, out _);

    /// <summary>
    /// Resolves every slot to its #RRGGBB string through the app's own apply path, and hands back
    /// the remediation decision ApplyTo reached on the way. Same construction
    /// <c>ContrastPairGateTests.ResolveTheme</c> uses, alpha preserved for the same reason: a
    /// translucent brush formatted as opaque would measure a ratio the screen never shows.
    /// </summary>
    private static Dictionary<string, string> Resolve(Theme theme, out EdgeRemediation.Decision decision)
    {
        var resources = new ResourceDictionary();

        // edgeAnswer: null is "the user has not been asked", the default state and the one that lets
        // a built-in derive its edge. It is also what makes the AskFirst/DeriveSilently split above
        // observable at all — an answered theme would collapse both arms onto the same branch.
        decision = ROROROblox.App.Theming.ThemeService.ApplyTo(resources, theme, edgeAnswer: null).Decision;

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in resources.Keys)
        {
            if (key is string name && resources[key] is SolidColorBrush brush)
            {
                var c = brush.Color;
                resolved[name] = c.A == 255
                    ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                    : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }
        }

        return resolved;
    }

    private static IReadOnlyList<Pair> ScanPairs()
    {
        var counts = new Dictionary<(string Fill, string Text), int>();

        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            var text = File.ReadAllText(file.FullPath);
            foreach (Match tag in ElementTag.Matches(text))
            {
                var bg = BackgroundToken.Match(tag.Value);
                var fg = ForegroundToken.Match(tag.Value);
                if (!bg.Success || !fg.Success) continue;

                var key = (bg.Groups[1].Value, fg.Groups[1].Value);
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
        }

        return counts.Select(kv => new Pair(kv.Key.Fill, kv.Key.Text, kv.Value)).ToList();
    }
}
