using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using ROROROblox.Core.Theming;
using Xunit.Abstractions;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Every keyed control style, rendered under every built-in theme, measured in pixels.
/// <para>
/// This is Phase 2 of the rendered-contrast-gate design (2026-08-09).
/// <see cref="ContrastPairGateTests"/> asserts token arithmetic over elements that declare both a
/// fill and a foreground inline; it cannot see a style's setters at all, and it cannot see a
/// <c>BorderBrush</c> anywhere. This renders the style, samples the bitmap, and computes ratios with
/// <c>ContrastGuard.RatioBetween</c> — the app's own function, never a reimplementation. If the gate
/// and the app ever disagree about what 3:1 means, that disagreement is itself worth failing on.
/// </para>
/// <para>
/// WHAT THE FIRST RUN FOUND, recorded because it is the whole reason the phase was built: nothing.
/// All 28 measured cases sample the exact resolved theme slot the setter names, to the byte. No
/// template overrode a setter, no <c>DynamicResource</c> fell back silently, nothing composited. The
/// design's thesis was that Phase 2 "is the only one that can contradict the token math"; it did not
/// contradict it once, and that is a finding rather than a disappointment — it means the arithmetic
/// every glow finding argues from is now backed by a pixel instead of only by inference.
/// </para>
/// <para>
/// What it did find is a pair the arithmetic never computed. See
/// <see cref="TheDerivedEdgeIsTunedToNavyAndFallsShortOnACard"/>.
/// </para>
/// <para>
/// It also found a hole in its own design, and only because the design insisted on being proven by
/// failure. Sampling the label as "the pixel furthest from the fill" — the design's own words — is
/// wrong on any control that also draws a border: kill the label and the BORDER becomes the furthest
/// thing, so the rule reads a colour nobody was trying to read and passes. That is what the first
/// deliberate-failure run measured. <see cref="Case"/> records the fix. A gate that had only ever
/// passed would have shipped with that hole intact, which is the argument for acceptance item 6 in
/// one sentence.
/// </para>
/// <para>
/// SCOPE, stated so nobody reads a green run as "contrast is verified". <c>ControlStyles.xaml</c>'s
/// own comment says it exists because 63 hand-copied attribute sets across 15 files repeat the same
/// themed attributes. Seven keyed styles have been extracted so far. Everything not yet migrated is
/// invisible here — including all 13 magenta CTAs, the surfaces F-050 is about. This gate covers the
/// styles, and the styles are a minority of the app's surfaces.
/// </para>
/// <para>
/// NO EXEMPTIONS, and the reason is verifiable rather than asserted: the magenta CTAs that would
/// have needed one are inline-styled. Re-counted against HEAD on 2026-08-10 — 13 elements set
/// <c>Background="{DynamicResource MagentaBrush}"</c> directly (GamesWindow :242, MainWindow :57,
/// :174, :424, :996, :1431, :1473, PluginsWindow :108, :218, :258, PreferencesWindow :349, :423,
/// SquadLaunchWindow :59) and not one of them carries a <c>Style</c> attribute. <c>MagentaBrush</c>
/// does not appear in <c>ControlStyles.xaml</c> at all. They are Phase 1's to guard, and Phase 1
/// guards them.
/// </para>
/// <para>
/// NOT COVERED: composition beyond the two surfaces the input styles name, runtime brushes set by a
/// converter or a view-model, layout of any kind, and whole windows — no <see cref="Application"/>
/// is constructed and no window is, both by design (see <see cref="ThemedRender"/>). The manual
/// smoke is still the only check on whole-page rendering; this narrows what it has to catch.
/// </para>
/// </summary>
public class RenderedStyleGateTests(ITestOutputHelper output)
{
    /// <summary>WCAG AA for body text. The app's body size is 11px, so the large-text allowance does not apply.</summary>
    private const double AaThreshold = 4.5;

    /// <summary>
    /// WCAG 1.4.11 for a non-text interactive boundary, read from the app's own constant rather than
    /// written as 3.0 here. The gate and the thing it guards have to agree on the number.
    /// </summary>
    private const double EdgeThreshold = ContrastGuard.MinimumBoundaryRatio;

