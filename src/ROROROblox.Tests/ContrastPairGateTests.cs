using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
/// THEME COVERAGE, stated plainly: this gate runs against every theme <see cref="ThemeStore"/>
/// returns with <c>IsBuiltIn</c> — <c>brand</c>, <c>midnight</c>, <c>magenta-heat</c>, and since
/// v1.17 <c>flatline</c>. Flatline enrolled itself the moment it shipped; <see cref="BuiltInThemes"/>
/// iterates the real store rather than a list written here, so there was nothing to wire.
/// </para>
/// <para>
/// Two things that used to be true of this doc and are not: flatline is a shipped theme a user can
/// pick, and the adversarial one-background/one-text theme the design-review campaign's findings
/// cite as a second measurement column is a DIFFERENT theme. That one is preserved as
/// <c>flatline-lab</c> in <c>FlatlineLabGateTests</c>, which resolves it through the same
/// <c>ApplyTo</c> path and asserts, by name and to two decimals, that it FAILS — 2.99:1 white on
/// magenta (F-050), 4.34:1 navy on cyan (F-031). So the register's numbers are now reproducible on
/// demand instead of unverifiable, and this gate has been shown capable of going red.
/// </para>
/// <para>
/// What is still NOT covered: user themes. <see cref="BuiltInThemes"/> filters on <c>IsBuiltIn</c>
/// deliberately, so a JSON dropped in <c>%LOCALAPPDATA%</c> is measured by nothing here. And one
/// token slipped out of scope by accident — since PR #100 rebound the last declared
/// <c>MutedTextBrush</c> foreground to <c>WhiteBrush</c>, no scanned element pairs the prose token
/// with a fill, so every binding of it is unmeasured by this gate. Written as "roughly 104" when
/// this doc was authored; re-counted at <b>113</b> on the v1.18 branch, up from 105 at v1.17 —
/// this cycle's own new hint text is the eight. Tracked as F-086; a green run here says nothing
/// about them, and the number it says nothing about keeps growing.
/// </para>
/// </summary>
public class ContrastPairGateTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ContrastPairGateTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>WCAG AA for body text. The app's body size is 11px, so the large-text allowance does not apply.</summary>
    private const double AaThreshold = 4.5;

    /// <summary>
    /// Measured 2026-08-10 against v1.17.0.0: 44 elements across 18 files, collapsing to <b>8</b>
    /// distinct pairs. It was 9 when this gate was authored at <c>1fcf74d</c>. Commit <c>2c9ab16</c>
    /// (PR #100) rebound three <c>MutedTextBrush</c> foregrounds to <c>WhiteBrush</c>, merging
    /// <c>MutedTextBrush on NavyBrush</c> into the existing <c>WhiteBrush on NavyBrush</c> — same 44
    /// elements, one fewer pair. Re-scanned rather than assumed.
    /// <para>
    /// <b>Re-measured on the v1.18 branch: 39 elements across 16 files, still 8 pairs.</b> Down 5
    /// elements and 2 files; pair count flat. The cause is v1.18 item 9 — the five accent-filled
    /// Close buttons it swept to a named style dropped their inline <c>Background</c> and
    /// <c>Foreground</c>, so they leave this gate's scope entirely. Five removed declarations, five
    /// fewer elements, no pair lost. Number and direction recorded per <c>CLAUDE.md</c>'s re-measure
    /// rule; F-086 owns the coverage this scan cannot see, and it just got slightly wider.
    /// </para>
    /// The floors below stay where they are; they are floors, not the measurement.
    /// </summary>
    private const int MinimumElements = 30;
    // Lowered 6 -> 4 at v1.20. The app declared 8 distinct fill/text pairs when every button
    // wrote its own; it declares 5 now because 107 buttons share seven ranks. That is the cycle's
    // entire purpose, and it is ALSO indistinguishable at a glance from a scan that stopped
    // seeing things -- which happened twice earlier in this same cycle. The difference is the
    // element count, which counts SITES and held steady: consolidation moves sites between pairs,
    // lost coverage removes them. Check that number before ever lowering this one again.
    private const int MinimumPairs = 4;

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
        // 4.16:1 midnight, 3.29:1 magenta-heat. Flatline joined in v1.17 at 4.68:1 — above AA, so
        // this exemption is not load-bearing for that theme, and F-050 stays open anyway because
        // shipping a theme that does not need the exemption is not the same as resolving CTA
        // foreground at brush-application time, which is the row's actual fix direction.
        // The best theme-derived foreground still only reaches
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

    /// <summary>
    /// F-086. Pairs measured UNCONDITIONALLY, whatever the element scan happens to find.
    /// <para>
    /// The scan above can only see an element declaring both halves inline. Since PR #100 rebound
    /// the last declared <c>MutedTextBrush</c> foreground to <c>WhiteBrush</c>, the prose token is
    /// the foreground of NO scanned pair — so its ~113 bindings were measured by nothing on any
    /// run, and the coverage kept shrinking as the token spread. <c>MinimumPairs</c> is 4, so
    /// losing it failed nothing and announced nothing.
    /// </para>
    /// <para>
    /// A named list does not have the composition problem the scan correctly refuses to guess at.
    /// The scan cannot know what fill an element inherits from an ancestor; somebody naming a pair
    /// deliberately can. These three are the prose token against the three surfaces it actually
    /// lands on, and they are re-derived every run rather than stated once in a spec.
    /// </para>
    /// <para>
    /// <b>F-050 does not close here.</b> This is its prerequisite, not its fix.
    /// </para>
    /// </summary>
    private static readonly (string Fill, string Text, string Why)[] NamedPairs =
    [
        (ThemeSlots.RowBg, ThemeSlots.MutedText, "the most common prose-on-surface pairing"),
        (ThemeSlots.Bg, ThemeSlots.MutedText, "prose on the page field"),
        (ThemeSlots.Navy, ThemeSlots.MutedText, "the disabled-button label"),
    ];

    private sealed record Pair(string Fill, string Text, int Sites);

    // ----------------------------------------------------------------------------------------
    // Style-setter scanning (v1.20, F-068).
    //
    // The inline scan above matches Background="{DynamicResource X}" on an element tag. A style
    // setter is <Setter Property="Background" Value="{DynamicResource X}" /> -- the literal text
    // Background= never appears, so setters were invisible here.
    //
    // That did not matter while every button carried its colours inline. It matters enormously the
    // moment a migration moves them into ranks: each migrated button DISAPPEARS from this gate.
    // Migrating all 72 would have taken the element count from 39 to near zero, dropping F-050's
    // exempted magenta debt out of measurement entirely, and the gate would have gone on reporting
    // whatever remained. A cycle whose stated purpose is consistency would have bought it by
    // deleting the measurement -- which is this project's most frequent defect, arriving this time
    // as a side effect of the fix.
    //
    // So a rank's pair is measured too, weighted by how many call sites use it.
    /// <summary>
    /// Every keyed style that sets both a Background and a Foreground, with the number of call
    /// sites referencing it. A rank nothing uses reports zero sites and is still measured — an
    /// unused rank with a failing pair is a trap laid for whoever adopts it next.
    /// <para>
    /// <b>Parsed, not pattern-matched.</b> The first version of this used a regex over
    /// <c>&lt;Style ...&gt;...&lt;/Style&gt;</c> and matched <b>nothing</b> — while a byte-identical
    /// pattern matched all eight blocks outside .NET. It reported zero style pairs and the gate
    /// stayed green, which is precisely the defect this method exists to prevent, arriving inside
    /// the fix for it. XAML is XML; parsing it removes a whole class of that.
    /// </para>
    /// </summary>
    /// <summary>
    /// Shared with <c>FlatlineLabGateTests</c>, which ran its own inline-only copy of this scan and
    /// therefore lost sight of every pair v1.20 moved into a rank. One definition, so a fix to it
    /// cannot land in one gate and miss the other — which is exactly what happened first.
    /// </summary>
    internal static IReadOnlyList<(string Fill, string Text, int Sites)> ScanStylePairsShared()
        => ScanStylePairs().Select(p => (p.Fill, p.Text, p.Sites)).ToList();

    private static IReadOnlyList<Pair> ScanStylePairs()
    {
        const string Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";
        var pres = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var x = XNamespace.Get(Xaml2006);

        var files = XamlStyleScanner.EnumerateAppXamlFiles().ToList();
        var texts = files.ToDictionary(f => f.FullPath, f => File.ReadAllText(f.FullPath));

        var pairs = new List<Pair>();
        foreach (var (path, text) in texts)
        {
            XDocument doc;
            try { doc = XDocument.Parse(text); }
            catch (System.Xml.XmlException) { continue; } // a malformed view file is XamlStyleIntegrityTests' problem

            foreach (var style in doc.Descendants(pres + "Style"))
            {
                var key = (string?)style.Attribute(x + "Key");
                if (string.IsNullOrEmpty(key)) continue;

                string? fill = null, fg = null;
                foreach (var setter in style.Elements(pres + "Setter"))
                {
                    var prop = (string?)setter.Attribute("Property");
                    var value = (string?)setter.Attribute("Value");
                    if (prop is null || value is null) continue;

                    var token = DynamicToken(value);
                    if (token is null) continue;
                    if (prop == "Background") fill = token;
                    else if (prop == "Foreground") fg = token;
                }
                if (fill is null || fg is null) continue;

                var uses = texts.Values.Sum(t2 =>
                    Regex.Matches(t2, $@"(?:Static|Dynamic)Resource\s+{Regex.Escape(key)}\s*\}}").Count);

                pairs.Add(new Pair(fill, fg, uses));
            }
        }
        return pairs;
    }

    /// <summary>"{DynamicResource NavyBrush}" -> "NavyBrush"; null for anything else.</summary>
    private static string? DynamicToken(string value)
    {
        var m = Regex.Match(value, @"^\{DynamicResource\s+(\w+)\}$");
        return m.Success ? m.Groups[1].Value : null;
    }

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

        var inline = counts.Select(kv => new Pair(kv.Key.Fill, kv.Key.Text, kv.Value));

        // Rank sites count as measured elements too. Without this the floor below reads a
        // migration as lost coverage: v1.20 moved 21 buttons out of inline markup and the inline
        // element count fell 39 -> 28, through a floor of 30, while the pairs themselves were
        // still measured -- just through their rank. The floor is there to catch a BROKEN scan,
        // and a scan that follows colours into styles is the opposite of broken.
        var styleP = ScanStylePairs();
        elementCount += styleP.Sum(sp => sp.Sites);

        // Fold in the ranks. A pair declared once in a Style and used by forty buttons is exactly
        // as load-bearing as the same pair written inline forty times -- more so, since fixing it
        // is one edit. Merged by (fill, text) so a pair reachable both ways is measured once with
        // its true site count.
        return inline.Concat(styleP)
            .GroupBy(p => (p.Fill, p.Text))
            .Select(g => new Pair(g.Key.Fill, g.Key.Text, g.Sum(x => x.Sites)))
            .ToList();
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

        Assert.True(themes.Count >= 4,
            $"Expected at least the 4 built-in themes (brand, midnight, magenta-heat, flatline); got {themes.Count}.");

        return themes;
    }

    [Fact]
    public void TheScanSeesTheAppItClaimsTo()
    {
        // A scan matching nothing passes every assertion below while checking nothing. Floors are
        // set well under the current measurement (39 elements / 8 pairs) so ordinary churn does
        // not trip them, and well above zero so a broken scan does. Worth naming what that headroom
        // cost twice over: the pair count fell from 9 to 8 at PR #100 and the element count from 44
        // to 39 at v1.18 item 9, and nothing said so either time, because a floor of 6 cannot
        // notice. The floors are the right shape for catching a broken scan and the wrong shape for
        // catching lost coverage — F-086.
        var pairs = ScanPairs(out var elements);

        Assert.True(elements >= MinimumElements,
            $"Found only {elements} elements declaring both Background and Foreground inline; "
            + $"expected at least {MinimumElements} (39 measured on the v1.18 branch, down from 44 "
            + "at v1.17). The scan is broken, not the app.");
        Assert.True(pairs.Count >= MinimumPairs,
            $"Found only {pairs.Count} distinct token pairs; expected at least {MinimumPairs} (8 measured on the v1.18 branch).");
    }

    [Fact]
    public void EveryDeclaredPairClearsAaUnderEveryTheme()
    {
        var pairs = ScanPairs(out _);

        // A filtered run of the loop below reports pass on a dead scan — the floor that catches a
        // broken scan otherwise lives only in the sibling TheScanSeesTheAppItClaimsTo. Restated here
        // so this test cannot report green on nothing by itself.
        Assert.True(pairs.Count >= MinimumPairs,
            $"Found only {pairs.Count} distinct token pairs; expected at least {MinimumPairs} (8 measured on the v1.18 branch).");

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

    /// <summary>
    /// F-086's widening. The named pairs, measured every run and recorded per theme.
    /// <para>
    /// WHAT THIS FOUND ON ITS FIRST RUN, 2026-08-11. <c>MutedText</c> on <c>RowBg</c> measured
    /// <b>4.19:1 in midnight</b> — under AA, shipped, and invisible to every instrument in the
    /// suite because no element declares that pair inline. It had presumably been there since
    /// midnight was authored. The ruling was to FIX it rather than exempt it: midnight's
    /// <c>MutedText</c> moved from <c>#6F7E92</c> to <c>#768598</c>, a 6% blend toward that theme's
    /// own <c>White</c>, which is the smallest step that clears the floor with headroom.
    /// </para>
    /// <para>
    /// WHY FIXED AND NOT EXEMPTED, when F-050 next door is exempted. An exemption is right when the
    /// fix is unavailable or costs more than the debt — F-050's best theme-derived foreground still
    /// only reaches 4.40:1 under brand, so exempting it records a real constraint. Nothing
    /// constrained this one: one slot in one built-in theme moved a few percent and every pair it
    /// touches improved. Exempting a defect you can simply fix is how an exemption list becomes a
    /// place to put things.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryNamedPairClearsAaUnderEveryTheme()
    {
        Assert.True(NamedPairs.Length >= 3,
            $"Only {NamedPairs.Length} named pairs. This list is the answer to a gate that lost "
            + "sight of a token entirely; emptying it re-opens F-086.");

        var failures = new List<string>();

        foreach (var theme in BuiltInThemes())
        {
            var slots = ResolveTheme(theme);

            foreach (var (fill, text, why) in NamedPairs)
            {
                Assert.True(slots.ContainsKey(fill), $"Theme '{theme.Id}' resolved no brush for {fill}.");
                Assert.True(slots.ContainsKey(text), $"Theme '{theme.Id}' resolved no brush for {text}.");

                var ratio = ContrastGuard.RatioBetween(slots[fill], slots[text]);
                Assert.True(ratio.HasValue,
                    $"Theme '{theme.Id}': could not compute a ratio for {text} on {fill} "
                    + $"({slots[fill]} / {slots[text]}).");

                // Recorded on every run, pass or fail. The whole complaint behind F-086 is that
                // these numbers lived in prose in a spec and were re-derived by nobody.
                _output.WriteLine($"{theme.Id,-14} {text} on {fill,-18} {ratio!.Value,6:F2}:1   ({why})");

                var exemption = Array.Find(Exemptions, e => e.Fill == fill && e.Text == text);
                if (exemption.Fill is not null)
                {
                    if (ratio.Value < exemption.MinimumRatio)
                    {
                        failures.Add($"{theme.Id}: EXEMPTED NAMED PAIR GOT WORSE — {text} on {fill} "
                            + $"({exemption.Finding}) = {ratio.Value:F2}:1, below its recorded floor "
                            + $"of {exemption.MinimumRatio:F2}:1.");
                    }

                    continue;
                }

                if (ratio.Value < AaThreshold)
                {
                    failures.Add($"{theme.Id}: {text} on {fill} = {ratio.Value:F2}:1 "
                        + $"(needs {AaThreshold}:1) — {why}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "A named pair is below WCAG AA. These are measured unconditionally precisely because "
            + "no element declares them inline, so nothing else in this suite is watching them. "
            + "Move the pair, not the floor — and prefer fixing to exempting, because an exemption "
            + "here would be forgiving a defect nobody can see:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// The reason the list above has to exist, pinned so it cannot quietly stop being true.
    /// <para>
    /// Fails in BOTH directions on purpose, the same discipline
    /// <c>RenderedStyleGateTests.TheDerivedEdgeIsTunedToNavyAndFallsShortOnACard</c> uses. If the
    /// prose token becomes the foreground of a scanned pair again, this goes red — not because
    /// that is bad, but because the named list would then be measuring something the scan already
    /// covers, and whoever made that true should decide whether it stays.
    /// </para>
    /// </summary>
    [Fact]
    public void TheNamedPairsExistBecauseTheScanCannotSeeThem()
    {
        var scanned = ScanPairs(out _);
        var scannedProse = scanned.Where(p => p.Text == ThemeSlots.MutedText).ToList();

        Assert.True(scannedProse.Count == 0,
            $"The element scan now reports {scannedProse.Count} pair(s) with {ThemeSlots.MutedText} "
            + "as the foreground: " + string.Join(", ", scannedProse.Select(p => $"{p.Text} on {p.Fill}"))
            + ". F-086 exists because that count was zero while the token had ~113 bindings, which "
            + "is why NamedPairs measures it unconditionally. If a declared pair is back, decide "
            + "deliberately whether the named entry is still earning its place rather than leaving "
            + "two mechanisms measuring one thing.");
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
