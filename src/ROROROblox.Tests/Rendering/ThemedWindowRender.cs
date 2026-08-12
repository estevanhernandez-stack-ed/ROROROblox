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
        double dpi = 96,
        bool raiseLoaded = false)
        => WindowRenderHost.Run(() =>
        {
            var content = Arrange(theme, what, build, raiseLoaded);
            return RenderRegion(content, select(content), theme, what, dpi);
        }, what);

    /// <summary>
    /// Arranges a window under <paramref name="theme"/> and hands the laid-out content tree to
    /// <paramref name="inspect"/>, without rendering a bitmap.
    /// <para>
    /// For the questions a bitmap answers badly. "Do three rows separate, and by how much" is about
    /// WHERE things landed; sampling colours to find that out would mean inferring geometry from a
    /// histogram, and a row separated by a gap the same colour as the row is invisible to a colour
    /// sample while being perfectly measurable as a rectangle. <c>Sample.BoundsOf</c> exists on the
    /// control harness for the same reason, added after a human found two geometry defects every
    /// colour assertion had passed over.
    /// </para>
    /// <para>
    /// <paramref name="inspect"/> must not return live visuals — they belong to the host thread and
    /// touching them from the calling thread throws. Return values (rectangles, counts, strings).
    /// </para>
    /// </summary>
    public static T Inspect<T>(
        Theme theme,
        string what,
        Func<Window> build,
        Func<FrameworkElement, T> inspect,
        bool raiseLoaded = false)
        => WindowRenderHost.Run(() => inspect(Arrange(theme, what, build, raiseLoaded)), what);

    /// <summary>
    /// Build → merge → (optionally) raise Loaded → measure → arrange → drain. The shared half of
    /// both entry points. Must run on the host thread.
    /// </summary>
    private static FrameworkElement Arrange(Theme theme, string what, Func<Window> build, bool raiseLoaded)
    {
        // Resources FIRST so the pack:// factory is installed before the window parses its XAML.
        var dict = ThemedRender.Resources(theme);

        var window = build();

        // MERGE, never assign — see the class remarks. AboutWindow declares its own
        // Window.Resources holding the mark's artwork brushes.
        window.Resources.MergedDictionaries.Add(dict);

        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException(
                $"'{what}' has Content of type {window.Content?.GetType().Name ?? "null"}, "
                + "which cannot be measured. This harness renders the content, not the Window.");
        }

        // MEASURED AT THE WINDOW'S OWN SIZE, NOT AT INFINITY, and the difference is not cosmetic.
        // An unconstrained measure means TextWrapping="Wrap" never wraps, so a window carrying long
        // prose reports a DesiredSize hundreds of times wider than it will ever be — and
        // RenderTargetBitmap then tries to allocate width x height x 4 bytes of it. AboutWindow is
        // NoResize at 500x460 and never showed this; MainWindow is 900x600 with wrapping banner
        // copy and wedged the render until each case hit the 60s budget. Arranging at the declared
        // size is also just more honest: it is the size the window actually opens at.
        var constraint = new Size(
            Bounded(window.Width, window.MinWidth, 1280),
            Bounded(window.Height, window.MinHeight, 900));

        content.Measure(constraint);
        var size = content.DesiredSize;
        if (size.Width < 1 || size.Height < 1)
        {
            throw new InvalidOperationException(
                $"'{what}' under theme '{theme.Id}' measured to {size.Width}x{size.Height}. "
                + "A zero-sized visual renders nothing and would sample as a vacuous pass.");
        }

        content.Arrange(new Rect(new Size(
            Math.Min(size.Width, constraint.Width),
            Math.Min(size.Height, constraint.Height))));
        content.UpdateLayout();

        // A window that is never shown never raises Loaded, so a window that builds its content
        // from a Loaded handler renders empty. Raised deliberately rather than by showing the
        // window, which would need a real HWND and would flash on screen.
        if (raiseLoaded)
        {
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, window));

            // The handler is typically `async void`; its continuation posts at Normal priority,
            // which is ABOVE Loaded, so draining to Loaded flushes it. A handler awaiting real I/O
            // would NOT be covered — callers assert their content arrived rather than trusting this.
            WindowRenderHost.DrainQueue();

            // Content built during Loaded has not been through a layout pass yet.
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            content.Arrange(new Rect(content.DesiredSize));
            content.UpdateLayout();
        }

        // DynamicResource invalidation and template application are QUEUED. Sampling before this
        // drains measures the default setter rather than the applied value.
        WindowRenderHost.DrainQueue();

        return content;
    }

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

    /// <summary>Declared size if the window has one, else its minimum, else a sane default.</summary>
    private static double Bounded(double declared, double minimum, double fallback) =>
        !double.IsNaN(declared) && declared > 0 ? declared
        : minimum > 0 ? minimum
        : fallback;

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
