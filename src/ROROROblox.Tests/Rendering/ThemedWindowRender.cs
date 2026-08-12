using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// A rendered region of a real app window: the raw pixels, and a hash of them.
/// </summary>
/// <param name="Hash">SHA-256 over the region's BGRA bytes. Two themes producing the same hash
/// rendered byte-identically — which is a claim no eye comparing screenshots can make.</param>
internal sealed record RegionSample(
    string Hash,
    int Width,
    int Height,
    IReadOnlyList<(string Colour, int Count)> Histogram)
{
    public string Describe() => string.Join("\n", Histogram.Take(8).Select(h => $"      {h.Colour} x{h.Count}"));
}

/// <summary>
/// Renders a REAL app <see cref="Window"/> offscreen under a theme, and samples a region of it.
/// <para>
/// <c>ThemedRender</c> renders synthetic controls built from a keyed style — exactly right for
/// proving a rank behaves in every theme and state, and structurally unable to answer anything
/// about a real window's real content. This is the window-level sibling. It reuses
/// <c>ThemedRender.Resources</c> unchanged rather than growing a second dictionary builder, because
/// shipping a fix into one scanner and missing its copy in another is a mistake this repo has
/// already made twice. It cannot reuse <c>Sta</c> — see point 1.
/// </para>
/// <para>
/// FOUR THINGS THE DESIGN GUESSED AND THE SPIKE SETTLED, recorded so nobody re-derives them. The
/// first one killed the design as written.
/// </para>
/// <list type="number">
/// <item><b>An <see cref="Application"/> is unavoidable, and the design said otherwise.</b> The
/// first draft asserted the theme dictionary could simply be merged into the finished window,
/// because <c>DynamicResource</c> resolves element → window → application. It cannot: window markup
/// takes app styles with <c>{StaticResource}</c>, which resolves at PARSE time inside
/// <c>InitializeComponent()</c>, so the window throws
/// <c>Cannot find resource named &apos;SecondaryStrongButtonStyle&apos;</c> and never exists to be
/// merged into. 26 App XAML files do this. See <see cref="WindowRenderHost"/> for how the hazard
/// <c>ThemedRender</c> names is avoided — theme-independent styles on the Application, theme brushes
/// on the window.</item>
/// <item><b>The theme dictionary is still MERGED into the window, never assigned.</b>
/// <c>AboutWindow</c> declares its own <c>Window.Resources</c> holding the mark&apos;s artwork
/// brushes; assigning would delete them and the gate would be measuring a window that cannot draw
/// its own logo.</item>
/// <item><b>The window is never shown, and the CONTENT is what gets rendered.</b> A never-shown
/// Window has no arranged visual, so rendering the Window samples nothing. Rendering
/// <c>window.Content</c> works because the content&apos;s logical parent is still the Window, so
/// resource lookup walks up into the dictionary merged above.</item>
/// <item><b>No sentinel host.</b> <c>ThemedRender</c> wraps its control in a magenta
/// <see cref="System.Windows.Controls.Border"/> so host-showing-through is obvious. Doing that here
/// would mean reparenting the content out from under its Window, which breaks the resource lookup
/// point 2 sets up. <see cref="RenderRegion"/> fails loudly on a zero-sized or single-colour region
/// instead.</item>
/// </list>
/// </summary>
internal static class ThemedWindowRender
{
    /// <summary>
    /// Builds a window under <paramref name="theme"/>, arranges it offscreen, and renders the region
    /// <paramref name="select"/> picks out of its content.
    /// <para>
    /// <paramref name="select"/> receives the arranged content root and returns the element to
    /// sample. Returning the root itself samples the whole window.
    /// </para>
    /// </summary>
    public static RegionSample MeasureRegion(
        Theme theme,
        string what,
        Func<Window> build,
        Func<FrameworkElement, FrameworkElement> select,
        double dpi = 96)
        => WindowRenderHost.Run(() =>
        {
            // Resources FIRST so the pack:// factory is installed before the window parses its XAML.
            var dict = ThemedRender.Resources(theme);

            var window = build();

            // MERGE, never assign — see the class remarks. AboutWindow declares its own
            // Window.Resources holding the mark's eight brushes.
            window.Resources.MergedDictionaries.Add(dict);

            if (window.Content is not FrameworkElement content)
            {
                throw new InvalidOperationException(
                    $"'{what}' has Content of type {window.Content?.GetType().Name ?? "null"}, "
                    + "which cannot be measured. This harness renders the content, not the Window.");
            }

            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = content.DesiredSize;
            if (size.Width < 1 || size.Height < 1)
            {
                throw new InvalidOperationException(
                    $"'{what}' under theme '{theme.Id}' measured to {size.Width}x{size.Height}. "
                    + "A zero-sized visual renders nothing and would sample as a vacuous pass.");
            }

            content.Arrange(new Rect(size));
            content.UpdateLayout();

            // DynamicResource invalidation and template application are QUEUED. Sampling before this
            // drains measures the default setter rather than the applied value.
            WindowRenderHost.DrainQueue();

            var target = select(content);
            return RenderRegion(content, target, theme, what, dpi);
        }, what);

