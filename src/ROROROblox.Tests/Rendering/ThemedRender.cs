using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.Core.Theming;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// What a render actually put on screen: the three colours the contrast rules are about, plus the
/// histogram behind them so a surprising ratio can be diagnosed instead of just disbelieved.
/// </summary>
/// <param name="Fill">Modal non-sentinel colour — the control's own surface.</param>
/// <param name="Foreground">The rendered colour furthest from <paramref name="Fill"/> by contrast.</param>
/// <param name="Edge">Outermost non-sentinel colour on the vertical midline, or null if none drawn.</param>
/// <param name="SentinelLeaked">True when the host showed through, which invalidates the sample.</param>
internal sealed record Sample(
    string Fill,
    string Foreground,
    string? Edge,
    bool SentinelLeaked,
    IReadOnlyList<(string Colour, int Count)> Histogram,
    int Width,
    int Height,
    IReadOnlyDictionary<string, (int Left, int Top, int Right, int Bottom)> Bounds)
{
    public double? ForegroundRatio => ContrastGuard.RatioBetween(Fill, Foreground);
    public double? EdgeRatio => Edge is null ? null : ContrastGuard.RatioBetween(Fill, Edge);

    /// <summary>
    /// Where a colour actually landed, for the questions contrast cannot answer — is this dot
    /// centred, did that rule draw on the left edge. Added when a human found two geometry defects
    /// by eye that every colour assertion in this project passed straight over.
    /// </summary>
    public (int Left, int Top, int Right, int Bottom, int Width, int Height)? BoundsOf(string hex) =>
        Bounds.TryGetValue(hex, out var b)
            ? (b.Left, b.Top, b.Right, b.Bottom, Width, Height)
            : null;

    public string Describe() => string.Join("\n", Histogram.Take(6).Select(h => $"      {h.Colour} x{h.Count}"));
}

/// <summary>
/// Renders a themed control offscreen and reports the colours it actually produced.
/// <para>
/// This is the Phase 2 harness from the rendered-contrast-gate design (2026-08-09). Phase 1
/// (<c>ContrastPairGateTests</c>) asserts token arithmetic; this measures pixels, which is the only
/// thing that can contradict that arithmetic. A control template can override what a setter asks
/// for, a <c>DynamicResource</c> that fails to resolve falls back silently, and alpha composites —
/// none of which token math can see.
/// </para>
/// <para>
/// Three things the design assumed and stage-1 measurement corrected, recorded so nobody re-derives
/// them:
/// </para>
/// <list type="number">
/// <item>Pack URIs need <see cref="Application"/>'s static constructor, not
/// <c>PackUriHelper</c>'s. Registering the URI parser alone still throws
/// <c>NotSupportedException("The URI prefix is not recognized")</c>, because resolving
/// <c>pack://application:,,,</c> also needs the WebRequest factory that <c>Application</c> installs.
/// Reading <see cref="Application.Current"/> is enough and does not construct one.</item>
/// <item><c>ControlStyles.xaml</c> parses fine outside an <see cref="Application"/>, both merge
/// orders and both URI forms. App.xaml:12-15 warns that its <c>BasedOn="{StaticResource {x:Type
/// Button}}"</c> resolves at parse time and needs WPF-UI merged first — true, and satisfied here by
/// merging in the same order, but it is not the obstacle it looks like from the markup.</item>
/// <item>The design says "Fill — the bitmap's modal colour." That is only true when the control is
/// the visual ROOT. Rendering it on a themed host makes the modal colour the HOST, and the first
/// stage-1 pass reported a correct ratio for the wrong reason because the host happened to carry the
/// same brush as the button. Hence the sentinel below.</item>
/// </list>
/// <para>
/// NO <see cref="Application"/> instance is ever constructed. <c>ThemeService.Apply</c> reads
/// <c>Application.Current?.Resources</c>, so an instance here would be process-global state altering
/// how every other test in this assembly resolves a theme.
/// </para>
/// </summary>
internal static class ThemedRender
{
    /// <summary>
    /// Magenta the themes cannot produce. The host is painted with it so that host-showing-through
    /// reports as an obviously-wrong colour rather than as a plausible ratio. Stage 1 shipped a
    /// sample that was right by coincidence; this is what makes that coincidence visible.
    /// </summary>
    private const string Sentinel = "#FF00FF";

