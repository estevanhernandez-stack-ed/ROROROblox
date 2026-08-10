using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml;
using System.Xml.Linq;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Theming;
using Xunit.Abstractions;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The colours a <c>DataTrigger</c> sets at runtime, rendered from the SHIPPED markup and measured
/// in pixels.
/// <para>
/// This closes the hole the rendered-contrast-gate design (2026-08-09) names in its own "What this
/// does not cover": <em>"Runtime brushes. Anything produced by a converter, set from a view-model,
/// or applied by a trigger at runtime is invisible here."</em> It was invisible to all three
/// existing checks at once. <c>ContrastPairGateTests</c> matches elements declaring both
/// <c>Background</c> and <c>Foreground</c> as inline attributes, and a trigger's <c>Setter</c> is
/// neither. <see cref="RenderedStyleGateTests"/> renders only the keyed styles in
/// <c>ControlStyles.xaml</c>, and the status dot's <c>Style</c> is inline in
/// <c>MainWindow.xaml</c>. <c>ThemedStatusColourTests</c> is a FENCE — it proves no colour literal
/// survives in App code, which is a statement about where the colour came from, never about what
/// it looks like. Nothing measured what a status dot actually renders. Now something does.
/// </para>
/// <para>
/// THE CONSTRAINT THAT SHAPES THE WHOLE FILE: it measures the shipped markup, not a reconstruction
/// of it. Hand-writing an equivalent <c>Ellipse</c> and <c>DataTrigger</c> here would be trivial,
/// would pass forever, and would keep passing while <c>MainWindow.xaml</c> rotted underneath it —
/// a gate that guards a copy of the thing rather than the thing. So <see cref="Locate"/> parses
/// <c>MainWindow.xaml</c> with <see cref="XDocument"/>, lifts the real element subtree out, and
/// <see cref="XamlReader.Parse(string)"/> turns it back into a live object. The proof that this is
/// not decoration is in the commit that added it: breaking the <c>green</c> trigger in
/// <c>MainWindow.xaml</c> turns this file red, and renaming the property it searches for makes the
/// extraction fail by name rather than measure nothing.
/// </para>
/// <para>
/// EXPECTATIONS ARE WRITTEN HERE, NOT READ FROM THE FILE. <see cref="DotMapping"/> and
/// <see cref="ChipMapping"/> restate spec §5.3 in this file deliberately. An expectation derived
/// from the markup under test cannot fail when the markup is wrong — repoint a trigger and both
/// sides move together, and the gate reports a comfortable pass. The markup's own declared slots
/// are still checked, in <see cref="TheStatusDotIsMappedTheWayTheSpecSaysItIs"/>, as a separate and
/// weaker structural claim.
/// </para>
/// <para>
/// WHY <c>Sample.SentinelLeaked</c> IS NOT ASSERTED HERE, unlike in
/// <see cref="RenderedStyleGateTests"/>. That check exists for a control that paints its own bounds;
/// neither subject here does. An 8px round dot leaves the host visible in every corner, and a
/// <c>TextBlock</c> has no fill at all, so the sentinel is legitimately the most common colour in
/// the frame. What replaces it is strictly stronger for this shape: the measured colour must EQUAL
/// the resolved theme slot to the byte, and must cover at least <see cref="CoverageFloor"/> of the
/// bitmap. A leak cannot survive both — the sentinel is a magenta no theme produces, so it can
/// never be mistaken for a slot.
/// </para>
/// <para>
/// TWO LAYOUT PROPERTIES ARE OVERRIDDEN ON THE EXTRACTED ELEMENT and nothing else: the dot's
/// <c>Width</c>/<c>Height</c> and the chip's <c>FontSize</c>. Same move, and the same argument, as
/// <c>RenderedStyleGateTests.Unbordered</c> — neither touches <c>Fill</c> or <c>Foreground</c>, and
/// this gate measures colour rather than layout, so size is free. At the shipped 8px an antialiased
/// dot has barely two dozen pure pixels and at 11px a glyph core is mostly blend, which would make
/// the sample an artifact-reading rather than a measurement.
/// </para>
/// <para>
/// NOT COVERED. The status bar's live-process dot (<c>MainWindow.xaml:1888</c>, the fifth site
/// F-088 opened) binds <c>LiveProcessCount</c> on <c>MainViewModel</c>, not on
/// <see cref="AccountSummary"/> — a different DataContext this file cannot stand up cheaply, and an
/// inverted arrangement where the loud state is the plain <c>Setter</c>. It is deliberately out of
/// scope here and remains guarded only by the fence. Also not covered: composition against a real
/// page, layout, and whether a state is REACHABLE — the view-model produces the string
/// (<see cref="RowInState"/> asserts that), the trigger fires on it, and what happens between the
/// app and that view-model is <c>AccountSummaryTests</c>' territory.
/// </para>
/// </summary>
public class TriggeredStatusColourGateTests(ITestOutputHelper output)
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// spec §5.3's status-to-slot table, restated here rather than read from the markup. See the
    /// class doc: an expectation lifted from the file under test moves with it and can never fail.
    /// <c>green</c> takes <c>WhiteBrush</c> and not the tempting <c>CyanBrush</c> because under
    /// flatline Cyan and RowExpiredAccent are the same value, which would land active and expired at
    /// 1.00:1 of each other — the collision
    /// <see cref="TheFourDotStatesStayMutuallyDistinctInEveryTheme"/> now guards in pixels.
    /// </summary>
    private static readonly (string State, string Slot)[] DotMapping =
    [
        ("green", ThemeSlots.White),
        ("yellow", ThemeSlots.RowExpiredAccent),
        ("magenta", ThemeSlots.Magenta),
        ("grey", ThemeSlots.MutedText),
    ];

    /// <summary>
    /// spec §5.3's chip rule: <c>RowExpiredAccentBrush</c> when warning, <c>MutedTextBrush</c>
    /// otherwise. Both warning chips share it so the two warn states read as one vocabulary.
    /// </summary>
    private static readonly (bool Warn, string Slot)[] ChipMapping =
    [
        (true, ThemeSlots.RowExpiredAccent),
        (false, ThemeSlots.MutedText),
    ];

    /// <summary>The two view-model flags that drive a warning chip's colour, spec §5.3.</summary>
    private static readonly string[] ChipBindings = ["IdleWarn", "MemoryWarning"];

    /// <summary>
    /// Rendered large so the sample is a measurement rather than a reading of antialiasing. See the
    /// class doc; <c>ThemedRender.GlyphSize</c> makes the same argument for the same reason.
    /// </summary>
    private const double DotSize = 96;
    private const double GlyphSize = 48;

    /// <summary>
    /// Share of the bitmap a colour has to hold before this gate will believe it is the thing the
    /// control drew. Set at 1% against measured headroom rather than guessed, the same way
    /// <c>RenderedStyleGateTests.GlyphCoverageFloor</c> is: measured 2026-08-10, the thinnest sample
    /// in this matrix is a chip's glyphs at 10.1% of the frame and the dot holds 70.6%, so the floor
    /// sits at 10x of headroom. Below it a colour is an antialiasing blend between the subject and
    /// the sentinel host, which is exactly the confident wrong colour a pixel gate exists to rule out
    /// rather than introduce.
    /// </summary>
    private const double CoverageFloor = 0.01;

    // ---------------------------------------------------------------------------------------
    // Extraction: the shipped markup, located by what it contains rather than by where it sits.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// One element in <c>MainWindow.xaml</c> whose colour a <c>DataTrigger</c> decides, with the
    /// subtree serialised back to parseable XAML. <see cref="Line"/> is the real line in the real
    /// file, which is what makes a failure message actionable and what makes "this came from the
    /// markup" checkable by a reader rather than merely claimed.
    /// </summary>
    private sealed record TriggerSite(
        string ElementName,
        string ColourProperty,
        string BindingPath,
        int Line,
        string Xaml,
        string DefaultSlot,
        IReadOnlyList<(string Value, string Slot)> TriggerSlots)
    {
        public string Label => $"{ElementName}.{ColourProperty} on {{Binding {BindingPath}}} "
            + $"(MainWindow.xaml:{Line})";
    }

    private static string MainWindowPath()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.False(appDir is null,
            "The repo-root walk failed, so this gate located no markup at all and would measure "
            + "nothing. XamlStyleScanner.AppSourceDirectory() walks up from AppContext.BaseDirectory "
            + "looking for ROROROblox.slnx.");

        var path = Path.Combine(appDir!, "MainWindow.xaml");
        Assert.True(File.Exists(path), $"MainWindow.xaml not found at {path}.");
        return path;
    }

    /// <summary>
    /// Every element of <paramref name="elementName"/> whose inline <c>Style</c> sets
    /// <paramref name="colourProperty"/> from a plain <c>Setter</c> AND overrides it from a
    /// <c>DataTrigger</c> bound to one of <paramref name="bindingPaths"/>.
    /// <para>
    /// Located by CONTENT, never by line number or ordinal position: the two warning chips and the
    /// compact-mode one are structurally identical, so the distinguishing fact is which view-model
    /// property their trigger binds. <see cref="XDocument"/> rather than a regex because the subject
    /// is a nested subtree with attributes, and a regex over markup is how you end up measuring the
    /// wrong element and never finding out.
    /// </para>
    /// </summary>
    private static IReadOnlyList<TriggerSite> Locate(
        string elementName, string colourProperty, params string[] bindingPaths)
    {
        var doc = XDocument.Load(MainWindowPath(), LoadOptions.SetLineInfo);
        var sites = new List<TriggerSite>();

        foreach (var element in doc.Descendants(P + elementName))
        {
            var style = element.Element(P + $"{elementName}.Style")?.Element(P + "Style");
            if (style is null) continue;

            var fallback = style.Elements(P + "Setter")
                .Select(s => (Property: (string?)s.Attribute("Property"), Slot: SlotOf((string?)s.Attribute("Value"))))
                .FirstOrDefault(s => s.Property == colourProperty && s.Slot is not null);
            if (fallback.Slot is null) continue;

            var triggers = new List<(string Value, string Slot)>();
            string? boundPath = null;

            foreach (var trigger in style.Element(P + "Style.Triggers")?.Elements(P + "DataTrigger") ?? [])
            {
                var path = PathOf((string?)trigger.Attribute("Binding"));
                if (path is null || !bindingPaths.Contains(path, StringComparer.Ordinal)) continue;

                var setter = trigger.Elements(P + "Setter")
                    .FirstOrDefault(s => (string?)s.Attribute("Property") == colourProperty);
                var slot = SlotOf((string?)setter?.Attribute("Value"));
                if (slot is null) continue;

                boundPath = path;
                triggers.Add(((string)trigger.Attribute("Value")!, slot));
            }

            if (boundPath is null) continue;

            sites.Add(new TriggerSite(
                elementName,
                colourProperty,
                boundPath,
                ((IXmlLineInfo)element).LineNumber,
                Serialise(element),
                fallback.Slot!,
                triggers));
        }

        return sites;
    }

    /// <summary>The property path inside <c>{Binding X}</c>, or null when the value is not one.</summary>
    private static string? PathOf(string? binding) =>
        binding is not null && binding.StartsWith("{Binding ", StringComparison.Ordinal) && binding.EndsWith('}')
            ? binding["{Binding ".Length..^1].Trim()
            : null;

    /// <summary>The key inside <c>{DynamicResource X}</c>, or null when the value is not one.</summary>
    private static string? SlotOf(string? value) =>
        value is not null && value.StartsWith("{DynamicResource ", StringComparison.Ordinal) && value.EndsWith('}')
            ? value["{DynamicResource ".Length..^1].Trim()
            : null;

    /// <summary>
    /// The extracted subtree as standalone XAML. <see cref="XElement.ToString()"/> already emits the
    /// in-scope declarations the subtree's element NAMES need — the default presentation namespace
    /// here. The <c>x:</c> prefix is added explicitly because a markup extension living in an
    /// ATTRIBUTE VALUE (<c>x:Static</c>, <c>x:Type</c>) carries no XML namespace for the writer to
    /// notice, so an element that grows one later would fail to parse rather than be quietly dropped.
    /// </summary>
    private static string Serialise(XElement element)
    {
        var clone = new XElement(element);
        if (clone.Attribute(XNamespace.Xmlns + "x") is null)
        {
            clone.SetAttributeValue(XNamespace.Xmlns + "x", XamlNs);
        }
        return clone.ToString();
    }

    private static IReadOnlyList<TriggerSite> DotSites() => Locate("Ellipse", "Fill", "StatusDot");

    private static IReadOnlyList<TriggerSite> ChipSites() => Locate("TextBlock", "Foreground", ChipBindings);

    // ---------------------------------------------------------------------------------------
    // The view model: the real one, in the state the real app puts it in.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The REAL <see cref="AccountSummary"/>, not a test double. It takes a plain
    /// <see cref="Account"/> record and no services, so there was no reason to substitute — and the
    /// difference matters: a double would prove the trigger fires on a STRING, while this proves
    /// <see cref="AccountSummary.StatusDot"/> produces that string and the trigger fires on what it
    /// produced. <see cref="RowInState"/> asserts the first half before rendering measures the
    /// second.
    /// </summary>
    private static AccountSummary Row() => new(new Account(
        Id: Guid.NewGuid(),
        DisplayName: "TestAlt",
        AvatarUrl: "https://example.com/avatar.png",
        CreatedAt: DateTimeOffset.UtcNow,
        LastLaunchedAt: null));

    /// <summary>
    /// A row driven into the state that makes <see cref="AccountSummary.StatusDot"/> return
    /// <paramref name="state"/>, through the same properties the app sets. The assertion at the end
    /// is the seam: if the view-model's precedence ever changes, this fails here with the state
    /// named rather than rendering a colour for a state nobody is in.
    /// </summary>
    private static AccountSummary RowInState(string state)
    {
        var vm = Row();
        switch (state)
        {
            case "green": vm.IsRunning = true; break;
            case "yellow": vm.SessionExpired = true; break;
            case "magenta": vm.SessionLimited = true; break;
            case "grey": break;
            default:
                Assert.Fail($"No view-model path is defined for StatusDot state '{state}'.");
                break;
        }

        Assert.True(vm.StatusDot == state,
            $"Drove AccountSummary into what should be the '{state}' state and StatusDot returned "
            + $"'{vm.StatusDot}'. The trigger this gate is about matches on that string, so measuring "
            + "a colour now would measure the wrong state.");

        return vm;
    }

    /// <summary>
    /// A row whose warning chips both have text to draw and whose <paramref name="bindingPath"/>
    /// flag is set to <paramref name="warn"/>. Both chips get content because the three located
    /// sites bind different flags and a chip with no text renders no glyphs to sample.
    /// </summary>
    private static AccountSummary RowWithChips(string bindingPath, bool warn)
    {
        var vm = Row();
        vm.SinceActivity = TimeSpan.FromMinutes(5);
        vm.MemoryText = "2.3 GB";

        switch (bindingPath)
        {
            case "IdleWarn": vm.IdleWarn = warn; break;
            case "MemoryWarning": vm.MemoryWarning = warn; break;
            default:
                Assert.Fail($"No view-model path is defined for chip binding '{bindingPath}'.");
                break;
        }

        return vm;
    }

    // ---------------------------------------------------------------------------------------
    // The matrix.
    // ---------------------------------------------------------------------------------------

    /// <summary>What one rendered case measured, and what it was supposed to.</summary>
    private sealed record Measured(
        TriggerSite Site,
        string State,
        string ThemeId,
        string ExpectedSlot,
        string ExpectedHex,
        Sample Sample)
    {
        /// <summary>The dominant colour the subject drew — the sentinel host is excluded by
        /// <c>ThemedRender</c>, so this is the dot's fill or the chip's glyph colour.</summary>
        public string Hex => Sample.Fill;

        public double Coverage
        {
            get
            {
                var total = Sample.Histogram.Sum(h => h.Count);
                var mine = Sample.Histogram.Where(h => h.Colour == Hex).Sum(h => h.Count);
                return total == 0 ? 0 : (double)mine / total;
            }
        }

        public string Label => $"{Site.ElementName}@{Site.Line} {State} under '{ThemeId}'";
    }

    /// <summary>
    /// Rendered ONCE for the class, same reasoning <see cref="RenderedStyleGateTests"/> records: a
    /// render is not free, the design named flake as this phase's real risk, and re-rolling the same
    /// matrix per fact buys nothing but extra chances to be flaky. Every fact below still asserts its
    /// own floor, so none can report green on an empty matrix.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<Measured>> DotMatrix = new(RenderDots);

    private static readonly Lazy<IReadOnlyList<Measured>> ChipMatrix = new(RenderChips);

    /// <summary>
    /// The app's real built-in themes, from the real <see cref="ThemeStore"/>, pointed at a throwaway
    /// folder so a dev box's user themes in <c>%LOCALAPPDATA%</c> cannot contaminate the result. Not
    /// a hand-written list — same construction the two sibling gates use, and the reason
    /// <c>flatline</c> enrolled itself in v1.17 with nothing to wire.
    /// </summary>
    private static IReadOnlyList<Theme> BuiltInThemes()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-trigger-gate-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn)
            .ToList();

        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
        catch (IOException) { }

        Assert.True(themes.Count >= 4,
            $"Expected at least the 4 built-in themes (brand, midnight, magenta-heat, flatline); got {themes.Count}.");

        return themes;
    }

    /// <summary>
    /// Parses the extracted subtree into a live object ON THE STA THREAD and hands it its
    /// DataContext. Parsing inside the delegate is deliberate: WPF affinitises a
    /// <c>DispatcherObject</c> to its creating thread, and <c>ThemedRender.Measure</c> renders on a
    /// fresh STA thread per call.
    /// <para>
    /// THE DRAIN HERE IS NOT THE ONE <c>ThemedRender</c> ALREADY DOES, and it is load-bearing for the
    /// chips. Assigning <c>DataContext</c> queues binding activation at
    /// <c>DispatcherPriority.DataBind</c>, so a <c>TextBlock</c> whose <c>Text</c> is bound still
    /// measures 0 wide when the caller measures immediately — which is what
    /// <c>ThemedRender.Measure</c>'s zero-size guard reported on the first run of this file, for
    /// every chip, in every theme. Draining before the element leaves this delegate is what makes its
    /// bound text exist by layout time. The dot never needed it: an <see cref="Ellipse"/> sized in
    /// the markup measures the same empty or not, and its trigger settles in
    /// <c>ThemedRender</c>'s own drain, which runs before the bitmap.
    /// </para>
    /// </summary>
    private static Sample Render(Theme theme, TriggerSite site, object dataContext, string what, Action<FrameworkElement> size)
        => ThemedRender.Measure(theme, what, _ =>
        {
            var element = (FrameworkElement)XamlReader.Parse(site.Xaml);
            size(element);
            element.DataContext = dataContext;
            Sta.DrainQueue();
            return element;
        });

    private static IReadOnlyList<Measured> RenderDots()
    {
        var sites = DotSites();
        var cases = new List<Measured>();

        foreach (var theme in BuiltInThemes())
        {
            var slots = ThemedRender.Resources(theme);

            foreach (var site in sites)
            {
                foreach (var (state, slot) in DotMapping)
                {
                    var sample = Render(theme, site, RowInState(state), $"dot/{state}/{theme.Id}",
                        e => { e.Width = DotSize; e.Height = DotSize; });

                    cases.Add(new Measured(site, state, theme.Id, slot, ThemedRender.Slot(slots, slot), sample));
                }
            }
        }

        return cases;
    }

    private static IReadOnlyList<Measured> RenderChips()
    {
        var sites = ChipSites();
        var cases = new List<Measured>();

        foreach (var theme in BuiltInThemes())
        {
            var slots = ThemedRender.Resources(theme);

            foreach (var site in sites)
            {
                foreach (var (warn, slot) in ChipMapping)
                {
                    var vm = RowWithChips(site.BindingPath, warn);
                    var what = $"chip/{site.BindingPath}@{site.Line}/{warn}/{theme.Id}";

                    var sample = Render(theme, site, vm, what, e =>
                    {
                        if (e is TextBlock tb) tb.FontSize = GlyphSize;
                    });

                    cases.Add(new Measured(site, $"{site.BindingPath}={warn}", theme.Id, slot,
                        ThemedRender.Slot(slots, slot), sample));
                }
            }
        }

        return cases;
    }

    // ---------------------------------------------------------------------------------------
    // Facts.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The extraction found what it went looking for, and says what it looked for when it did not.
    /// <para>
    /// This fact exists because a gate that silently measures nothing is worse than no gate — the
    /// lesson this repo already paid for once with a <c>--filter</c> that matched zero tests. Every
    /// fact below re-asserts its own floor for the same reason, so none of them can pass on an empty
    /// matrix even if this one is deleted.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStatusDotIsWhereThisGateThinksItIs()
    {
        var sites = DotSites();

        Assert.True(sites.Count >= 1,
            "Found no <Ellipse> in MainWindow.xaml whose inline <Style> both sets Fill from a "
            + "{DynamicResource} Setter and overrides it from a <DataTrigger Binding=\"{Binding "
            + "StatusDot}\">. That is the per-row status dot, at MainWindow.xaml:443 when this gate "
            + "was written. Either it moved to a different element, its trigger now binds a different "
            + "view-model property, or its Style stopped being inline — in every case this gate is "
            + "measuring nothing and the four status colours are unguarded again.");

        foreach (var site in sites)
        {
            output.WriteLine($"LOCATED {site.Label}  default={site.DefaultSlot}");
            output.WriteLine(site.Xaml);
        }
    }

    /// <summary>
    /// Both warning chips, and the compact-mode one the spec's own correction records as the site a
    /// grep found and the original draft missed. Located by the flag their trigger binds rather than
    /// by position, because all three are structurally identical.
    /// </summary>
    [Fact]
    public void TheWarningChipsAreWhereThisGateThinksTheyAre()
    {
        var sites = ChipSites();

        Assert.True(sites.Count >= 3,
            $"Found {sites.Count} <TextBlock> chips in MainWindow.xaml whose inline <Style> sets "
            + "Foreground from a {DynamicResource} Setter and overrides it from a <DataTrigger> bound "
            + $"to one of [{string.Join(", ", ChipBindings)}]; expected at least 3 (idle chip :489, "
            + "standard memory chip :514, compact-mode memory chip :76, measured 2026-08-10). Spec "
            + "§5.3's own correction records that the compact-mode one is the site a grep found and "
            + "the first draft missed, so a floor of 3 is the part of this that has already been "
            + "wrong once.");

        var bound = sites.Select(s => s.BindingPath).Distinct().ToList();
        foreach (var flag in ChipBindings)
        {
            Assert.Contains(flag, bound);
        }

        foreach (var site in sites)
        {
            output.WriteLine($"LOCATED {site.Label}  default={site.DefaultSlot} "
                + $"triggers=[{string.Join(", ", site.TriggerSlots.Select(t => $"{t.Value}->{t.Slot}"))}]");
        }
    }

    /// <summary>
    /// The markup declares the mapping spec §5.3 states. A STRUCTURAL claim, deliberately separate
    /// from the rendered one: this reads the file, so it cannot catch a slot that resolves to the
    /// wrong colour or a trigger that never fires. It catches the cheaper mistake — someone
    /// repointing a trigger at a slot the spec does not name — one build step earlier and with a
    /// clearer message than a pixel mismatch would give.
    /// </summary>
    [Fact]
    public void TheStatusDotIsMappedTheWayTheSpecSaysItIs()
    {
        var sites = DotSites();
        Assert.True(sites.Count >= 1, "No status-dot site located; see TheStatusDotIsWhereThisGateThinksItIs.");

        var quiet = Array.Find(DotMapping, m => m.State == "grey").Slot;

        foreach (var site in sites)
        {
            Assert.True(site.DefaultSlot == quiet,
                $"{site.Label}: the plain Setter names {site.DefaultSlot}; spec §5.3 maps the quiet "
                + $"state to {quiet}. That Setter is also the fallback for a StatusDot string nobody "
                + "planned for, which is what the deleted converter's `_ => Grey` did.");

            foreach (var (state, slot) in DotMapping.Where(m => m.State != "grey"))
            {
                var declared = site.TriggerSlots.Where(t => t.Value == state).Select(t => t.Slot).ToList();

                Assert.True(declared.Count == 1,
                    $"{site.Label}: expected exactly one DataTrigger on StatusDot=='{state}' setting "
                    + $"Fill, found {declared.Count}.");

                Assert.True(declared[0] == slot,
                    $"{site.Label}: StatusDot=='{state}' sets {declared[0]}; spec §5.3 maps it to "
                    + $"{slot}.");
            }
        }
    }

    /// <summary>
    /// RULE 1 — RESOLUTION. Every state of the shipped dot renders the exact theme slot spec §5.3
    /// maps it to, in every built-in theme, measured in pixels.
    /// <para>
    /// This is the sentence that turns the fence into a gate. <c>ThemedStatusColourTests</c> proves
    /// no colour literal survives in App code; that is a claim about provenance. This proves the
    /// <c>DataTrigger</c> actually fired, the <c>{DynamicResource}</c> actually resolved, and the
    /// bytes on the surface are the theme's — three things a template override, a failed lookup or
    /// an alpha composite can each break without any of the static checks noticing.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryStatusDotStateRendersTheThemeSlotItIsMappedTo()
    {
        var cases = DotMatrix.Value;

        // 4 states x 4 built-in themes x 1 site. A floor, not an equality, so a fifth built-in theme
        // enrols itself without editing a number here.
        Assert.True(cases.Count >= 16,
            $"Rendered {cases.Count} dot cases; expected at least 16 (4 states x 4 built-in themes). "
            + "A short matrix passes every assertion below while measuring less than it claims.");

        var failures = new List<string>();

        foreach (var c in cases)
        {
            output.WriteLine($"{c.Label,-46} measured={c.Hex} expected={c.ExpectedHex} "
                + $"({c.ExpectedSlot}) coverage={c.Coverage:P1}");

            if (c.Coverage < CoverageFloor)
            {
                failures.Add($"{c.Label}: the dominant colour {c.Hex} covers only {c.Coverage:P2} of "
                    + $"the bitmap, below the {CoverageFloor:P0} floor. That is an antialiasing "
                    + "artifact, not the dot — the render produced nothing solid enough to measure.");
                continue;
            }

            if (c.Hex != c.ExpectedHex)
            {
                failures.Add($"{c.Label}: rendered {c.Hex}, expected {c.ExpectedHex} from slot "
                    + $"{c.ExpectedSlot} (spec §5.3). Histogram:\n" + c.Sample.Describe());
            }
        }

        Assert.True(failures.Count == 0,
            "Status-dot states that did not render the theme slot spec §5.3 maps them to. These are "
            + "pixels from the shipped MainWindow.xaml subtree, so a trigger that did not fire, a "
            + "DynamicResource that fell back, or a repointed slot all land here:\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// RULE 1 for the warning chips: warn takes <c>RowExpiredAccentBrush</c>, quiet takes
    /// <c>MutedTextBrush</c>, in every theme, for every located chip including the compact-mode one.
    /// </summary>
    [Fact]
    public void EveryWarningChipStateRendersTheThemeSlotItIsMappedTo()
    {
        var cases = ChipMatrix.Value;

        // 3 chips x 2 states x 4 built-in themes.
        Assert.True(cases.Count >= 24,
            $"Rendered {cases.Count} chip cases; expected at least 24 (3 chips x 2 states x 4 themes).");

        var failures = new List<string>();

        foreach (var c in cases)
        {
            output.WriteLine($"{c.Label,-46} measured={c.Hex} expected={c.ExpectedHex} "
                + $"({c.ExpectedSlot}) coverage={c.Coverage:P1}");

            if (c.Coverage < CoverageFloor)
            {
                failures.Add($"{c.Label}: the dominant colour {c.Hex} covers only {c.Coverage:P2} of "
                    + $"the bitmap, below the {CoverageFloor:P0} floor — the chip drew no glyphs "
                    + "solid enough to measure, so this case measured an antialiasing artifact.");
                continue;
            }

            if (c.Hex != c.ExpectedHex)
            {
                failures.Add($"{c.Label}: rendered {c.Hex}, expected {c.ExpectedHex} from slot "
                    + $"{c.ExpectedSlot} (spec §5.3). Histogram:\n" + c.Sample.Describe());
            }
        }

        Assert.True(failures.Count == 0,
            "Warning-chip states that did not render the theme slot spec §5.3 maps them to:\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// RULE 2 — DISTINCTNESS. The four dot states land on four different colours in every theme.
    /// <para>
    /// This is the assertion that would have caught the decision spec §5.3 made by hand. Cyan was the
    /// tempting slot for <c>green</c>, and it was rejected on measurement: under flatline
    /// <c>CyanBrush</c> and <c>RowExpiredAccentBrush</c> are the same value, so active and expired
    /// would have rendered at 1.00:1 of each other — two states, one colour, no way to tell them
    /// apart. Nothing enforced that reasoning until now. A future palette that collapses any two
    /// states fails here, in whichever theme collapses them, by name.
    /// </para>
    /// <para>
    /// ASSERTED AS INEQUALITY, not as a ratio threshold. The spec records the four states separating
    /// by only 1.36:1, 1.95:1 and 1.77:1 under flatline and defends that as adequate because
    /// <c>SecondaryStatusText</c> states every state in words beside the dot. Legislating a higher
    /// floor here would fail a shipped design this gate has no standing to overrule. What IS pinned
    /// is narrower and different in kind: those three published figures, under flatline only, because
    /// a number a spec prints as its evidence going stale is a defect in the document even when the
    /// app is fine. Every other theme's separations are printed, not gated.
    /// </para>
    /// <para>
    /// Printing them turned out to matter. §5.3 reasons about flatline as the hard case, and flatline
    /// is not the hard case here: measured 2026-08-10, its closest pair is green/yellow at 1.36:1
    /// while <c>midnight</c> puts magenta/grey at 1.19:1 and <c>brand</c> puts yellow/grey at 1.29:1.
    /// The achromatic theme separates its dots BETTER than the two chromatic ones do, because it was
    /// designed to and they were not.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFourDotStatesStayMutuallyDistinctInEveryTheme()
    {
        var cases = DotMatrix.Value;
        Assert.True(cases.Count >= 16, $"Rendered {cases.Count} dot cases; expected at least 16.");

        // spec §5.3: "The four dot values separate by only 1.36:1, 1.95:1 and 1.77:1 from each other
        // under flatline." Those are the three ADJACENT separations in luminance order — green,
        // yellow, grey, magenta from lightest down.
        double[] publishedFlatlineSeparations = [1.36, 1.95, 1.77];

        var collisions = new List<string>();
        var drifted = new List<string>();

        foreach (var group in cases.GroupBy(c => (c.ThemeId, c.Site.Line)))
        {
            var byState = group.ToList();
            Assert.True(byState.Count >= 4,
                $"Theme '{group.Key.ThemeId}' measured only {byState.Count} dot states; expected 4.");

            var closest = (Pair: string.Empty, Ratio: double.MaxValue);

            for (var i = 0; i < byState.Count; i++)
            {
                for (var j = i + 1; j < byState.Count; j++)
                {
                    var a = byState[i];
                    var b = byState[j];

                    var ratio = ContrastGuard.RatioBetween(a.Hex, b.Hex);
                    Assert.True(ratio.HasValue,
                        $"'{group.Key.ThemeId}': no ratio computable between {a.State} ({a.Hex}) and "
                        + $"{b.State} ({b.Hex}). A null is a sampled pixel ContrastGuard could not "
                        + "parse, asserted rather than coerced to zero.");

                    if (ratio!.Value < closest.Ratio)
                    {
                        closest = ($"{a.State}/{b.State}", ratio.Value);
                    }

                    if (a.Hex == b.Hex)
                    {
                        collisions.Add($"'{group.Key.ThemeId}': {a.State} and {b.State} both render "
                            + $"{a.Hex} — 1.00:1, indistinguishable. Slots {a.ExpectedSlot} and "
                            + $"{b.ExpectedSlot} resolve to the same value in this theme.");
                    }
                }
            }

            // Luminance order without reimplementing luminance: RatioBetween against black is
            // (L + 0.05) / 0.05, which is monotonic in L, so sorting by it sorts by lightness.
            var ladder = byState
                .OrderByDescending(c => ContrastGuard.RatioBetween("#000000", c.Hex) ?? 0)
                .ToList();

            var steps = Enumerable.Range(0, ladder.Count - 1)
                .Select(i => (Pair: $"{ladder[i].State}/{ladder[i + 1].State}",
                              Ratio: ContrastGuard.RatioBetween(ladder[i].Hex, ladder[i + 1].Hex) ?? 0))
                .ToList();

            output.WriteLine($"DISTINCT '{group.Key.ThemeId}': closest pair {closest.Pair} at "
                + $"{closest.Ratio:F2}:1  ladder ["
                + string.Join(", ", steps.Select(s => $"{s.Pair} {s.Ratio:F2}:1")) + "]  ["
                + string.Join(", ", byState.Select(c => $"{c.State}={c.Hex}")) + "]");

            if (group.Key.ThemeId != "flatline") continue;

            for (var i = 0; i < publishedFlatlineSeparations.Length && i < steps.Count; i++)
            {
                if (Math.Round(steps[i].Ratio, 2) != publishedFlatlineSeparations[i])
                {
                    drifted.Add($"step {i + 1} ({steps[i].Pair}): spec §5.3 publishes "
                        + $"{publishedFlatlineSeparations[i]:F2}:1, measured {steps[i].Ratio:F4}:1");
                }
            }
        }

        Assert.True(collisions.Count == 0,
            "Two status-dot states render the same colour, so the dot cannot distinguish them. This "
            + "is exactly the collision spec §5.3 rejected CyanBrush to avoid, now measured rather "
            + "than reasoned:\n  " + string.Join("\n  ", collisions));

        Assert.True(drifted.Count == 0,
            "spec §5.3 publishes the flatline dot ladder as 1.36:1 / 1.95:1 / 1.77:1 and the palette "
            + "no longer produces it. Re-measure and update the spec rather than widening a tolerance "
            + "here — that ladder is the evidence the four-state mapping rests on:\n  "
            + string.Join("\n  ", drifted));
    }

    /// <summary>
    /// The dot against the row behind it — RECORDED, and deliberately not gated at 3:1.
    /// <para>
    /// SPEC §5.3'S ARGUMENT, restated so it is arguable rather than assumed. Under flatline the four
    /// states measure 13.17:1, 9.68:1, 4.98:1 and 2.81:1 against the row, and the <c>limited</c> dot
    /// therefore sits below the 3:1 WCAG 1.4.11 asks of a graphical object <em>that carries
    /// information alone</em>. The spec's claim is that it does not carry it alone:
    /// <c>SecondaryStatusText</c> states all four states in words immediately beside it (§6.1), so
    /// the dot is a redundant echo rather than a required graphical object. This gate does not
    /// legislate that claim in either direction. Asserting 3:1 would fail a shipped design on a rule
    /// the spec argued its way out of; ignoring the question would let the number drift with nobody
    /// watching. So the ratios are measured and printed for every state in every theme, and the four
    /// figures the spec actually PUBLISHED are pinned, because a published number going stale is a
    /// defect in the document even when the app is fine.
    /// </para>
    /// <para>
    /// ONE DISCREPANCY, recorded rather than corrected. The spec's four flatline figures are all
    /// computed against <c>RowBgBrush</c>, and this reproduces them on that basis. But the
    /// <c>yellow</c> dot appears exactly when <c>SessionExpired</c> is true, which is exactly when
    /// the row's own <c>DataTrigger</c> (<c>MainWindow.xaml:229</c>) repaints the row to
    /// <c>RowExpiredBgBrush</c> — so 9.68:1 is measured against a surface that state never shows.
    /// The against-the-real-surface figure is printed beside it. This is a note on the spec, not a
    /// failure of the app: the real surface is lighter, so the expired dot's true ratio is lower than
    /// the published one, and still well clear.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDotAgainstItsRowIsRecordedRatherThanGated()
    {
        var cases = DotMatrix.Value;
        Assert.True(cases.Count >= 16, $"Rendered {cases.Count} dot cases; expected at least 16.");

        // spec §5.3's published table, against RowBgBrush, to two decimals. Not tunable: these are
        // the output of the shipped palette through ContrastGuard.RatioBetween.
        var published = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["green"] = 13.17,
            ["yellow"] = 9.68,
            ["grey"] = 4.98,
            ["magenta"] = 2.81,
        };

        var themes = BuiltInThemes().ToDictionary(t => t.Id, StringComparer.Ordinal);
        var drifted = new List<string>();

        foreach (var c in cases)
        {
            var slots = ThemedRender.Resources(themes[c.ThemeId]);
            var rowBg = ThemedRender.Slot(slots, ThemeSlots.RowBg);

            // The surface the state actually shows: the row repaints to RowExpiredBg under exactly
            // the condition that makes the dot yellow.
            var actualSurface = c.State == "yellow"
                ? ThemedRender.Slot(slots, ThemeSlots.RowExpiredBg)
                : rowBg;

            var vsRowBg = ContrastGuard.RatioBetween(rowBg, c.Hex);
            var vsActual = ContrastGuard.RatioBetween(actualSurface, c.Hex);

            Assert.True(vsRowBg.HasValue && vsActual.HasValue,
                $"{c.Label}: no ratio computable for {c.Hex} on {rowBg} / {actualSurface}. A null is a "
                + "value ContrastGuard could not parse, asserted rather than coerced.");

            output.WriteLine($"{c.Label,-46} dot={c.Hex} vs RowBg {rowBg} = {vsRowBg!.Value:F2}:1"
                + (c.State == "yellow"
                    ? $"   vs the surface it actually shows, RowExpiredBg {actualSurface} = {vsActual!.Value:F2}:1"
                    : string.Empty));

            if (c.ThemeId == "flatline" && Math.Round(vsRowBg.Value, 2) != published[c.State])
            {
                drifted.Add($"{c.State}: spec §5.3 publishes {published[c.State]:F2}:1 against RowBg "
                    + $"under flatline; measured {vsRowBg.Value:F4}:1 ({c.Hex} on {rowBg})");
            }
        }

        Assert.True(drifted.Count == 0,
            "spec §5.3 publishes four flatline ratios as the evidence for choosing WhiteBrush over "
            + "CyanBrush, and the palette no longer produces them. Re-measure and update the spec's "
            + "table with the new numbers rather than widening a tolerance here — the argument for the "
            + "mapping rests on those figures:\n  " + string.Join("\n  ", drifted));
    }
}
