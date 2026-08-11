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

        Assert.DoesNotContain("border", targets);
        Assert.Contains("Chrome", targets);
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

    [Theory]
    [InlineData("PrimaryButtonStyle")]
    [InlineData("SecondaryButtonStyle")]
    [InlineData("SecondaryStrongButtonStyle")]
    [InlineData("DestructiveButtonStyle")]
    [InlineData("CtaButtonStyle")]
    [InlineData("AccentActionButtonStyle")]
    public void HoverAndPressed_AreBothDefined_AndAreNotTheSameSlot(string rank)
    {
        var tmpl = TemplateFor(rank);

        static object? SlotFor(ControlTemplate t, string property) => t.Triggers
            .OfType<Trigger>()
            .Where(x => x.Property.Name == property)
            .SelectMany(x => x.Setters.OfType<Setter>())
            .Where(s => s.Property == Border.BackgroundProperty)
            .Select(s => (s.Value as DynamicResourceExtension)?.ResourceKey)
            .FirstOrDefault();

        var hover = SlotFor(tmpl, nameof(UIElement.IsMouseOver));
        var pressed = SlotFor(tmpl, "IsPressed");

        Assert.NotNull(hover);
        Assert.NotNull(pressed);

        // A hover and a pressed that resolve to the same slot are one state wearing two names —
        // the control would give no feedback on click, which is a regression the eye misses because
        // hovering is a precondition for pressing.
        Assert.NotEqual(hover, pressed);
    }
}