    /// <summary>
    /// Styles that own their fill: the setter names a background, so foreground-vs-fill and
    /// edge-vs-fill are both relationships the style itself declares.
    /// </summary>
    private static readonly string[] FullyMeasured =
        ["PrimaryButtonStyle", "SecondaryButtonStyle", "SecondaryStrongButtonStyle"];

    /// <summary>
    /// Styles carrying no <c>Background</c> setter, deliberately — see the comment above
    /// <c>AppTextBoxStyle</c>. Two fills are legitimate and the call sites supply them: re-counted
    /// against HEAD on 2026-08-10, all 14 sites using these two styles set <c>Background</c>
    /// explicitly — 8 to <c>RowBgBrush</c> (Games x2, JoinByLink, Rename, Export x2, Import x2) and
    /// 6 to <c>NavyBrush</c> (Plugins, Preferences x2, SquadLaunch, CaptionColorPicker,
    /// ThemeBuilder). Not one inherits a fill ambiently. So "surface-supplied" is a property of the
    /// markup, not a hope about ancestors, and rendering these on both surfaces reproduces what
    /// ships.
    /// </summary>
    private static readonly string[] SurfaceSupplied = ["AppTextBoxStyle", "AppPasswordBoxStyle"];

    /// <summary>
    /// A CLOSED list, so a style added later cannot land here by accident — it lands in
    /// <see cref="EveryKeyedStyleIsClassified"/>'s failure message instead. Neither exclusion is a
    /// judgement call taken on trust; <see cref="TheExcludedStylesHaveNoPairThisGateCouldMeasure"/>
    /// renders both and measures the absence.
    /// <para>
    /// <c>CardBorderStyle</c> is a <see cref="Border"/> drawing a card, and WCAG 1.4.11 does not
    /// govern a separator. That is the same role split <c>InteractiveEdgeBindingTests</c> already
    /// enforces — it fails the build if the derived edge is ever bound to a Style targeting a
    /// decorative type — and this gate must not contradict it. <c>SectionHeadingStyle</c> is a
    /// <see cref="TextBlock"/>: prose, no fill of its own, no interactive boundary.
    /// </para>
    /// </summary>
    private static readonly string[] Excluded = ["CardBorderStyle", "SectionHeadingStyle"];

    /// <summary>The two fills the input styles' own comment names, in the order it names them.</summary>
    private static readonly string[] LegitimateSurfaces = [ThemeSlots.RowBg, ThemeSlots.Navy];

    /// <summary>
    /// Share of the bitmap a colour has to hold before this gate will call it the control's text.
    /// <para>
    /// Anything under it is an antialiasing artifact, and one particular artifact matters: WPF-UI's
    /// buttons have rounded corners, so a few dozen pixels per render are the sentinel host blended
    /// with the fill. Those blends are the most contrasting thing in the frame once a label goes
    /// missing, so without a floor the gate reports a colour no reader ever sees — measured
    /// 2026-08-10 during the deliberate-failure proof, which named <c>#600060</c> at 89px on a
    /// 19,200px button.
    /// </para>
    /// <para>
    /// 1% is set against 8x of measured headroom, not guessed: the thinnest legitimate glyph sample
    /// in the matrix is the PasswordBox's dots at 1,220 of 14,512 pixels, 8.4%. The design already
    /// reasons this way — <c>ThemedRender.GlyphSize</c> is deliberately large so glyph cores stay
    /// solid — this is that argument made numeric.
    /// </para>
    /// </summary>
    private const double GlyphCoverageFloor = 0.01;