    /// <summary>
    /// Deliberately large. Glyph cores stay solid so antialiasing does not drag the foreground
    /// sample toward the fill. This gate measures colour, not layout, so the size is free.
    /// </summary>
    private const double GlyphSize = 48;

    private const string GlyphText = "MMMM";

    static ThemedRender()
    {
        // Refuse early and legibly if a RoRoRo instance is up, rather than throwing "The URI prefix
        // is not recognized" out of the ThemesDictionary ctor below, ~100 times, with nothing in the
        // message naming the cause. See RenderEnvironment for the measurements and F-105 for the
        // unsolved mechanism.
        RenderEnvironment.RequireClean();

        // Triggers Application's static ctor, which installs the pack WebRequest factory. Reading
        // the property does NOT create an Application — verified in stage 1, where Current stayed
        // null through the whole probe.
        //
        // NOTE: this is necessary but demonstrably not sufficient. Routing it through an explicit
        // Application instance instead was tried 2026-08-12 and changed nothing — same exception,
        // same line, and even WindowRenderHost's own tests failed, which have a real Application by
        // construction. So whatever a running RoRoRo breaks, it is not this registration.
        _ = Application.Current;
    }

    /// <summary>
    /// Builds a resource dictionary carrying WPF-UI's vocabulary, the app's keyed control styles,
    /// and <paramref name="theme"/>'s eleven brushes, resolved through the shipped
    /// <c>ThemeService.ApplyTo</c> seam rather than a reimplementation.
    /// </summary>
    public static ResourceDictionary Resources(Theme theme)
    {
        var dict = new ResourceDictionary();

        // Order matches App.xaml:10-15 and is load-bearing there. Kept here so the gate measures the
        // dictionary the app actually composes, not a convenient approximation of it.
        dict.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        dict.MergedDictionaries.Add(new ControlsDictionary());
        dict.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ROROROblox.App;component/Controls/ControlStyles.xaml", UriKind.Absolute),
        });

        // edgeAnswer: null is the real default — the user has not been asked about edge remediation,
        // which is the state that lets a built-in derive its edge. Same argument Phase 1 records.
        ROROROblox.App.Theming.ThemeService.ApplyTo(dict, theme, edgeAnswer: null);
        return dict;
    }

    /// <summary>Reads a resolved slot back as <c>#RRGGBB</c>, the form ContrastGuard parses.</summary>
    public static string Slot(ResourceDictionary dict, string key)
    {
        var c = ((SolidColorBrush)dict[key]).Color;
        return c.A == 255
            ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    /// <summary>
    /// Renders <paramref name="build"/>'s element as the visual root on a sentinel host and samples
    /// it. Runs on its own STA thread; the caller does not need to be on one.
    /// </summary>
    public static Sample Measure(Theme theme, string what, Func<ResourceDictionary, FrameworkElement> build, double dpi = 96)
        => Sta.Run(() =>
        {
            var dict = Resources(theme);
            var content = build(dict);

            var host = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Sentinel)!),
                Child = content,
                Resources = dict,
            };

            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = host.DesiredSize;
            if (size.Width < 1 || size.Height < 1)
            {
                throw new InvalidOperationException(
                    $"'{what}' under theme '{theme.Id}' measured to {size.Width}x{size.Height}. "
                    + "A zero-sized visual renders nothing and would sample as a vacuous pass.");
            }

            host.Arrange(new Rect(size));
            host.UpdateLayout();

            // DynamicResource invalidation and template application are QUEUED, not immediate.
            // Sampling before this drains measures the default setter rather than the applied
            // trigger — a confident wrong colour, which is the failure mode a pixel gate exists to
            // rule out rather than introduce.
            Sta.DrainQueue();

            var w = (int)Math.Ceiling(size.Width);
            var h = (int)Math.Ceiling(size.Height);
            // DPI is a parameter because two of this project's geometry defects only exist at
            // fractional scaling: an inset that rounds evenly at 96 splits unevenly at 120. A gate
            // that only ever renders at 96 cannot see the bug a user reported at 125%.
            var scale = dpi / 96.0;
            var pw = (int)Math.Ceiling(w * scale);
            var ph = (int)Math.Ceiling(h * scale);
            var bmp = new RenderTargetBitmap(pw, ph, dpi, dpi, PixelFormats.Pbgra32);
            bmp.Render(host);

            var stride = pw * 4;
            var px = new byte[stride * ph];
            bmp.CopyPixels(px, stride, 0);

            var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
            var bounds = new Dictionary<string, (int Left, int Top, int Right, int Bottom)>(StringComparer.Ordinal);
            for (var i = 0; i < px.Length; i += 4)
            {
                var key = $"#{px[i + 2]:X2}{px[i + 1]:X2}{px[i]:X2}";
                histogram[key] = histogram.GetValueOrDefault(key) + 1;

                var pixel = i / 4;
                var x = pixel % pw;
                var y = pixel / pw;
                bounds[key] = bounds.TryGetValue(key, out var b)
                    ? (Math.Min(b.Left, x), Math.Min(b.Top, y), Math.Max(b.Right, x), Math.Max(b.Bottom, y))
                    : (x, y, x, y);
            }

            var ordered = histogram.OrderByDescending(kv => kv.Value)
                                   .Select(kv => (Colour: kv.Key, kv.Value))
                                   .ToList();

            var fill = ordered.FirstOrDefault(c => c.Colour != Sentinel).Colour
                       ?? throw new InvalidOperationException(
                           $"'{what}' under theme '{theme.Id}' rendered nothing but the sentinel host.");

            var foreground = histogram.Keys
                .Where(c => c != Sentinel)
                .OrderByDescending(c => ContrastGuard.RatioBetween(fill, c) ?? 0)
                .First();

            // Walk in from the left edge on the vertical midline; the first non-sentinel pixel is
            // the outermost thing the control draws, which is its border when it has one.
            string? edge = null;
            var midRow = ph / 2;
            for (var x = 0; x < pw && edge is null; x++)
            {
                var o = midRow * stride + x * 4;
                var c = $"#{px[o + 2]:X2}{px[o + 1]:X2}{px[o]:X2}";
                if (c != Sentinel) edge = c;
            }

            return new Sample(
                fill,
                foreground,
                edge,
                SentinelLeaked: ordered.Take(3).Any(c => c.Colour == Sentinel),
                ordered,
                pw,
                ph,
                bounds);
        }, what);

    /// <summary>A control carrying a keyed style, sized so its glyphs sample cleanly.</summary>
    public static FrameworkElement Styled(ResourceDictionary dict, string styleKey) => dict[styleKey] switch
    {
        Style { TargetType: var t } s when t == typeof(Button) =>
            new Button { Content = GlyphText, FontSize = GlyphSize, Padding = new Thickness(24, 12, 24, 12), Style = s },
        Style { TargetType: var t } s when t == typeof(System.Windows.Controls.Primitives.ToggleButton) =>
            new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = GlyphText, FontSize = GlyphSize,
                Padding = new Thickness(24, 12, 24, 12), Style = s,
            },
        Style { TargetType: var t } s when t == typeof(TextBox) =>
            new TextBox { Text = GlyphText, FontSize = GlyphSize, Padding = new Thickness(24, 12, 24, 12), Style = s },
        Style { TargetType: var t } s when t == typeof(PasswordBox) =>
            new PasswordBox { Password = GlyphText, FontSize = GlyphSize, Padding = new Thickness(24, 12, 24, 12), Style = s },
        Style { TargetType: var t } s when t == typeof(TextBlock) =>
            new TextBlock { Text = GlyphText, FontSize = GlyphSize, Style = s },
        Style { TargetType: var t } s when t == typeof(Border) =>
            new Border { Width = 200, Height = 100, Style = s },
        var other => throw new NotSupportedException(
            $"'{styleKey}' resolved to {other?.GetType().Name ?? "null"}. Every keyed style in "
            + "ControlStyles.xaml must be constructible here, or the enumeration is measuring less "
            + "than it claims."),
    };
}
