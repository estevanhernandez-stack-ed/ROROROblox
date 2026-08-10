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
/// <para>
/// THEME COVERAGE, stated plainly: this gate runs against the three themes <see cref="ThemeStore"/>
/// actually ships — <c>brand</c>, <c>midnight</c>, <c>magenta-heat</c>. "Flatline" — the adversarial
/// one-background/one-text theme the design-review campaign's findings repeatedly cite as a second
/// measurement column — is NOT covered here, because it is not a shipped theme. It exists only in
/// campaign documentation and was never committed as a <see cref="ThemeStore"/> entry. That gap
/// matters concretely: several register findings (F-050 among them) argue from ratios measured
/// under flatline, and this gate cannot reproduce those numbers or re-check them. A pair that passes
/// every assertion below could still collapse under flatline — passing here is not the same claim as
/// passing every theme a design reviewer might reach for.
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

    // Left-boundary lookbehind: without it, "Background=" also matches inside "SelectionBackground="
    // and "Foreground=" inside "PlaceholderForeground=", and Regex.Match returns the FIRST hit in the
    // tag — so a WPF-UI PlaceholderForeground attribute would silently steal the match and the gate
    // would measure the wrong token pair without any indication it had done so. Nothing in the app
    // uses those attributes today, but this is the same shape as a bug this repo has already shipped.
    private static readonly Regex BackgroundToken = new(@"(?<![A-Za-z.])Background\s*=\s*""\{DynamicResource (\w+)\}""", RegexOptions.Compiled);
    private static readonly Regex ForegroundToken = new(@"(?<![A-Za-z.])Foreground\s*=\s*""\{DynamicResource (\w+)\}""", RegexOptions.Compiled);

    /// <summary>
    /// A pair that is allowed to fail AA, and the register row that says why. Each entry is contrast
    /// DEBT, not permission — <see cref="NoExemptionOutlivesItsFinding"/> deletes it for you when the
    /// finding closes. <c>MinimumRatio</c> is a second, lower floor: the exemption forgives today's
    /// known-bad ratio, it does not forgive that ratio getting worse. A pair with no recorded floor
    /// would be watched by nothing at all.
    /// </summary>
    private static readonly (string Fill, string Text, string Finding, double MinimumRatio)[] Exemptions =
    [
        // F-050: white on magenta. Measured 2026-08-09 through this same resolution path
        // (ContrastGuard.RatioBetween on ResolveTheme output, not hand arithmetic): 3.79:1 brand,
        // 4.16:1 midnight, 3.29:1 magenta-heat. The best theme-derived foreground still only reaches
        // 4.40:1 under brand — under AA even after the obvious fix, which is why this is exempted
        // rather than fixed outright. 8 elements use this pair. Open at the time of writing.
        //
        // MinimumRatio 3.20 sits a little below the worst measured theme (magenta-heat, 3.2858),
        // leaving ~0.09 of headroom for measurement noise while still catching a real regression —
        // the scenario this exemption exists to guard against is brand's Magenta getting retuned
        // darker and white-on-magenta sliding from 3.79 toward 2-something while this gate stays
        // green. A floor this close to the worst measured value cannot let that happen silently.
        ("MagentaBrush", "WhiteBrush", "F-050", 3.20),
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

                // Preserve alpha. Dropping it here would silently re-introduce, inside this gate's
                // own resolution step, the exact defect ContrastGuard.Composite's doc already
                // describes fixing: a 12%-alpha white hairline (#20FFFFFF) would format as opaque
                // #FFFFFF and measure 16.66:1 against brand navy when it really lands at 1.46:1. All
                // three built-ins ship 6-digit (fully opaque) hex today, so this is not live against
                // anything currently shipped — but Phase 2 will copy this formatter, and
                // ContrastGuard.TryParse already accepts the #AARRGGBB form this produces.
                resolved[name] = c.A == 255
                    ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                    : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
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
            $"Expected at least the 3 built-in themes (brand, midnight, magenta-heat); got {themes.Count}.");

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

        // A filtered run of the loop below reports pass on a dead scan — the floor that catches a
        // broken scan otherwise lives only in the sibling TheScanSeesTheAppItClaimsTo. Restated here
        // so this test cannot report green on nothing by itself.
        Assert.True(pairs.Count >= MinimumPairs,
            $"Found only {pairs.Count} distinct token pairs; expected at least {MinimumPairs} (9 measured 2026-08-09).");

        var failures = new List<string>();

        foreach (var theme in BuiltInThemes())
        {
            var slots = ResolveTheme(theme);

            foreach (var pair in pairs)
            {
                Assert.True(slots.ContainsKey(pair.Fill), $"Theme '{theme.Id}' resolved no brush for {pair.Fill}.");
                Assert.True(slots.ContainsKey(pair.Text), $"Theme '{theme.Id}' resolved no brush for {pair.Text}.");

                // Computed unconditionally, including for an exempted pair below — an exemption
                // forgives a known ratio, it does not excuse the gate from measuring it.
                var ratio = ContrastGuard.RatioBetween(slots[pair.Fill], slots[pair.Text]);

                // A null means a resolved brush would not parse. Under the pre-fix formatter (see
                // ResolveTheme) this branch was unreachable: dropping alpha meant every string this
                // method could produce was an unconditional 6-digit hex built from valid byte
                // components, which ContrastGuard.TryParse always accepts — so this assertion could
                // never actually fire, no matter what a theme shipped. Preserving alpha routes
                // translucent brushes through TryParse's #AARRGGBB branch for the first time, so this
                // is now the seam that would catch a genuinely malformed resolved value rather than a
                // check that could not have failed either way.
                Assert.True(ratio.HasValue,
                    $"Theme '{theme.Id}': could not compute a ratio for {pair.Text} on {pair.Fill} "
                    + $"({slots[pair.Fill]} / {slots[pair.Text]}).");

                var exemption = Array.Find(Exemptions, e => e.Fill == pair.Fill && e.Text == pair.Text);
                if (exemption.Fill is not null)
                {
                    // Exempted DEBT, not exempted from measurement. The exemption forgives today's
                    // known-bad ratio down to its recorded floor; it does not forgive that ratio
                    // sliding further from AA unnoticed. This is the branch I-1 exists for.
                    if (ratio!.Value < exemption.MinimumRatio)
                    {
                        failures.Add($"{theme.Id}: EXEMPTED PAIR GOT WORSE — {pair.Text} on {pair.Fill} "
                            + $"({exemption.Finding}) = {ratio.Value:F2}:1, below its recorded floor of "
                            + $"{exemption.MinimumRatio:F2}:1 ({pair.Sites} site(s)). This is exempted "
                            + "debt deepening, not a newly discovered violation — re-measure and either "
                            + "tighten the floor to match reality or fix the pair.");
                    }

                    continue;
                }

                if (ratio!.Value < AaThreshold)
                {
                    failures.Add($"{theme.Id}: {pair.Text} on {pair.Fill} = {ratio.Value:F2}:1 "
                        + $"(needs {AaThreshold}:1, {pair.Sites} site(s))");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Colour pairs below WCAG AA (or an exempted pair's debt got worse — see EXEMPTED PAIR "
            + "GOT WORSE lines). Fix the pair, or add/adjust an exemption naming the register row "
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
        foreach (var (fill, text, finding, _) in Exemptions)
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