    /// <summary>
    /// One case, rendered TWICE, and the second render is not redundant.
    /// <para>
    /// <c>Sample.Foreground</c> is the pixel furthest from the fill by contrast. That is the right
    /// definition of "the most legible thing this control drew" and the wrong one for "the text",
    /// and on a control that also draws a border the difference is load-bearing: when the label
    /// collapses onto the fill, the glyphs stop producing pixels and the BORDER becomes the furthest
    /// thing, so rule 1 silently measures the edge and reports a comfortable pass.
    /// </para>
    /// <para>
    /// That is not hypothetical. Design acceptance item 6 says to prove the gate by forcing a
    /// style's foreground to its own fill; done against <c>PrimaryButtonStyle</c> on 2026-08-10, the
    /// single-render gate stayed GREEN and reported 9.39:1, which is cyan-on-navy — the border,
    /// standing in for a label that was no longer there. The gate could not fail the case it was
    /// built to catch. Item 6 exists for exactly this, and it earned its keep on first use.
    /// </para>
    /// <para>
    /// So <see cref="Borderless"/> renders the same style with <c>BorderThickness</c> zeroed and
    /// nothing else touched. Fill and foreground are untouched by that, and with the edge gone the
    /// glyphs are the only non-fill content in the frame — so a label that matches its fill measures
    /// 1.00:1 instead of borrowing the border's ratio. <see cref="Authored"/> keeps the edge, because
    /// rule 2 needs the border the style actually declares.
    /// </para>
    /// </summary>
    private sealed record Case(
        string StyleKey,
        string ThemeId,
        string? Surface,
        string SurfaceHex,
        Sample Authored,
        Sample Borderless)
    {
        public string Label => Surface is null
            ? $"{StyleKey} under '{ThemeId}'"
            : $"{StyleKey} on {Surface} ({SurfaceHex}) under '{ThemeId}'";

        /// <summary>
        /// The colour of the control's label: the most contrasting thing in the borderless render
        /// that covers at least <see cref="GlyphCoverageFloor"/> of the bitmap.
        /// <para>
        /// Deliberately NOT <c>Sample.Foreground</c>. That property answers "the most contrasting
        /// pixel", which is the right question for the harness and the wrong one for rule 1 — it
        /// cannot tell a glyph from a corner artifact, and a label that has vanished into its own
        /// fill leaves nothing but artifacts. The ratio still comes from
        /// <c>ContrastGuard.RatioBetween</c>; only the choice of which pixel to hand it is made here.
        /// </para>
        /// <para>
        /// When a label really has collapsed onto its fill, every colour above the floor IS the fill,
        /// so this returns the fill and the ratio is 1.00:1 — the true measurement, stated plainly,
        /// rather than a number borrowed from something else in the frame.
        /// </para>
        /// </summary>
        public string Text
        {
            get
            {
                var floor = Borderless.Histogram.Sum(h => h.Count) * GlyphCoverageFloor;
                return Borderless.Histogram
                    .Where(h => h.Count >= floor)
                    .Select(h => h.Colour)
                    .OrderByDescending(c => ContrastGuard.RatioBetween(Borderless.Fill, c) ?? 0)
                    .First();
            }
        }

        public double? TextRatio => ContrastGuard.RatioBetween(Borderless.Fill, Text);
    }

    /// <summary>
    /// Rendered ONCE for the whole class. Every other gate in this repo re-scans per fact on purpose,
    /// because a regex sweep is free and a shared helper couples two gates' failure modes. A render
    /// is not free — 64 STA threads, bitmaps and dispatcher drains — and the design named flake as
    /// the real risk of this phase, so rolling the dice four times over the same matrix buys nothing
    /// and costs three extra chances to be flaky. Each fact below still asserts its own floor, so
    /// none of them can report green on an empty matrix.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<Case>> Matrix = new(RenderMatrix);

    private static readonly Lazy<IReadOnlyList<Case>> ExcludedMatrix = new(RenderExcluded);

