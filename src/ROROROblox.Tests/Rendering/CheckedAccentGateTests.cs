using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ROROROblox.Core.Theming;
using Xunit;
using Xunit.Abstractions;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The ticked state of a checkbox, which nothing in this project measured until v1.33.
/// <para>
/// WPF-UI's <c>CheckBox</c> template resolves every one of its state fills, and the check glyph,
/// through <c>{DynamicResource}</c>. Its Dark dictionary answers with <b><c>#0067C0</c></b> for the
/// checked fill — a fixed Fluent blue that is in no theme and that no theme change can reach — and
/// with <b>Transparent</b> for both checked hover fills, assigned to the OUTER border, so hovering a
/// ticked box erased the tick's fill and left the glyph floating on the row behind it. Both shipped
/// in every theme for the app's whole life. The accent one was most wrong under <c>flatline</c>,
/// whose entire premise is carrying no meaning in colour and whose ticked boxes were the single blue
/// object on the page; it became impossible to ignore when the Alerts page put 28 of them on one
/// screen.
/// </para>
/// <para>
/// <b>Why this renders pixels rather than only reading the dictionary.</b> The whole claim of the
/// fix is that an entry written into <c>Application.Resources</c> beats WPF-UI's merged dictionary
/// inside a template this repo does not own. Asserting that <c>ThemeService.ApplyTo</c> wrote four
/// keys proves our half and says nothing about the library's. One of these clauses therefore renders
/// a real ticked <c>CheckBox</c> under the real composed dictionaries and reads the fill off the
/// bitmap.
/// </para>
/// </summary>
public class CheckedAccentGateTests
{
    private readonly ITestOutputHelper _output;

    public CheckedAccentGateTests(ITestOutputHelper output) => _output = output;

    /// <summary>WCAG 1.4.3 AA. A check glyph is a graphical object, but it carries the meaning of
    /// the control and it is 13px, so it is held to the text floor the rest of this suite uses
    /// rather than 1.4.11's 3:1.</summary>
    private const double AaThreshold = 4.5;

    /// <summary>
    /// What WPF-UI's Dark dictionary sets the checked fill to. Written down so the clauses below
    /// can assert its ABSENCE by name — "the fill follows the theme" and "the library's blue is
    /// gone" are different statements, and only the second one fails if a future override lands on
    /// a key the template stopped reading.
    /// </summary>
    private const string LibraryBlue = "#0067C0";

    private static readonly string[] BuiltInThemeIds = { "brand", "midnight", "magenta-heat", "flatline" };

    public static TheoryData<string> AllThemes()
    {
        var data = new TheoryData<string>();
        foreach (var t in BuiltInThemeIds) data.Add(t);
        return data;
    }

    /// <summary>
    /// The real shipped themes through the real store, pointed at a throwaway folder so a dev box's
    /// user themes in <c>%LOCALAPPDATA%</c> cannot contaminate a measurement — the same precaution
    /// <c>ContrastPairGateTests.BuiltInThemes</c> and <c>ButtonStateGateTests.ThemeById</c> take.
    /// </summary>
    private static Theme ThemeById(string id)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-checked-accent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var theme = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
                .SingleOrDefault(t => t.IsBuiltIn && t.Id == id);
            Assert.True(theme is not null, $"built-in theme '{id}' is not in ThemeStore any more.");
            return theme!;
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// The four checked slots as the app resolves them, read back as hex ON the STA thread that
    /// built the dictionary. A <c>SolidColorBrush</c> is thread-affine once a dispatcher touches it,
    /// so reading <c>Color</c> from the test thread throws — the first draft of this file did
    /// exactly that, which is a useful reminder that the dictionary these gates measure is a live
    /// WPF object graph and not a bag of strings.
    /// </summary>
    private static (string Resting, string Hover, string Pressed, string Glyph) ResolveCheckedSlots(Theme theme)
        => Sta.Run(() =>
        {
            var dict = ThemedRender.Resources(theme);
            return (
                ThemedRender.Slot(dict, ThemeSlots.CheckedAccent),
                ThemedRender.Slot(dict, ThemeSlots.CheckedAccentHover),
                ThemedRender.Slot(dict, ThemeSlots.CheckedAccentPressed),
                ThemedRender.Slot(dict, ThemeSlots.OnCheckedAccent));
        }, $"checked slots for {theme.Id}");

