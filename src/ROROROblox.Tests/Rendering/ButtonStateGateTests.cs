using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The states a button shows when you touch it, which nothing in this project measured until v1.20.
/// <para>
/// The four ranks used to set resting colours and inherit their <c>ControlTemplate</c> from the OS.
/// That template's state triggers are hardcoded literals: hover <c>#BEE6FD</c> (Windows Aero light
/// blue), pressed <c>#C4E5F6</c>, disabled <c>#F4F4F4</c>. Every button in the app flashed Aero blue
/// on hover, in every theme, for the app's whole life — including flatline, whose entire purpose is
/// carrying no meaning in colour.
/// </para>
/// <para>
/// <b>Why this reads the template rather than rendering a hovered button.</b> <c>IsMouseOver</c> is
/// set by the input system and cannot be assigned, and
/// <c>VisualStateManager.GoToState(btn, "MouseOver")</c> returns <b>False</b> here because this
/// template uses property triggers and declares no visual state groups. A probe during <c>/prd</c>
/// did not know that, forced a state that never applied, sampled a pixel identical to resting, and
/// read it as "hover changes nothing" — a confident wrong answer that survived until a control was
/// added for it. Reading the resolved template's own setters is what actually answered the question,
/// and it needs no input simulation.
/// </para>
/// <para>
/// <b>Coverage is one list, deliberately.</b> The first version of this file carried four theories
/// with hand-written <c>InlineData</c> covering 6, 6, 8 and 7 ranks respectively, beside a
/// <c>Ranks</c> array that nothing referenced. Two ranks were therefore unmeasured by half the gate
/// and the ToggleButton rank by all of it — while the file read, at a glance, as covering
/// everything. Every theory now enumerates <see cref="Ranks"/>, so a rank added to the vocabulary
/// is either measured by all of this or by none of it, and "none" fails
/// <see cref="TheRankListMatchesTheVocabulary"/>.
/// </para>
/// </summary>
public class ButtonStateGateTests
{
    /// <summary>WCAG 1.4.3 AA for body text. A state is still text; the floor does not move.</summary>
    private const double AaThreshold = 4.5;

    /// <summary>
    /// Every rank in the vocabulary, Button and ToggleButton alike. This is the list; nothing else
    /// in this file writes rank names.
    /// </summary>
    private static readonly string[] Ranks =
    {
        "PrimaryButtonStyle",
        "SecondaryButtonStyle",
        "SecondaryStrongButtonStyle",
        // F-041. Not a new rank — it IS SecondaryStrong, plus the padding, alignment and
        // Enter/Esc behaviour that nine Close buttons used to restate five different ways.
        // Measured anyway, because BasedOn is exactly how a rank quietly acquires a
        // different resting pair without anyone deciding to give it one.
        "DialogCloseButtonStyle",
        "DestructiveButtonStyle",
        "CtaButtonStyle",
        "AccentActionButtonStyle",
        "WarningButtonStyle",
        "GhostButtonStyle",
        "SecondaryToggleButtonStyle",
    };

    public static TheoryData<string> AllRanks()
    {
        var data = new TheoryData<string>();
        foreach (var r in Ranks) data.Add(r);
        return data;
    }

    public static TheoryData<string, string> AllRanksAndThemes()
    {
        var data = new TheoryData<string, string>();
        foreach (var r in Ranks)
            foreach (var t in BuiltInThemeIds)
                data.Add(r, t);
        return data;
    }

    private static readonly string[] BuiltInThemeIds = { "brand", "midnight", "magenta-heat", "flatline" };

    /// <summary>
    /// The real shipped themes, resolved through the real store. Pointed at a throwaway folder so a
    /// dev box's user themes in <c>%LOCALAPPDATA%</c> cannot contaminate a measurement — the same
    /// precaution <c>ContrastPairGateTests.BuiltInThemes</c> takes and for the same reason.
    /// </summary>
    private static Theme ThemeById(string id)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-state-gate-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Resolve a rank's template the way the app composes its dictionaries.</summary>
    private static ControlTemplate TemplateFor(string rank) => Sta.Run(() =>
    {
        var dict = ThemedRender.Resources(ThemeById("flatline"));
        var ctl = (Control)ThemedRender.Styled(dict, rank);
        ctl.Resources = dict;
        ctl.ApplyTemplate();
        return ctl.Template!;
    }, $"template for {rank}");

