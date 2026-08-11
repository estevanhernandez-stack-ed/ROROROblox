using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
/// </summary>
public class ButtonStateGateTests
{
    private static readonly string[] Ranks =
    {
        "PrimaryButtonStyle",
        "SecondaryButtonStyle",
        "SecondaryStrongButtonStyle",
        "DestructiveButtonStyle",
        "CtaButtonStyle",
        "AccentActionButtonStyle",
    };

    private static Theme Flatline() => new(
        Id: "flatline", Name: "Flatline",
        Bg: "#101010", Cyan: "#D4D4D4", Magenta: "#6E6E6E", White: "#F5F5F5",
        MutedText: "#989898", Divider: "#333333", RowBg: "#2A2A2A",
        RowExpiredBg: "#3D3D3D", RowExpiredAccent: "#D4D4D4", Navy: "#101010",
        IsBuiltIn: true);

    /// <summary>Resolve a rank's template the way the app composes its dictionaries.</summary>
    private static ControlTemplate TemplateFor(string rank) => Sta.Run(() =>
    {
        var dict = ThemedRender.Resources(Flatline());
        var btn = (Button)ThemedRender.Styled(dict, rank);
        btn.Resources = dict;
        btn.ApplyTemplate();
        return btn.Template!;
    }, $"template for {rank}");

    [Theory]
    [InlineData("PrimaryButtonStyle")]
    [InlineData("SecondaryButtonStyle")]
    [InlineData("SecondaryStrongButtonStyle")]
    [InlineData("DestructiveButtonStyle")]
    [InlineData("CtaButtonStyle")]
    [InlineData("AccentActionButtonStyle")]
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
    [InlineData("PrimaryButtonStyle")]
    [InlineData("SecondaryButtonStyle")]
    [InlineData("SecondaryStrongButtonStyle")]
    [InlineData("DestructiveButtonStyle")]
    [InlineData("CtaButtonStyle")]
    [InlineData("AccentActionButtonStyle")]
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
                    setter.Value is System.Windows.Media.Brush,
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
    /// </summary>
    [Theory]
    [InlineData("PrimaryButtonStyle")]
    [InlineData("SecondaryButtonStyle")]
    [InlineData("SecondaryStrongButtonStyle")]
    [InlineData("DestructiveButtonStyle")]
    [InlineData("CtaButtonStyle")]
    [InlineData("AccentActionButtonStyle")]
    [InlineData("WarningButtonStyle")]
    [InlineData("GhostButtonStyle")]
    public void HoverAndPressed_ShowDistinctSheens_AndNeverTouchTheFill(string rank)
    {
        var tmpl = TemplateFor(rank);

        static string[] TargetsFor(ControlTemplate t, string property) => t.Triggers
            .OfType<Trigger>()
            .Where(x => x.Property.Name == property && Equals(x.Value, true))
            .SelectMany(x => x.Setters.OfType<Setter>())
            .Select(s => s.TargetName ?? "<button>")
            .ToArray();

        var hover = TargetsFor(tmpl, nameof(UIElement.IsMouseOver));
        var pressed = TargetsFor(tmpl, "IsPressed");

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
    [InlineData("PrimaryButtonStyle")]
    [InlineData("SecondaryButtonStyle")]
    [InlineData("SecondaryStrongButtonStyle")]
    [InlineData("DestructiveButtonStyle")]
    [InlineData("CtaButtonStyle")]
    [InlineData("AccentActionButtonStyle")]
    [InlineData("WarningButtonStyle")]
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

    /// <summary>Every FrameworkElement in a loaded template tree, depth-first.</summary>
    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement root)
    {
        yield return root;
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        if (count == 0 && root is System.Windows.Controls.Decorator d && d.Child is FrameworkElement dc)
        {
            foreach (var e in Descendants(dc)) yield return e;
            yield break;
        }
        if (root is System.Windows.Controls.Panel p)
        {
            foreach (FrameworkElement c in p.Children.OfType<FrameworkElement>())
                foreach (var e in Descendants(c)) yield return e;
        }
    }
}