    // ------------------------------------------------------------------------------------------
    // The override reaches the library's template
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The headline assertion, and the only one here that can tell the truth about WPF-UI: a ticked
    /// box is painted in the theme's accent, in pixels, under the dictionaries the app composes.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllThemes))]
    public void ATickedBoxIsPaintedInTheThemesAccent(string themeId)
    {
        var theme = ThemeById(themeId);

        var sample = ThemedRender.Measure(theme, $"ticked CheckBox under {themeId}", dict =>
            // No explicit Style. The app gets WPF-UI's CheckBox style implicitly from
            // ControlsDictionary, and assigning one here would prove a different thing than the one
            // in question. Content stays null so the box is nearly the whole bitmap.
            new CheckBox { IsChecked = true });

        _output.WriteLine($"{themeId,-14} fill {sample.Fill}  (theme accent {theme.Cyan})");
        _output.WriteLine($"   top colours: {string.Join(", ", sample.Histogram.Take(4).Select(h => $"{h.Colour}x{h.Count}"))}");

        Assert.Equal(theme.Cyan, sample.Fill, ignoreCase: true);

        // Said separately and deliberately. The clause above goes green if the theme's accent ever
        // happened to BE the library's blue; this one is the one that cannot.
        Assert.DoesNotContain(
            sample.Histogram.Select(h => h.Colour),
            c => string.Equals(c, LibraryBlue, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// All four keys are written, for every built-in, and the three fills are DISTINCT — a hover
    /// state identical to rest is a state the eye never sees, and WPF-UI's own answer for both
    /// hover fills was Transparent, which is a distinct value that happens to be a defect.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllThemes))]
    public void EveryCheckedSlotIsResolved_AndTheThreeFillsDiffer(string themeId)
    {
        var theme = ThemeById(themeId);
        var (rest, hover, pressed, glyph) = ResolveCheckedSlots(theme);

        _output.WriteLine($"{themeId,-14} rest {rest}  hover {hover}  pressed {pressed}  glyph {glyph}");

        Assert.Equal(theme.Cyan, rest, ignoreCase: true);

        // Opaque, every one. The library shipped #00FFFFFF for two of these and an alpha value here
        // means the fill is composited against whatever row hosts the control, which is the
        // surface-replacement defect C1 found on buttons wearing a different hat.
        foreach (var (name, value) in new[] { ("rest", rest), ("hover", hover), ("pressed", pressed), ("glyph", glyph) })
        {
            Assert.True(value.Length == 7 && value[0] == '#',
                $"{themeId}: the {name} slot resolved to '{value}'. A checked fill carrying alpha "
                + "composites against the row behind the box rather than over the accent.");
        }

        Assert.NotEqual(rest, hover, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(hover, pressed, StringComparer.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------------------------------
    // Measurement
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The glyph stays readable on all three fills, in all four themes, recorded on every run.
    /// <para>
    /// <c>ContrastPairGateTests.NamedPairs</c> carries the same three pairs and is the gate that
    /// FAILS the build. This clause exists beside it because that one measures the slots and this
    /// one names the control, and because the numbers belong somewhere a reader of this file can
    /// see them without running the other.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllThemes))]
    public void TheGlyphClearsAaOnEveryCheckedFill(string themeId)
    {
        var theme = ThemeById(themeId);
        var (rest, hover, pressed, glyph) = ResolveCheckedSlots(theme);

        foreach (var (state, fill) in new[] { ("rest", rest), ("hover", hover), ("pressed", pressed) })
        {
            var ratio = ContrastGuard.RatioBetween(fill, glyph);

            Assert.True(ratio is not null, $"{themeId}/{state}: '{fill}' or '{glyph}' did not parse.");
            _output.WriteLine($"{themeId,-14} {state,-8} glyph {glyph} on {fill}  {ratio!.Value,6:F2}:1");

            Assert.True(ratio >= AaThreshold,
                $"{themeId}, {state} state: the check glyph {glyph} on {fill} measures "
                + $"{ratio.Value:F2}:1, under AA's {AaThreshold:F1}:1. The glyph is derived by "
                + "ContrastGuard.BestForeground against the RESTING fill — if a state fill moved "
                + "away from it, move the fill, not this floor.");
        }
    }

    /// <summary>
    /// The two sheen opacities <c>ThemeService</c> composites with are the same two
    /// <c>ControlStyles.xaml</c> layers, read out of the real template.
    /// <para>
    /// This is the drift this repo keeps shipping: a fix landing in one place and missing its copy
    /// in another. The checkbox accent cannot use the template's sheen LAYERS — WPF-UI's trigger
    /// replaces a fill rather than revealing a layer, and we do not own that template — so it
    /// composites the same blend at apply time from its own constants. Nothing in the language
    /// keeps the two in step, so this does.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSheenOpacitiesMatchTheButtonTemplate()
    {
        var (hover, pressed) = Sta.Run(() =>
        {
            var dict = ThemedRender.Resources(ThemeById("brand"));

            // CtaButtonStyle, because it is a bright-filled rank and therefore carries
            // AppFilledButtonTemplate — the template whose sheens the checkbox accent mirrors.
            var ctl = (Control)ThemedRender.Styled(dict, "CtaButtonStyle");
            ctl.Resources = dict;
            ctl.ApplyTemplate();

            var tmpl = ctl.Template!;
            double Opacity(string name) => tmpl.FindName(name, ctl) is Border b
                ? b.Opacity
                : throw new InvalidOperationException(
                    $"AppFilledButtonTemplate has no '{name}' element. The sheen layers are how "
                    + "hover and pressed are expressed; without them there is nothing to mirror.");

            return (Opacity("HoverSheen"), Opacity("PressSheen"));
        }, "sheen opacities");

        _output.WriteLine($"template hover {hover:F2} / pressed {pressed:F2}");

        Assert.Equal(hover, ROROROblox.App.Theming.ThemeService.HoverSheenOpacity, precision: 6);
        Assert.Equal(pressed, ROROROblox.App.Theming.ThemeService.PressSheenOpacity, precision: 6);
    }

    /// <summary>
    /// The four keys are the library's, so a WPF-UI bump that renames one would leave the override
    /// resolving nothing and the blue quietly back — with every assertion above still green, because
    /// they all read OUR dictionary. This reads the shipped <c>DefaultCheckBoxStyle</c> and asserts
    /// its template still references each key by name.
    /// </summary>
    [Fact]
    public void TheLibrarysTemplateStillReadsEveryKeyWeOverride()
    {
        var markup = Sta.Run(() =>
        {
            var dict = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/Wpf.Ui;component/Controls/CheckBox/CheckBox.xaml",
                    UriKind.Absolute),
            };

            var style = dict["DefaultCheckBoxStyle"] as Style;
            Assert.True(style is not null, "WPF-UI no longer exposes DefaultCheckBoxStyle.");
            return System.Windows.Markup.XamlWriter.Save(style);
        }, "WPF-UI CheckBox style");

        foreach (var key in new[]
        {
            ThemeSlots.CheckedAccent,
            ThemeSlots.CheckedAccentHover,
            ThemeSlots.CheckedAccentPressed,
            ThemeSlots.OnCheckedAccent,
        })
        {
            Assert.True(markup.Contains(key, StringComparison.Ordinal),
                $"WPF-UI's CheckBox template no longer resolves '{key}'. Overriding a key nothing "
                + "reads is an override that does nothing, and every other clause in this file "
                + "would stay green through it. Re-read the shipped template and rebind.");
        }
    }
}