    // ------------------------------------------------------------------------------------------
    // Structure
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The list above is the whole vocabulary, not a subset somebody kept up to date by hand.
    /// <para>
    /// This is the assertion that makes every other theory in the file trustworthy. Without it,
    /// adding a rank to <c>ControlStyles.xaml</c> and forgetting this file produces a gate that
    /// passes while measuring less than it did yesterday — the exact defect that let the ToggleButton
    /// rank ship with the OS template's hardcoded <c>#BEE6FD</c> hover in the middle of the toolbar.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRankListMatchesTheVocabulary()
    {
        var declared = XamlStyleScanner.EnumerateAppXamlFiles()
            .Where(f => f.FullPath.EndsWith("ControlStyles.xaml", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => System.Text.RegularExpressions.Regex
                .Matches(File.ReadAllText(f.FullPath), @"x:Key=""(\w*Button(?:Style)?)""")
                .Select(m => m.Groups[1].Value))
            .Where(k => k.EndsWith("ButtonStyle", StringComparison.Ordinal))
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        var measured = Ranks.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.True(
            declared.SequenceEqual(measured, StringComparer.Ordinal),
            "The rank list in this file has drifted from ControlStyles.xaml.\n"
            + $"  declared in XAML : {string.Join(", ", declared)}\n"
            + $"  measured here    : {string.Join(", ", measured)}\n"
            + "A rank the gate does not name is a rank nothing in this file measures.");
    }

    [Theory]
    [MemberData(nameof(AllRanks))]
    public void EveryRank_UsesOurTemplate_NotTheInheritedOne(string rank)
    {
        var tmpl = TemplateFor(rank);

        // The inherited OS template names its chrome element "border" and carries five triggers
        // including IsDefaulted and IsChecked. Ours names it "Chrome" and carries three. Asserting
        // on the element name rather than the count, because a count is the kind of thing that
        // drifts and still passes.
        var targets = tmpl.Triggers.OfType<Trigger>()
            .SelectMany(t => t.Setters.OfType<Setter>())
            .Select(s => s.TargetName)
            .Where(n => n is not null)
            .Distinct()
            .ToArray();

        // "border" is the OS template's chrome element. Ours are the sheen layers, which is what
        // the state triggers address since hover stopped repainting the fill.
        Assert.DoesNotContain("border", targets);
        Assert.Contains("HoverSheen", targets);

        // And the fill element is present in the tree we authored, even though no trigger targets
        // it any more -- asserting only on trigger targets would pass against a template that had
        // sheens and no Chrome to lay them over.
        var named = Sta.Run(() =>
        {
            var tree = (FrameworkElement)tmpl.LoadContent();
            return Descendants(tree).Select(e => e.Name)
                .Where(n => !string.IsNullOrEmpty(n)).ToArray();
        }, $"template tree for {rank}");
        Assert.Contains("Chrome", named);
    }

    /// <summary>
    /// The headline assertion. A state setter whose value is a literal cannot follow a theme, which
    /// is the entire defect: <c>#BEE6FD</c> stayed <c>#BEE6FD</c> under flatline.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllRanks))]
    public void NoStateSetter_CarriesAHardcodedColour(string rank)
    {
        var tmpl = TemplateFor(rank);

        foreach (var trigger in tmpl.Triggers.OfType<Trigger>())
        {
            foreach (var setter in trigger.Setters.OfType<Setter>())
            {
                // A DynamicResource reference survives as a DynamicResourceExtension in the
                // setter's value. A literal colour arrives already converted to a Brush, which is
                // exactly what the inherited template did and what must never come back.
                Assert.False(
                    setter.Value is Brush,
                    $"{rank}: trigger {trigger.Property.Name}=={trigger.Value} sets "
                    + $"{setter.Property.Name} to a literal brush. State colours must be "
                    + "{DynamicResource} or they cannot follow a theme — that is F-068.");
            }
        }
    }

    /// <summary>
    /// Hover and pressed must both exist, must differ, and must not replace the fill.
    /// <para>
    /// The first template swapped <c>Chrome.Background</c> to a fixed slot on hover. That is a
    /// SURFACE colour, so hovering a bright cyan CTA turned it dark navy and the builder reported
    /// the button dimming to nothing at C1. It was not dimming; it was being replaced. The fix is
    /// a translucent sheen layer, which is relative to whatever fill is underneath and therefore
    /// correct for every rank and every theme, including ones nobody has written yet.
    /// </para>
    /// <para>
    /// A ToggleButton's second state is <c>IsChecked</c> rather than <c>IsPressed</c> — a toggle
    /// that is ON is a control being held down, so it reuses the press sheen rather than earning a
    /// third layer.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllRanks))]
    public void HoverAndPressed_ShowDistinctSheens_AndNeverTouchTheFill(string rank)
    {
        var tmpl = TemplateFor(rank);

        static string[] TargetsFor(ControlTemplate t, params string[] properties) => t.Triggers
            .OfType<Trigger>()
            .Where(x => properties.Contains(x.Property.Name) && Equals(x.Value, true))
            .SelectMany(x => x.Setters.OfType<Setter>())
            .Select(s => s.TargetName ?? "<button>")
            .ToArray();

        var hover = TargetsFor(tmpl, nameof(UIElement.IsMouseOver));
        var pressed = TargetsFor(tmpl, "IsPressed", "IsChecked");

        Assert.NotEmpty(hover);
        Assert.NotEmpty(pressed);

        // Distinct layers, or pressing a hovered button shows no change at all -- and hovering is
        // a precondition for pressing, so the eye never catches that one.
        Assert.NotEqual(hover, pressed);

        // And neither may touch the fill. This is the assertion that would have caught the
        // original defect: a hover that repaints Chrome is a hover that can erase a bright button.
        foreach (var trigger in tmpl.Triggers.OfType<Trigger>())
        {
            foreach (var setter in trigger.Setters.OfType<Setter>())
            {
                var touchesFill = setter.TargetName == "Chrome"
                    && setter.Property == Border.BackgroundProperty;
                Assert.False(touchesFill,
                    $"{rank}: trigger {trigger.Property.Name}=={trigger.Value} repaints Chrome's "
                    + "Background. A state that replaces the fill rather than layering over it "
                    + "turns a bright button dark and reads as the control vanishing.");
            }
        }
    }

    /// <summary>
    /// A disabled button must still be visible.
    /// <para>
    /// The first template dimmed <c>Chrome</c> to 45% opacity for the disabled state. On a dark row
    /// that does not read as dimmed, it reads as gone: a disabled <c>Launch As</c> lost its cyan
    /// entirely and left muted text floating, and the builder flagged it as "the buttons are going
    /// away" within a minute of the C1 walk. The OS template that was replaced painted a light grey
    /// fill for disabled, which was ugly and unmissable — unmissable being the part that mattered.
    /// </para>
    /// <para>
    /// So: no state may erase the fill. Opacity in particular is a multiplier with no floor, and
    /// the fill underneath it can be any colour a user's theme supplies.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllRanks))]
    public void NoState_DimsTheChromeIntoInvisibility(string rank)
    {
        var tmpl = TemplateFor(rank);

        foreach (var trigger in tmpl.Triggers.OfType<Trigger>())
        {
            foreach (var setter in trigger.Setters.OfType<Setter>())
            {
                Assert.False(
                    setter.Property == UIElement.OpacityProperty,
                    $"{rank}: trigger {trigger.Property.Name}=={trigger.Value} sets Opacity. "
                    + "A state that fades the fill makes the control vanish on a dark surface "
                    + "rather than look unavailable — express the state in a themed brush instead.");
            }
        }
    }

    /// <summary>
    /// Disabled must actually be expressed. Every assertion above is a prohibition, and a template
    /// that simply deleted its <c>IsEnabled</c> trigger satisfies all of them — a disabled button
    /// identical to an enabled one passes "no literal", "no opacity" and "never touches the fill"
    /// perfectly.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllRanks))]
    public void DisabledIsExpressed_AndInAThemedBrush(string rank)
    {
        var tmpl = TemplateFor(rank);

        var disabled = tmpl.Triggers.OfType<Trigger>()
            .Where(t => t.Property.Name == nameof(UIElement.IsEnabled) && Equals(t.Value, false))
            .SelectMany(t => t.Setters.OfType<Setter>())
            .ToArray();

        Assert.True(disabled.Length > 0,
            $"{rank}: the template declares no IsEnabled=False trigger, so a disabled button is "
            + "indistinguishable from an enabled one. Every other assertion in this file is a "
            + "prohibition and would pass against exactly that template.");

        // Two legitimate expressions, and which one a rank uses is decided by its fill. A dark
        // fill swaps the label to the muted token. A bright fill cannot -- muted on cyan measured
        // 1.37:1 -- so it keeps its dark label and reveals a wash layer underneath instead.
        // Accepting only the first is what let the bright ranks ship at 1.29:1 while a gate that
        // demanded "the disabled trigger sets a Foreground" stayed green on them.
        var swapsLabel = disabled.Any(s => s.Property == Control.ForegroundProperty);
        var revealsWash = disabled.Any(s =>
            s.TargetName == "DisabledWash" && s.Property == UIElement.VisibilityProperty);

        Assert.True(swapsLabel || revealsWash,
            $"{rank}: the IsEnabled=False trigger neither swaps the label to a themed brush nor "
            + "reveals a wash layer. Those are the two ways this vocabulary says unavailable.");
    }

    // ------------------------------------------------------------------------------------------
    // Measurement — §5 option (1)'s second half
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// What the label actually sits on in each state, measured, in every shipped theme.
    /// <para>
    /// The assertions above prove a state setter is themed rather than literal. Themed is not the
    /// same as legible: the sheens are white at a fixed opacity, so a hovered button's label sits on
    /// a blend of white and the rank's fill, and the disabled label sits on the fill with a
    /// different foreground token entirely. None of those three surfaces is a theme slot, so nothing
    /// else in the suite measures them — <c>ContrastPairGateTests</c> reads the rank's declared
    /// pair, which is the RESTING pair, and reports it for all four states.
    /// </para>
    /// <para>
    /// Compositing runs through <c>ContrastGuard.Flatten</c> rather than arithmetic written here,
    /// because this repo has twice shipped a fix into one scanner and missed its copy in another.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllRanksAndThemes))]
    public void EveryStatePair_ClearsAa(string rank, string themeId)
    {
        var v = VisualsFor(rank, themeId);

        // A transparent rank has no fill, so there is no surface here to measure against — the
        // label lands on whatever hosts the control, which is the composition problem
        // ContrastPairGateTests declares out of scope for exactly the same reason. Stated rather
        // than silently skipped: GhostButtonStyle's states are unmeasured by this gate.
        if (v.FillIsTransparent) return;

        var hoverSurface = ContrastGuard.Flatten(v.Fill, v.HoverSheen, v.HoverOpacity);
        var pressSurface = ContrastGuard.Flatten(v.Fill, v.PressSheen, v.PressOpacity);

        // A rank with a wash puts the label on the WASHED fill, not the resting one. Measuring
        // disabled against v.Fill for a washed rank would report a number no user ever sees --
        // and it would report the failing one, since the wash is the fix.
        var disabledSurface = v.DisabledWash is null
            ? v.Fill
            : ContrastGuard.Flatten(v.Fill, v.DisabledWash, v.DisabledWashOpacity);

        Check("rest", v.Fill, v.Foreground);
        Check("hover", hoverSurface, v.Foreground);
        Check("pressed", pressSurface, v.Foreground);
        Check("disabled", disabledSurface, v.DisabledForeground);

        void Check(string state, string surface, string label)
        {
            var ratio = ContrastGuard.RatioBetween(surface, label);
            Assert.True(ratio is not null, $"{rank}/{themeId}/{state}: '{surface}' or '{label}' did not parse.");
            Assert.True(ratio >= AaThreshold,
                $"{rank} under '{themeId}', {state} state: label {label} on {surface} measures "
                + $"{ratio:F2}:1, under AA's {AaThreshold:F1}:1.\n"
                + $"  resting fill was {v.Fill}, resting label {v.Foreground}.\n"
                + "A state is still text. Change the rank, not this floor.");
        }
    }

    private sealed record Visuals(
        string Fill, string Foreground, bool FillIsTransparent,
        string HoverSheen, double HoverOpacity,
        string PressSheen, double PressOpacity,
        string DisabledForeground,
        string? DisabledWash, double DisabledWashOpacity);

    /// <summary>
    /// Resolves a rank against a theme the way the app does, then reads back the four things a
    /// state measurement needs: the fill, the resting label, both sheen layers with their opacity,
    /// and the label the disabled trigger swaps in.
    /// <para>
    /// The sheens are read from the APPLIED template rather than hardcoded at 0.22 and 0.38. A gate
    /// carrying its own copy of those numbers would stay green through a retune that made hover
    /// illegible, which is the failure mode this whole file exists to close.
    /// </para>
    /// </summary>
    private static Visuals VisualsFor(string rank, string themeId) => Sta.Run(() =>
    {
        var dict = ThemedRender.Resources(ThemeById(themeId));
        var ctl = (Control)ThemedRender.Styled(dict, rank);
        ctl.Resources = dict;
        ctl.ApplyTemplate();

        var tmpl = ctl.Template!;

        var (sheenHover, opacityHover) = Sheen(tmpl, ctl, "HoverSheen");
        var (sheenPress, opacityPress) = Sheen(tmpl, ctl, "PressSheen");

        // Optional: only the bright-filled ranks carry one. Read rather than assumed, same as the
        // interaction sheens -- a gate holding its own copy of 0.55 stays green through a retune.
        var wash = tmpl.FindName("DisabledWash", ctl) as Border;

        return new Visuals(
            Fill: Hex(ctl.Background),
            Foreground: Hex(ctl.Foreground),
            FillIsTransparent: ctl.Background is SolidColorBrush { Color.A: 0 } or null,
            HoverSheen: sheenHover,
            HoverOpacity: opacityHover,
            PressSheen: sheenPress,
            PressOpacity: opacityPress,
            DisabledForeground: DisabledForeground(tmpl, dict, ctl),
            DisabledWash: wash is null ? null : Hex(wash.Background),
            DisabledWashOpacity: wash?.Opacity ?? 0.0);
    }, $"visuals for {rank}/{themeId}");

    private static (string Hex, double Opacity) Sheen(ControlTemplate tmpl, Control ctl, string name)
    {
        // FindName against the APPLIED template, not LoadContent() — a fresh tree is not connected
        // to the control, so its DynamicResource references resolve against nothing.
        if (tmpl.FindName(name, ctl) is not Border b)
        {
            throw new InvalidOperationException(
                $"the template has no '{name}' element. The state layers are how hover and pressed "
                + "are expressed; a template without them expresses neither.");
        }
        return (Hex(b.Background), b.Opacity);
    }

    /// <summary>
    /// The brush the <c>IsEnabled=False</c> trigger swaps the label to, resolved against the theme.
    /// Falls back to the resting foreground when no trigger sets one — <see
    /// cref="DisabledIsExpressed_AndInAThemedBrush"/> owns that case, so this measures rather than
    /// throws and the two failures stay distinguishable.
    /// </summary>
    private static string DisabledForeground(ControlTemplate tmpl, ResourceDictionary dict, Control ctl)
    {
        var setter = tmpl.Triggers.OfType<Trigger>()
            .Where(t => t.Property.Name == nameof(UIElement.IsEnabled) && Equals(t.Value, false))
            .SelectMany(t => t.Setters.OfType<Setter>())
            .FirstOrDefault(s => s.Property == Control.ForegroundProperty);

        if (setter is null) return Hex(ctl.Foreground);

        return setter.Value switch
        {
            DynamicResourceExtension d when d.ResourceKey is string k && dict[k] is Brush b => Hex(b),
            Brush b => Hex(b),
            _ => Hex(ctl.Foreground),
        };
    }

    /// <summary>Reads a brush back as the hex form ContrastGuard parses, alpha preserved.</summary>
    private static string Hex(Brush? brush)
    {
        if (brush is not SolidColorBrush s) return "";
        var c = s.Color;
        return c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    /// <summary>Every FrameworkElement in a loaded template tree, depth-first.</summary>
    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement root)
    {
        yield return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        if (count == 0 && root is Decorator d && d.Child is FrameworkElement dc)
        {
            foreach (var e in Descendants(dc)) yield return e;
            yield break;
        }
        if (root is Panel p)
        {
            foreach (FrameworkElement c in p.Children.OfType<FrameworkElement>())
                foreach (var e in Descendants(c)) yield return e;
        }
    }
}
