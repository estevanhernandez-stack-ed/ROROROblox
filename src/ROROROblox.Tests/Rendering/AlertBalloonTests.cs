using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ROROROblox.App.Tray;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The balloon RoRoRo draws for itself (v1.33 item 4), on both axes that can be tested without
/// eyes: what a click does, and whether its brushes resolve under every built-in theme.
/// <para>
/// WHY THIS CONTROL EXISTS. <c>TaskbarIcon.ShowBalloonTip</c> is the shell balloon and it always
/// plays the system notification sound; <c>BalloonFlags.NoSound</c> is reachable only through a
/// non-public overload of the shipped Hardcodet.NotifyIcon.Wpf 2.0.1 (verified by reflection
/// 2026-10-05). <c>ShowCustomBalloon</c> is public, draws no OS chrome, and therefore plays no OS
/// sound — which is what makes the sound a setting in item 5 instead of Windows' decision.
/// </para>
/// <para>
/// WHY CLICK-TO-FOCUS IS HERE RATHER THAN IN THE TRAY. <c>TrayService</c> needs a real
/// <c>TaskbarIcon</c> and cannot be constructed in a test, which is exactly how the regression this
/// item fixes went unnoticed: item 3 routed every balloon through <c>ShowToast</c>, which cleared
/// the remembered memory-warning account by design, and a memory-warning click silently stopped
/// doing anything with no test going red. Putting the account ON the control moves the behaviour
/// somewhere a test can reach it — the control is a <see cref="UserControl"/>, and
/// <see cref="Sta"/> gives it the STA thread WPF needs.
/// </para>
/// <para>
/// WHAT STILL NEEDS EYES, stated so a green run is not mistaken for a verdict: whether the balloon
/// looks right, where it appears, whether it fades, how it reads at 125% scaling, and whether it
/// paints over an exclusive-fullscreen Roblox client. Those are C2's and item 11's.
/// </para>
/// </summary>
public class AlertBalloonTests
{
    private static readonly PayloadLimits Toast = PayloadLimits.For(AlertDestination.Local);

    /// <summary>
    /// Builds the control on a fresh STA thread and runs <paramref name="body"/> against it there.
    /// WPF affinitises a <c>DispatcherObject</c> to its creating thread, so the construction and
    /// the assertion have to happen inside one <see cref="Sta.Run"/>.
    /// </summary>
    private static T OnBalloon<T>(string what, Func<AlertBalloon, T> body) =>
        Sta.Run(() => body(new AlertBalloon()), what);