    /// <summary>
    /// The app's real built-in themes, from the real <see cref="ThemeStore"/>, pointed at a throwaway
    /// folder so a dev box's user themes in <c>%LOCALAPPDATA%</c> cannot contaminate the result. Not
    /// a hand-written list: the design was authored when three themes shipped, v1.17 added
    /// <c>flatline</c>, and iterating the store is why that theme enrolled itself with nothing to
    /// wire. Same construction <c>ContrastPairGateTests.BuiltInThemes</c> uses.
    /// </summary>
    private static IReadOnlyList<Theme> BuiltInThemes()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-rendered-gate-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn)
            .ToList();

        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
        catch (IOException) { }

        Assert.True(themes.Count >= 4,
            $"Expected at least the 4 built-in themes (brand, midnight, magenta-heat, flatline); got {themes.Count}.");

        return themes;
    }

    private static IReadOnlyList<Case> RenderMatrix()
    {
        var cases = new List<Case>();

        foreach (var theme in BuiltInThemes())
        {
            foreach (var key in FullyMeasured)
            {
                var authored = ThemedRender.Measure(theme, $"{key}/{theme.Id}",
                    d => ThemedRender.Styled(d, key));
                var borderless = ThemedRender.Measure(theme, $"{key}/borderless/{theme.Id}",
                    d => Unbordered(ThemedRender.Styled(d, key)));

                cases.Add(new Case(key, theme.Id, Surface: null, authored.Fill, authored, borderless));
            }

            foreach (var key in SurfaceSupplied)
            {
                foreach (var surface in LegitimateSurfaces)
                {
                    var hex = ThemedRender.Slot(ThemedRender.Resources(theme), surface);

                    var authored = ThemedRender.Measure(theme, $"{key}/{surface}/{theme.Id}",
                        d => Composed(d, key, surface, borderless: false));
                    var borderless = ThemedRender.Measure(theme, $"{key}/{surface}/borderless/{theme.Id}",
                        d => Composed(d, key, surface, borderless: true));

                    cases.Add(new Case(key, theme.Id, surface, hex, authored, borderless));
                }
            }
        }

        return cases;
    }

    /// <summary>
    /// The same control with its border removed and nothing else changed — see <see cref="Case"/>
    /// for why that second render exists. <c>BorderThickness</c> is a layout property; zeroing it
    /// touches neither <c>Foreground</c> nor <c>Background</c>, which is what makes the fill measured
    /// here identical to the authored one and lets <see cref="TheMatrixRenderedWhatItClaimsTo"/>
    /// assert that equality rather than assume it.
    /// </summary>
    private static FrameworkElement Unbordered(FrameworkElement element)
    {
        if (element is Control control) control.BorderThickness = new Thickness(0);
        return element;
    }

    /// <summary>
    /// A surface-supplied control placed the way the app places it: its own <c>Background</c> set to
    /// the surface token, inside a container painted the same token.
    /// <para>
    /// BOTH HALVES ARE LOAD-BEARING, and the second one is the part that would be easy to drop. The
    /// wrapper alone does NOT put the surface behind the glyphs: measured 2026-08-10, a
    /// <c>TextBox</c> carrying <c>AppTextBoxStyle</c> and no <c>Background</c> renders an opaque fill
    /// of its own that no theme produced — WPF's <c>TextBoxBase</c> default template, and the style's
    /// <c>BasedOn</c> does not reach WPF-UI's. Sampling that composition reports the style's white
    /// foreground against a fill the app never shows, which is precisely the confident-wrong-colour
    /// failure a pixel gate exists to rule out rather than introduce. Setting the control's own
    /// <c>Background</c> is what all 14 call sites do, so it is also simply what ships. The wrapper
    /// stays because it is what makes the composition honest: nothing can reach the sentinel through
    /// a rounded corner, and the surface is genuinely present rather than merely assigned.
    /// <see cref="TheSurfaceHasToBeOnTheControlNotOnlyBehindIt"/> keeps that from being re-simplified.
    /// </para>
    /// </summary>
    private static FrameworkElement Composed(ResourceDictionary dict, string styleKey, string surfaceKey, bool borderless)
    {
        var brush = (Brush)dict[surfaceKey];
        var control = (Control)ThemedRender.Styled(dict, styleKey);
        control.Background = brush;
        if (borderless) Unbordered(control);
        return new Border { Background = brush, Child = control };
    }

    private static IReadOnlyList<Case> RenderExcluded()
    {
        var cases = new List<Case>();
        foreach (var theme in BuiltInThemes())
        {
            foreach (var key in Excluded)
            {
                // One render, not two: neither excluded style declares a border, so there is no edge
                // for a second pass to remove and nothing for it to disambiguate.
                var sample = ThemedRender.Measure(theme, $"{key}/{theme.Id}", d => ThemedRender.Styled(d, key));
                cases.Add(new Case(key, theme.Id, Surface: null, sample.Fill, sample, sample));
            }
        }

        return cases;
    }

    /// <summary>
    /// Design acceptance item 5, and the most important structural piece in the file. Styles are
    /// enumerated FROM THE MARKUP, so a style added to <c>ControlStyles.xaml</c> and classified
    /// nowhere fails here by name rather than being silently unmeasured.
    /// </summary>
    [Fact]
    public void EveryKeyedStyleIsClassified()
    {
        var keys = KeyedStyles();

        // Floor first: a path bug returning nothing would make every set comparison below vacuously
        // agreeable. Seven keyed styles exist today; the file's own comment explains why the ComboBox
        // style is the one implicit style and therefore carries no key.
        Assert.True(keys.Count >= 7,
            $"Found {keys.Count} keyed styles in ControlStyles.xaml; expected at least 7 (7 measured "
            + "2026-08-10). The scan is broken, not the file.");

        var classified = FullyMeasured.Concat(SurfaceSupplied).Concat(Excluded).ToList();

        Assert.True(classified.Count == classified.Distinct().Count(),
            "A style key appears in more than one classification list. The three lists partition the "
            + "file; overlap means one of them is measuring a style the other exempted.");

        var unclassified = keys.Except(classified).ToList();
        Assert.True(unclassified.Count == 0,
            "ControlStyles.xaml holds keyed styles this gate classifies nowhere, so they render in "
            + "the app and are measured by nothing:\n  " + string.Join("\n  ", unclassified)
            + "\nAdd each to FullyMeasured, SurfaceSupplied, or Excluded. Excluding one needs the "
            + "same argument CardBorderStyle and SectionHeadingStyle carry — a role WCAG's text or "
            + "boundary rules do not govern — not the convenience of a green run.");

        var vanished = classified.Except(keys).ToList();
        Assert.True(vanished.Count == 0,
            "This gate classifies styles that no longer exist in ControlStyles.xaml, so it is "
            + "measuring less than its lists claim:\n  " + string.Join("\n  ", vanished));
    }

    /// <summary>
    /// Every keyed style's <c>x:Key</c>, read from the shipped markup. Deliberately the file itself
    /// rather than the parsed <see cref="ResourceDictionary"/>: the dictionary also carries WPF-UI's
    /// entire vocabulary once merged, and the claim being made is about what this repo authored.
    /// </summary>
    private static IReadOnlyList<string> KeyedStyles()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.False(appDir is null, "Repo-root walk failed, so this test scanned nothing.");

        var path = Path.Combine(appDir!, "Controls", "ControlStyles.xaml");
        Assert.True(File.Exists(path), $"ControlStyles.xaml not found at {path}.");

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Descendants()
            .Where(e => e.Name.LocalName == "Style")
            .Select(e => (string?)e.Attribute(x + "Key"))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!)
            .ToList();
    }

    /// <summary>
    /// The matrix is what it says it is before anything is concluded from it: every case rendered,
    /// nothing sampled the host, and every ratio computed.
    /// </summary>
    [Fact]
    public void TheMatrixRenderedWhatItClaimsTo()
    {
        var cases = Matrix.Value;

        // 5 styles x 4 themes, with the two surface-supplied styles measured on both of their
        // legitimate fills: (3 x 4) + (2 x 2 x 4) = 28. A floor rather than an equality so a fifth
        // built-in theme enrols itself without editing a number here.
        Assert.True(cases.Count >= 28,
            $"Rendered {cases.Count} cases; expected at least 28 (5 styles x 4 built-in themes, the "
            + "two inputs on 2 surfaces each). A short matrix passes every assertion below while "
            + "measuring less than it claims.");

        foreach (var c in cases)
        {
            output.WriteLine($"{c.Label,-64} fill={c.Authored.Fill} text={c.Text} "
                + $"edge={c.Authored.Edge ?? "none"} "
                + $"text-ratio={c.TextRatio?.ToString("F4") ?? "null"} "
                + $"edge-ratio={c.Authored.EdgeRatio?.ToString("F4") ?? "null"}");

            // A leaked sentinel means the magenta host showed through in bulk, so the sample describes
            // the harness rather than the style. Stage 1 shipped a sample that was right by
            // coincidence; this is the assertion that stops the coincidence being repeated silently.
            foreach (var (sample, which) in new[] { (c.Authored, "authored"), (c.Borderless, "borderless") })
            {
                Assert.False(sample.SentinelLeaked,
                    $"{c.Label} ({which}): the sentinel host is among the three most common colours, so "
                    + "this control did not cover its own bounds and the sample is measuring the "
                    + "harness. Histogram:\n" + sample.Describe());
            }

            // Removing the border must not move the fill. If it does, the template is deriving the
            // control's surface from its border in some way this gate does not model, and the two
            // renders are no longer measuring the same control.
            Assert.True(c.Authored.Fill == c.Borderless.Fill,
                $"{c.Label}: fill sampled {c.Authored.Fill} with the border and {c.Borderless.Fill} "
                + "without it. BorderThickness is a layout property and must not repaint the surface; "
                + "the two renders are no longer comparable.");

            // Null means a sampled pixel would not parse through ContrastGuard.TryParse. Asserted
            // rather than coerced to zero: a zero would look like a catastrophic contrast failure and
            // send the reader after the theme instead of after the formatter.
            Assert.True(c.TextRatio.HasValue,
                $"{c.Label}: no ratio computable for {c.Text} on {c.Borderless.Fill}.");
        }

        // Every style named in a classification list has to appear, or a rename would quietly shrink
        // the matrix while the count floor above still passed on the remaining themes.
        foreach (var key in FullyMeasured.Concat(SurfaceSupplied))
        {
            Assert.Contains(cases, c => c.StyleKey == key);
        }
    }

    /// <summary>
    /// Rule 1. Foreground vs the fill it actually landed on, every style, every theme, and for the
    /// input styles every surface their own comment calls legitimate.
    /// </summary>
    [Fact]
    public void EveryRenderedForegroundClearsAaAgainstTheFillItLandedOn()
    {
        var cases = Matrix.Value;
        Assert.True(cases.Count >= 28, $"Rendered {cases.Count} cases; expected at least 28.");

        var failures = new List<string>();

        foreach (var c in cases)
        {
            // Restated here rather than left to the sibling fact: a host bleeding into the frame in
            // bulk could otherwise clear the coverage floor and be mistaken for the label.
            Assert.False(c.Borderless.SentinelLeaked,
                $"{c.Label}: the sentinel host is among the three most common colours in the "
                + "borderless render, so the label sample may be measuring the harness. Histogram:\n"
                + c.Borderless.Describe());

            var ratio = c.TextRatio;
            Assert.True(ratio.HasValue,
                $"{c.Label}: no ratio computable for {c.Text} on {c.Borderless.Fill}.");

            if (ratio!.Value < AaThreshold)
            {
                failures.Add($"{c.Label}: {c.Text} on {c.Borderless.Fill} "
                    + $"= {ratio.Value:F2}:1 (needs {AaThreshold}:1)");
            }
        }

        Assert.True(failures.Count == 0,
            "Rendered foregrounds below WCAG AA. These are pixels, not token arithmetic — the fill "
            + "named here is what the bitmap contained, so a template override or a failed resource "
            + "lookup shows up here and nowhere else:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// Rule 2, applied where the style's edge and the style's fill are the same relationship
    /// <c>ContrastGuard</c> derived: the three button styles, whose setters name their own fill, and
    /// the input styles on <c>NavyBrush</c>, the surface the derived edge is computed against.
    /// <para>
    /// The remaining eight cases — the inputs on <c>RowBgBrush</c> — are not silently dropped. They
    /// are measured and asserted in <see cref="TheDerivedEdgeIsTunedToNavyAndFallsShortOnACard"/>,
    /// which fails if that gap closes as well as if it widens.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRenderedEdgeClearsNonTextContrastAgainstTheSurfaceItIsDerivedFor()
    {
        var cases = Matrix.Value.Where(c => c.Surface is null or ThemeSlots.Navy).ToList();

        // (3 styles x 4 themes) + (2 inputs x 4 themes on Navy) = 20.
        Assert.True(cases.Count >= 20,
            $"Have {cases.Count} own-fill cases; expected at least 20. A filtered matrix reports no "
            + "failures at all, which reads identically to a passing gate.");

        var failures = new List<string>();
        var edgeless = new List<string>();

        foreach (var c in cases)
        {
            if (c.Authored.Edge is null)
            {
                edgeless.Add(c.Label);
                continue;
            }

            var ratio = c.Authored.EdgeRatio;
            Assert.True(ratio.HasValue,
                $"{c.Label}: no ratio computable for edge {c.Authored.Edge} on {c.Authored.Fill}.");

            if (ratio!.Value < EdgeThreshold)
            {
                failures.Add($"{c.Label}: edge {c.Authored.Edge} on {c.Authored.Fill} = {ratio.Value:F2}:1 "
                    + $"(needs {EdgeThreshold}:1)");
            }
        }

        // Every style in this set sets BorderThickness=1 and a BorderBrush. A case with no edge means
        // the border did not render, which is a template swallowing a setter — the exact class of
        // defect this phase exists to catch, and it would otherwise pass as "no edge, no rule".
        Assert.True(edgeless.Count == 0,
            "A style that declares BorderThickness and a BorderBrush rendered no edge at all:\n  "
            + string.Join("\n  ", edgeless));

        Assert.True(failures.Count == 0,
            $"Rendered edges below WCAG 1.4.11's {EdgeThreshold}:1 for a non-text interactive "
            + "boundary:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// A LIVE DEFECT, pinned rather than forgiven. Found by this phase on its first run, 2026-08-10.
    /// <para>
    /// <c>InteractiveEdgeBrush</c> is derived by <c>ContrastGuard.Ensure(theme.Navy, theme.Divider)</c>
    /// — against <c>Navy</c>, and it clears 3:1 against Navy by exactly as much as the walk needed
    /// (3.02-3.07 across the four built-ins). Every built-in also ships <c>RowBg</c> lighter than
    /// <c>Navy</c>, so the same edge on a card lands UNDER the floor: 2.82:1 in brand, midnight and
    /// magenta-heat, 2.28:1 in flatline, where RowBg is furthest from Navy. Eight of the fourteen
    /// call sites using these two styles sit on RowBg.
    /// </para>
    /// <para>
    /// Neither existing gate could see this. Phase 1 pairs a <c>Background</c> with a
    /// <c>Foreground</c> and never reads a <c>BorderBrush</c>. <c>InteractiveEdgeBindingTests</c>
    /// asserts the derived edge is bound to the right ROLE, not that the value clears its floor
    /// against the surface it actually lands on. F-031 is clean and records the derivation reaching
    /// 3.03:1 — true, and true only of Navy.
    /// </para>
    /// <para>
    /// WHY THIS IS NOT AN EXEMPTION. An exemption grants permission and lets the gate stay green
    /// while the number drifts. This asserts the number in both directions: it fails if the ratio
    /// improves, because then the fix landed and these eight cases belong in
    /// <see cref="EveryRenderedEdgeClearsNonTextContrastAgainstTheSurfaceItIsDerivedFor"/> and this
    /// test should be deleted; and it fails if the ratio worsens, because a recorded defect getting
    /// deeper is news. A theme shipped later with no entry here fails too — measure it and record it
    /// with its direction, the same discipline the register's own rows are held to.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDerivedEdgeIsTunedToNavyAndFallsShortOnACard()
    {
        // Measured 2026-08-10 by this file, in pixels, through ContrastGuard.RatioBetween. Not
        // tunable: these are the output of the shipped derivation over the shipped theme hexes.
        var recorded = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["brand"] = 2.82,
            ["midnight"] = 2.81,
            ["magenta-heat"] = 2.81,
            ["flatline"] = 2.28,
        };

        var cases = Matrix.Value.Where(c => c.Surface == ThemeSlots.RowBg).ToList();
        Assert.True(cases.Count >= 8,
            $"Have {cases.Count} card-surface cases; expected at least 8 (2 input styles x 4 themes).");

        foreach (var c in cases)
        {
            var ratio = c.Authored.EdgeRatio;
            Assert.True(ratio.HasValue,
                $"{c.Label}: no ratio computable for edge {c.Authored.Edge} on {c.Authored.Fill}.");

            Assert.True(ratio!.Value < EdgeThreshold,
                $"{c.Label}: the derived edge now measures {ratio.Value:F2}:1, at or above "
                + $"{EdgeThreshold}:1. If the derivation learned about the surface a control actually "
                + "sits on, this defect is fixed — delete this test and let "
                + "EveryRenderedEdgeClearsNonTextContrastAgainstTheSurfaceItIsDerivedFor cover these "
                + "cases, which it will do automatically once its filter stops excluding them.");

            Assert.True(recorded.ContainsKey(c.ThemeId),
                $"{c.Label}: no ratio recorded for theme '{c.ThemeId}'. A theme that ships after this "
                + "was written enrols itself in the matrix automatically and has to be measured here "
                + $"deliberately — it renders at {ratio.Value:F2}:1 today.");

            Assert.True(Math.Round(ratio.Value, 2) == recorded[c.ThemeId],
                $"{c.Label}: measured {ratio.Value:F4}:1, recorded {recorded[c.ThemeId]:F2}:1 "
                + $"(edge {c.Authored.Edge} on {c.Authored.Fill}). A recorded defect moved. Re-measure "
                + "and record the new number with its direction rather than widening the tolerance.");
        }
    }

    /// <summary>
    /// The exclusions, measured rather than asserted by fiat. Each excluded style renders, and what
    /// the bitmap shows is the mechanical form of the reason it is excluded.
    /// </summary>
    [Fact]
    public void TheExcludedStylesHaveNoPairThisGateCouldMeasure()
    {
        var cases = ExcludedMatrix.Value;
        Assert.True(cases.Count >= 8,
            $"Rendered {cases.Count} excluded cases; expected at least 8 (2 styles x 4 themes).");

        foreach (var c in cases)
        {
            output.WriteLine($"EXCLUDED {c.Label,-52} fill={c.Authored.Fill} edge={c.Authored.Edge ?? "none"} "
                + $"host-visible={c.Authored.SentinelLeaked}");

            switch (c.StyleKey)
            {
                case "CardBorderStyle":
                    // Decorative: it paints a fill and draws no boundary, so rule 2 has no subject.
                    // The outermost thing on the midline is the card's own fill. If that ever stops
                    // being true, a Border style started drawing a control edge and
                    // InteractiveEdgeBindingTests has an opinion about it first.
                    Assert.True(c.Authored.Edge == c.Authored.Fill,
                        $"{c.Label}: outermost colour {c.Authored.Edge} differs from the fill "
                        + $"{c.Authored.Fill}, so this Border now draws a boundary. It is excluded on "
                        + "the grounds that it does not — re-classify it, and check what "
                        + "InteractiveEdgeBindingTests says about the brush it drew with.");
                    break;

                case "SectionHeadingStyle":
                    // Prose: no fill of its own, so rule 1 has no surface. The host showing through in
                    // bulk IS that statement, measured. A heading that started painting a background
                    // would stop leaking and would need measuring like any other filled surface.
                    Assert.True(c.Authored.SentinelLeaked,
                        $"{c.Label}: the host no longer shows through, so this TextBlock now paints a "
                        + "fill of its own. It is excluded on the grounds that it has none — "
                        + "re-classify it into FullyMeasured.");
                    break;

                default:
                    Assert.Fail($"'{c.StyleKey}' is excluded but this test states no measured reason "
                        + "for it. An exclusion nobody can check is the shape a gate rots into.");
                    break;
            }
        }
    }

    /// <summary>
    /// Keeps <see cref="Composed"/> from being simplified into the thing that looks equivalent.
    /// <para>
    /// Placing a surface-supplied control on a themed surface without also setting its own
    /// <c>Background</c> does not put that surface behind its glyphs: the control paints an opaque
    /// fill of its own that no theme produced, and the sample then reports a real-looking ratio
    /// against a colour the app never shows. Asserted as a relationship — the measured fill is not
    /// the surface — never as a colour, because a user's theme supplies the surface and the glow
    /// invariant is that a finding prescribing a colour is invalid.
    /// </para>
    /// <para>
    /// If WPF-UI's own input styles ever reach these controls through <c>BasedOn</c> and the fill
    /// becomes transparent, this test goes red and the right answer is to simplify
    /// <see cref="Composed"/> to the wrapper alone. That is the failure meaning "this is now
    /// unnecessary", which is the only kind of red worth welcoming.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSurfaceHasToBeOnTheControlNotOnlyBehindIt()
    {
        var theme = BuiltInThemes()[0];

        foreach (var key in SurfaceSupplied)
        {
            var expected = ThemedRender.Slot(ThemedRender.Resources(theme), ThemeSlots.RowBg);

            var behindOnly = ThemedRender.Measure(theme, $"{key}/behind-only/{theme.Id}", d => new Border
            {
                Background = (Brush)d[ThemeSlots.RowBg],
                Child = ThemedRender.Styled(d, key),
            });

            Assert.True(behindOnly.Fill != expected,
                $"{key} under '{theme.Id}': a surface placed only BEHIND the control now reaches the "
                + $"pixel — measured fill {behindOnly.Fill} equals {ThemeSlots.RowBg}. The control has "
                + "stopped painting a fill of its own, so Composed no longer needs to set Background "
                + "and should be simplified to the wrapper alone.");
        }
    }
}