    /// <summary>
    /// Renders <paramref name="target"/> alone, through a <see cref="VisualBrush"/> drawn into a
    /// <see cref="DrawingVisual"/> at the origin.
    /// <para>
    /// THE SPIKE TRIED RENDER-THEN-CROP FIRST AND IT WAS WRONG IN A WAY WORTH RECORDING. The trap is
    /// usually stated as "rendering a child applies the child's layout offset", so cropping from
    /// the parent looks like the safe move. It is not: <c>RenderTargetBitmap.Render</c> does not
    /// apply the ROOT's own offset either, and <c>AboutWindow</c>'s content Grid carries
    /// <c>Margin="32,28"</c>. So the bitmap was shifted up-left by exactly that margin while
    /// <c>TransformToAncestor</c> reported unshifted coordinates, and the 64x64 crop landed mostly
    /// on transparent Grid — 2,944 of 4,096 sampled pixels were nothing at all, which still produced
    /// a stable per-theme hash and would have passed a weaker assertion.
    /// </para>
    /// <para>
    /// A <c>VisualBrush</c> of the target painted into a rect at (0,0) has no offset to get wrong in
    /// either direction, and needs no crop arithmetic.
    /// </para>
    /// </summary>
    private static RegionSample RenderRegion(
        FrameworkElement root, FrameworkElement target, Theme theme, string what, double dpi)
    {
        var scale = dpi / 96.0;
        var tw = (int)Math.Round(target.ActualWidth * scale);
        var th = (int)Math.Round(target.ActualHeight * scale);

        if (tw < 1 || th < 1)
        {
            throw new InvalidOperationException(
                $"'{what}' under '{theme.Id}': the selected region measured {tw}x{th} device px. "
                + "Nothing to sample.");
        }

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(
                new VisualBrush(target) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null,
                new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        }

        var bmp = new RenderTargetBitmap(tw, th, dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(dv);

        var region = new byte[tw * th * 4];
        bmp.CopyPixels(region, tw * 4, 0);

        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < region.Length; i += 4)
        {
            var key = $"#{region[i + 2]:X2}{region[i + 1]:X2}{region[i]:X2}";
            histogram[key] = histogram.GetValueOrDefault(key) + 1;
        }

        var ordered = histogram.OrderByDescending(kv => kv.Value)
                               .Select(kv => (Colour: kv.Key, kv.Value))
                               .ToList();

        // A region that came out one flat colour is the shape a broken render takes: the visual
        // never arrived and the bitmap is the cleared surface. Every region this harness is pointed
        // at draws something.
        if (ordered.Count < 2)
        {
            throw new InvalidOperationException(
                $"'{what}' under '{theme.Id}': the region rendered a single colour "
                + $"({ordered[0].Colour} x{ordered[0].Value}). That is what an unarranged or "
                + "unresolved visual looks like, not a mark.");
        }

        return new RegionSample(
            Convert.ToHexString(SHA256.HashData(region)),
            tw,
            th,
            ordered);
    }

    /// <summary>
    /// Depth-first visual-tree search for the first element matching <paramref name="match"/>.
    /// Throws rather than returning null: a selector that finds nothing would render the wrong
    /// region or crop to zero, and both of those read as a passing test.
    /// </summary>
    public static FrameworkElement Find(
        FrameworkElement root, Func<FrameworkElement, bool> match, string described)
    {
        return Walk(root)
            ?? throw new InvalidOperationException(
                $"No element matching '{described}' in the arranged tree. The selector is measuring "
                + "nothing, which would pass vacuously.");

        FrameworkElement? Walk(DependencyObject node)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is FrameworkElement fe && match(fe)) return fe;
                if (Walk(child) is { } deeper) return deeper;
            }

            return null;
        }
    }
}
