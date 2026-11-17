using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Localization;
using ROROROblox.App.Updates;
using ROROROblox.App.ViewModels;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The header's update button, drawn by the real <c>MainWindow</c> through <see cref="ThemedWindowRender"/>.
/// <para>
/// Two things here. The always-on gates: the button is absent with no update, sits at the LEFT end of
/// the header group before Games, and in every shipped language the group still clears the centred
/// default-game widget at the declared window width. And an opt-in capture
/// (<c>RORORO_CAPTURE_UPDATE_BUTTON=1</c>) that writes the PNGs for the owner's review under
/// <c>docs/cart-runs/2026-11-17-update-button/captures/</c>.
/// </para>
/// </summary>
[Collection("MutatesUiCulture")]
public class UpdateButtonRenderTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public UpdateButtonRenderTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private sealed class YesProbe : IStoreUpdateProbe
    {
        public Task<bool> IsUpdateWaitingAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    public enum State { None, Store, Staged }

    /// <summary>Applies a state to a view model: set before assigning, so no cross-thread notify is involved.</summary>
    private static void Apply(MainViewModel vm, State state)
    {
        switch (state)
        {
            case State.Store:
                var store = new UpdateAvailability(true, new YesProbe(), new FakeTimeProvider());
                store.CheckNowAsync().GetAwaiter().GetResult();
                vm.UpdateAvailability = store;
                break;
            case State.Staged:
                var staged = new UpdateAvailability(false, new YesProbe(), new FakeTimeProvider());
                staged.ReportStaged("1.40.0");
                vm.UpdateAvailability = staged;
                break;
        }
    }

    private static Window BuildWindow(State state) =>
        PanelWindowFixtures.Build(accounts: 1, expanded: false, savedGames: true, configureVm: vm => Apply(vm, state));

    private sealed record Geometry(
        bool UpdateShown, double UpdateLeft, double GamesLeft, double UpdateWidth, double GroupRight,
        double CentreLeft, double CentreWidth, double CentreDesiredWidth, double RoomForCentre);

    private Geometry Measure(State state, string culture, string themeId = "brand")
    {
        var previous = TranslationSource.Instance.CurrentCulture;
        TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
        try
        {
            return ThemedWindowRender.Inspect(
                MainWindowOneAccountFitsTests.BuiltIn(themeId),
                $"MainWindow header, update {state}, {culture}",
                () => BuildWindow(state),
                root =>
                {
                    var update = PanelWindowFixtures.ByAutomationId(root, "ToolbarUpdate");
                    var games = PanelWindowFixtures.ByAutomationId(root, "ToolbarGames");
                    var group = (FrameworkElement)games.Parent;
                    var header = (Grid)group.Parent;
                    var centre = header.Children.OfType<FrameworkElement>().Single(c => Grid.GetColumn(c) == 1);
                    double X(FrameworkElement fe) => fe.TransformToAncestor(header).Transform(new Point(0, 0)).X;
                    return new Geometry(
                        PanelWindowFixtures.EffectivelyVisible(update) && update.ActualWidth > 0,
                        X(update), X(games), update.ActualWidth, X(group) + group.ActualWidth,
                        X(centre), centre.ActualWidth, centre.DesiredSize.Width,
                        // Room the middle column really has: the header's width less the right-hand
                        // column and the left group. The default-game widget grows to 340 when a
                        // long game name is chosen; this is what it would have to grow into.
                        header.ActualWidth - header.Children.OfType<FrameworkElement>().Single(c => Grid.GetColumn(c) == 2).ActualWidth
                            - (X(group) + group.ActualWidth));
                });
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }
    }

    [WindowRenderFact]
    public void WithNoUpdate_TheButtonIsNotInTheHeaderAtAll()
    {
        var g = Measure(State.None, "en");

        Assert.False(g.UpdateShown);
    }

    [WindowRenderTheory]
    [InlineData(State.Store)]
    [InlineData(State.Staged)]
    public void WithAnUpdate_TheButtonIsAtTheLeftEnd_BeforeGames(State state)
    {
        var g = Measure(state, "en");
        _output.WriteLine($"{state}: update left {g.UpdateLeft:F1}, width {g.UpdateWidth:F1}, games left {g.GamesLeft:F1}");

        Assert.True(g.UpdateShown);
        Assert.Equal(0, g.UpdateLeft, 0.5);
        Assert.True(g.GamesLeft >= g.UpdateLeft + g.UpdateWidth, "Games must sit after the update button, not under it.");
    }

    /// <summary>
    /// Length is the risk the translations carry: German and Russian run long. The header's centre column holds the
    /// default-game widget (MinWidth 220) and is the first thing a long left group squeezes. At the declared width, in
    /// every language and both labels, the group must end before that column starts and the column must keep its width.
    /// </summary>
    [WindowRenderTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("pl")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    public void InEveryLanguage_TheHeaderGroupStillClearsTheCentreColumn(string culture)
    {
        foreach (var state in new[] { State.Store, State.Staged })
        {
            var g = Measure(state, culture);
            _output.WriteLine(
                $"{culture} {state}: button {g.UpdateWidth:F1} wide, group right {g.GroupRight:F1}, centre left {g.CentreLeft:F1}, "
                + $"centre {g.CentreWidth:F1} of {g.CentreDesiredWidth:F1} wanted, room for it {g.RoomForCentre:F1}");

            Assert.True(g.GroupRight <= g.CentreLeft + 0.5,
                $"{culture}/{state}: the header's button group ends at {g.GroupRight:F1} but the centre column starts at {g.CentreLeft:F1}.");
            Assert.True(g.RoomForCentre >= 340,
                $"{culture}/{state}: only {g.RoomForCentre:F1} DIP is left for the default-game widget, which grows to 340 for a long game name.");
            Assert.True(g.CentreWidth + 0.5 >= g.CentreDesiredWidth,
                $"{culture}/{state}: the centre column is squeezed to {g.CentreWidth:F1} of the {g.CentreDesiredWidth:F1} it wants.");
        }
    }

    // ---- the opt-in capture ----

    private sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_CAPTURE_UPDATE_BUTTON") != "1")
            {
                Skip = "Opt-in capture: set RORORO_CAPTURE_UPDATE_BUTTON=1 to write the PNGs under docs/cart-runs/2026-11-17-update-button/captures/.";
            }
        }
    }

    private static string CaptureDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var target = Path.Combine(dir!, "docs", "cart-runs", "2026-11-17-update-button", "captures");
        Directory.CreateDirectory(target);
        return target;
    }

    /// <summary>The header band: the Border above the roster that holds the title row and the button row.</summary>
    private static FrameworkElement HeaderBand(FrameworkElement root)
    {
        DependencyObject? node = PanelWindowFixtures.ByAutomationId(root, "ToolbarGames");
        while (node is not null)
        {
            if (node is Border b && b.Padding.Left == 24 && b.Padding.Top == 18) return b;
            node = VisualTreeHelper.GetParent(node);
        }

        throw new InvalidOperationException("The header band was not found above the Games button.");
    }

    private static byte[] Capture(Theme theme, double dpi, State state, string culture)
    {
        var previous = TranslationSource.Instance.CurrentCulture;
        TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
        try
        {
            return ThemedWindowRender.Inspect(
                theme,
                $"update button capture [{theme.Id}] {state} {culture} @{dpi}dpi",
                () => BuildWindow(state),
                root =>
                {
                    var target = HeaderBand(root);
                    var ground = root.TryFindResource("BgBrush") as Brush
                        ?? throw new InvalidOperationException("BgBrush did not resolve; refusing to paint a white PNG.");
                    var scale = dpi / 96.0;
                    var w = (int)Math.Round(target.ActualWidth * scale);
                    var h = (int)Math.Round(target.ActualHeight * scale);

                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(ground, null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                        dc.DrawRectangle(
                            new VisualBrush(target) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                            null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                    }

                    var bitmap = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    return stream.ToArray();
                });
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }
    }

    private void Write(string name, byte[] png)
    {
        var path = Path.Combine(CaptureDir(), name);
        File.WriteAllBytes(path, png);
        _output.WriteLine($"{path} ({png.Length} bytes)");
    }

    [OptInFact]
    public void WriteTheHeaderCaptures()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-update-capture-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn && t.Id is "brand" or "midnight").ToList();
        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch (IOException) { }
        Assert.Equal(2, themes.Count);

        foreach (var theme in themes)
        {
            foreach (var dpi in new[] { 96d, 120d })
            {
                foreach (var state in new[] { State.Store, State.Staged })
                {
                    Write($"header-{state.ToString().ToLowerInvariant()}-{theme.Id}-{(int)dpi}dpi.png", Capture(theme, dpi, state, "en"));
                }
            }
        }

        // The no-update header, for the before side of the pair.
        Write("header-none-brand-96dpi.png", Capture(themes.Single(t => t.Id == "brand"), 96, State.None, "en"));

        // The longest labels, in the two languages that cost the most width.
        var brand = themes.Single(t => t.Id == "brand");
        foreach (var culture in new[] { "de", "ru" })
        {
            Write($"header-store-brand-96dpi-{culture}.png", Capture(brand, 96, State.Store, culture));
            Write($"header-staged-brand-96dpi-{culture}.png", Capture(brand, 96, State.Staged, culture));
        }
    }
}