    /// <summary>
    /// A real <c>MouseLeftButtonUp</c> on the surface, not a call to the handler. The wiring IS the
    /// deliverable — a handler that works and a markup attribute that does not reference it is
    /// precisely the shape of the bug this item fixes — so the event has to travel the route a
    /// user's click travels.
    /// </summary>
    private static void ClickTheSurface(AlertBalloon balloon)
    {
        var surface = (UIElement)balloon.FindName("Surface")!;
        surface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
        });
    }

    [Fact]
    public void AClickOnABalloonThatNamesOneAccount_AsksForThatAccount()
    {
        var id = Guid.NewGuid();

        var focused = OnBalloon(nameof(AClickOnABalloonThatNamesOneAccount_AsksForThatAccount), balloon =>
        {
            var seen = new List<Guid>();
            balloon.AccountId = id;
            balloon.FocusRequested += (_, a) => seen.Add(a);
            ClickTheSurface(balloon);
            return seen;
        });

        Assert.Equal(id, Assert.Single(focused));
    }

    [Fact]
    public void AClickOnABalloonThatNamesNoAccount_AsksForNothing()
    {
        // The honest half of the generalisation. A coalesced group of three accounts arrives with
        // no id because there is no single row to jump to, and a balloon like that must stay inert
        // rather than guess. Surfacing the window and highlighting an arbitrary one of the three is
        // worse than doing nothing: it is wrong and it looks deliberate.
        var fired = OnBalloon(nameof(AClickOnABalloonThatNamesNoAccount_AsksForNothing), balloon =>
        {
            var count = 0;
            balloon.AccountId = null;
            balloon.FocusRequested += (_, _) => count++;
            ClickTheSurface(balloon);
            return count;
        });

        Assert.Equal(0, fired);
    }

    [Fact]
    public void OnlyABalloonThatNamesAnAccount_LooksClickable()
    {
        // The cursor is the whole affordance (no hint line — see the markup's note on why), so it
        // has to track the one thing that decides whether a click does anything.
        var (clickable, inert) = Sta.Run(() =>
        {
            var withId = new AlertBalloon { AccountId = Guid.NewGuid() };
            var without = new AlertBalloon { AccountId = null };
            return (
                ((FrameworkElement)withId.FindName("Surface")!).Cursor,
                ((FrameworkElement)without.FindName("Surface")!).Cursor);
        }, nameof(OnlyABalloonThatNamesAnAccount_LooksClickable));

        Assert.Equal(Cursors.Hand, clickable);
        Assert.NotEqual(Cursors.Hand, inert);
    }

    [Fact]
    public void TheBalloonHoldsTheDesktopEnvelope_WhateverItIsHanded()
    {
        // 63/255 used to be enforced by the shell, which cut silently. We draw it now, so nothing
        // cuts unless the control does — and an unclamped 4,000-character body is a notification
        // sized by its content rather than by its envelope. PayloadLimits.Clamp* owns the cut; this
        // asserts the control actually applies it, because a clamp nobody calls is decoration.
        var (title, body) = OnBalloon(nameof(TheBalloonHoldsTheDesktopEnvelope_WhateverItIsHanded), balloon =>
        {
            balloon.Title = new string('x', 400);
            balloon.Body = new string('y', 4000);
            return (balloon.Title, balloon.Body);
        });

        Assert.Equal(Toast.Title, title.Length);
        Assert.Equal(Toast.Body, body.Length);
        Assert.EndsWith("…", title, StringComparison.Ordinal);
        Assert.EndsWith("…", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinaryAlertsWordingIsLeftAlone()
    {
        // The vacuity guard on the clause above: a clamp that truncated everything would satisfy it
        // too. A real routed payload must come through the control unchanged.
        var payload = WebhookPayload.ForAlert(
            AlertKind.AccountDroppedOut,
            [new AlertTrigger(AlertKind.AccountDroppedOut, Guid.NewGuid(), "BaronBloxwell",
                "real_BaronBloxwell", "Pet Simulator 99!", null, DateTimeOffset.UtcNow)],
            limits: Toast);

        var (title, body) = OnBalloon(nameof(AnOrdinaryAlertsWordingIsLeftAlone), balloon =>
        {
            balloon.Title = payload.Title;
            balloon.Body = payload.Body;
            return (balloon.Title, balloon.Body);
        });

        Assert.Equal(payload.Title, title);
        Assert.Equal(payload.Body, body);
        Assert.DoesNotContain("…", title, StringComparison.Ordinal);
    }

    /// <summary>
    /// The app's real built-in themes, same way <c>ContrastPairGateTests</c> reaches them: the real
    /// <see cref="ThemeStore"/> pointed at a throwaway folder, so a user theme sitting in
    /// <c>%LOCALAPPDATA%</c> on a dev box cannot contaminate the result.
    /// </summary>
    public static TheoryData<string> BuiltInThemeIds()
    {
        var data = new TheoryData<string>();
        foreach (var theme in BuiltInThemes()) data.Add(theme.Id);
        return data;
    }

    private static IReadOnlyList<Theme> BuiltInThemes()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-balloon-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn)
            .ToList();

        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
        catch (IOException) { }

        Assert.True(themes.Count >= 4,
            $"Expected at least the 4 built-in themes (brand, midnight, magenta-heat, flatline); got {themes.Count}.");

        return themes;
    }

    /// <summary><c>#RRGGBB</c> for a resolved brush, the form <c>ContrastGuard</c> parses.</summary>
    private static string Hex(System.Windows.Media.Brush? brush)
    {
        var c = Assert.IsType<System.Windows.Media.SolidColorBrush>(brush).Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    [Theory]
    [MemberData(nameof(BuiltInThemeIds))]
    public void TheBalloonResolvesEveryBrushItNamesUnderEveryBuiltInTheme(string themeId)
    {
        // "DynamicResource brushes only, and it follows all four themes" is item 4's first
        // criterion, and it is the kind of claim that is true when written and false two cycles
        // later. So: build the real control under each built-in theme's real dictionary, lay it
        // out, and read back the brush EVERY named slot actually resolved to.
        //
        // READ OFF THE LIVE TREE, not off the bitmap, and that correction came from a mutation
        // check rather than from reasoning. The first version asserted ThemedRender's
        // Sample.ForegroundRatio >= 4.5, which reads "the rendered colour FURTHEST from the fill by
        // contrast" — so with the body text mutated to DividerBrush (a hairline on RowBg, nowhere
        // near AA) the clause stayed GREEN, because the most-contrasty pixel in the bitmap was the
        // balloon's own border. It was measuring the edge and reporting on the text. Per-element
        // brushes cannot be confused for each other, and they are immune to the glyph
        // antialiasing that makes an exact colour match unreliable at 12px.
        //
        // WHAT IT CANNOT SEE: the fade, the position, the sound, how it reads at 125% scaling,
        // whether it paints over fullscreen Roblox. Arithmetic is not a pair of eyes.
        var theme = BuiltInThemes().Single(t => t.Id == themeId);
        var slots = ThemedRender.Resources(theme);

        var resolved = Sta.Run(() =>
        {
            var dict = ThemedRender.Resources(theme);
            var balloon = new AlertBalloon
            {
                Title = "BaronBloxwell — memory warning",
                Body = "• BaronBloxwell — 3.1 GB private bytes, over the 2.5 GB cap",
            };
            var host = new Border { Resources = dict, Child = balloon };
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();
            // DynamicResource invalidation is QUEUED, not immediate — reading before this drains
            // measures App.xaml's pre-startup fallback and reports a confident wrong colour, which
            // is the exact failure mode this test exists to rule out rather than introduce.
            Sta.DrainQueue();

            var surface = (Border)balloon.FindName("Surface")!;
            return (
                Fill: Hex(surface.Background),
                Edge: Hex(surface.BorderBrush),
                TitleFg: Hex(((TextBlock)balloon.FindName("TitleText")!).Foreground),
                BodyFg: Hex(((TextBlock)balloon.FindName("BodyText")!).Foreground),
                Size: host.DesiredSize);
        }, $"alert-balloon/{themeId}");

        Assert.True(resolved.Size.Width > 1 && resolved.Size.Height > 1,
            $"The balloon measured {resolved.Size.Width}x{resolved.Size.Height} under '{themeId}'. "
            + "A zero-sized notification is one nobody sees, and every colour assertion below would "
            + "pass on it.");

        // Every slot the markup names, against the value ThemeService.ApplyTo resolved for this
        // theme. A StaticResource frozen at App.xaml's pre-startup value would hold brand's
        // #15263A / #FFFFFF / #9AA8B8 under all four, which these equalities catch for the three
        // that differ; an unresolved key leaves the property null and Hex's type assert fires.
        Assert.Equal(ThemedRender.Slot(slots, ThemeSlots.RowBg), resolved.Fill);
        Assert.Equal(ThemedRender.Slot(slots, ThemeSlots.InteractiveEdge), resolved.Edge);
        Assert.Equal(ThemedRender.Slot(slots, ThemeSlots.White), resolved.TitleFg);
        Assert.Equal(ThemedRender.Slot(slots, ThemeSlots.MutedText), resolved.BodyFg);

        // And both are legible on it — AA for body text, the bar ContrastPairGateTests holds the
        // rest of the app to. (RowBg, MutedText) is in that gate's NamedPairs, so the body line is
        // a second reading of an already-watched pair; the title line is not watched anywhere else,
        // because nothing in the app declares White on RowBg inline.
        foreach (var (what, colour) in new[] { ("title", resolved.TitleFg), ("body", resolved.BodyFg) })
        {
            var ratio = ContrastGuard.RatioBetween(resolved.Fill, colour);
            Assert.True(ratio >= 4.5,
                $"The balloon's {what} measured {ratio:0.00}:1 against its own surface in "
                + $"'{themeId}', under AA's 4.5:1. Fill {resolved.Fill}, text {colour}.");
        }
    }

    [Theory]
    [MemberData(nameof(BuiltInThemeIds))]
    public void TheBalloonActuallyPaintsItsOwnSurface(string themeId)
    {
        // The pixel half, and a narrow claim: the control draws, and the host does not show
        // through. ThemedRender paints its host an impossible magenta precisely so that
        // "showing through" reports as an obviously wrong colour rather than a plausible ratio —
        // stage 1 of that harness shipped a sample that was right by coincidence.
        var theme = BuiltInThemes().Single(t => t.Id == themeId);

        var sample = ThemedRender.Measure(theme, $"alert-balloon-pixels/{themeId}", _ =>
        {
            var balloon = new AlertBalloon
            {
                Title = "BaronBloxwell — memory warning",
                Body = "• BaronBloxwell — 3.1 GB private bytes, over the 2.5 GB cap",
            };
            Sta.DrainQueue();
            return balloon;
        });

        Assert.False(sample.SentinelLeaked,
            $"The host showed through in '{themeId}' — the sample measured the harness, not the "
            + $"balloon. Histogram:\n{sample.Describe()}");

        Assert.Equal(ThemedRender.Slot(ThemedRender.Resources(theme), ThemeSlots.RowBg), sample.Fill);
    }

    [Fact]
    public void TheMarkupNamesNoStaticResourceAndNoColourOfItsOwn()
    {
        // The render gate above proves what resolves TODAY; this proves the rule the markup was
        // written to. A StaticResource and a raw hex both render correctly on first paint and then
        // survive every theme change unchanged, which is the one defect a single-theme eye test
        // cannot see. ThemedStatusColourTests holds the same line app-wide for XAML hex; this is
        // the StaticResource half, scoped to the one control that has no page to inherit from.
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "src", "ROROROblox.App", "Tray", "AlertBalloon.xaml");

        // COMMENTS STRIPPED, borrowing ThemedStatusColourTests.StripXmlComment rather than copying
        // it. This is the third time in this repo a fence has read prose as code, and this one
        // caught itself doing it: the markup's own note explains WHY a StaticResource would be
        // wrong here, and the first version of this assertion read that explanation as the crime.
        var inComment = false;
        var code = new List<string>();
        foreach (var line in File.ReadAllLines(path))
        {
            code.Add(ThemedStatusColourTests.StripXmlComment(line, ref inComment));
        }
        var markup = string.Join('\n', code);

        Assert.DoesNotContain("StaticResource", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("#", markup, StringComparison.Ordinal);

        // Vacuity floor: the file has to actually be naming themed brushes for the two absences
        // above to mean anything.
        var dynamicRefs = markup.Split("{DynamicResource").Length - 1;
        Assert.True(dynamicRefs >= 6,
            $"AlertBalloon.xaml names only {dynamicRefs} themed resources. Either the control was "
            + "gutted or this test is reading the wrong file; both absences above pass on an empty "
            + "string.");
    }
}
