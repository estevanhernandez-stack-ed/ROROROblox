using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Opt-in capture of the account row with the v1.36 facts line, for the screenshot acceptance box of
/// item 6: brand, midnight, magenta-heat and flatline, at 96 and 120 DPI, three rows each (facts
/// populated, one failed-refresh row, one empty row that must look like v1.35). Writes PNGs under
/// <c>docs/cycles/2026-11-01-account-library/mocks/compare/</c>.
/// <para>
/// OFF BY DEFAULT. The normal suite must not write files into the repo, so this is skipped unless
/// <c>RORORO_CAPTURE_ROWS=1</c> is set:
/// <c>$env:RORORO_CAPTURE_ROWS=1; dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter FullyQualifiedName~AccountRowFactsCaptureTests</c>.
/// Account names are fake on purpose.
/// </para>
/// <para>
/// Like <see cref="AccountRowCollapsedRenderTests"/>, layout runs once at 96 and the bitmap is
/// rendered at the requested DPI; a real 125% display re-runs layout and is not reproduced here.
/// </para>
/// </summary>
public class AccountRowFactsCaptureTests
{
    private sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_CAPTURE_ROWS") != "1")
            {
                Skip = "Opt-in capture: set RORORO_CAPTURE_ROWS=1 to write the item-6 row PNGs under docs/cycles/.../mocks/compare/.";
            }
        }
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public AccountRowFactsCaptureTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static string CompareDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var target = Path.Combine(dir!, "docs", "cycles", "2026-11-01-account-library", "mocks", "compare");
        Directory.CreateDirectory(target);
        return target;
    }

    private static Window BuildWindow()
    {
        var built = MainViewModelTests.Build();
        var now = DateTimeOffset.UtcNow;

        Account Make(string name, AccountFacts? facts, string? note, bool main = false) => new(
            Id: Guid.NewGuid(), DisplayName: name, AvatarUrl: "", CreatedAt: now, LastLaunchedAt: null,
            IsMain: main, Facts: facts, Note: note);

        var populated = new AccountSummary(Make("NovaFake", new AccountFacts(1240, null, 3, now.AddMinutes(-3), null),
            "Farm alt, keep at 20 FPS", main: true));
        var failed = new AccountSummary(Make("PetFake07", new AccountFacts(85, null, 0, now.AddHours(-2), "403"), null));
        var empty = new AccountSummary(Make("AltFake", null, null));

        foreach (var (row, game, hoursAgo) in new[] { (populated, "Pet Simulator 99", 2), (failed, "Grow a Garden", 24) })
        {
            var panel = new AccountPanelViewModel(
                row,
                () => Task.FromResult<IReadOnlyList<LaunchSession>>([]),
                () => Task.FromResult<IReadOnlyList<LibraryEntry>>([]),
                _ => Task.CompletedTask,
                () => Task.CompletedTask);
            row.AttachPanel(panel);
            panel.PrimeLastPlayed([new LaunchSession(Guid.NewGuid(), row.Id, "Alt", null, game, 1L, false,
                now.AddHours(-hoursAgo), now.AddHours(-hoursAgo + 1), null)]);
        }

        built.Vm.Accounts.Add(populated);
        built.Vm.Accounts.Add(failed);
        built.Vm.Accounts.Add(empty);
        return new ROROROblox.App.MainWindow(built.Vm);
    }

    private static byte[] Render(Theme theme, double dpi) =>
        ThemedWindowRender.Inspect(
            theme,
            $"account rows with facts [{theme.Id}] @{dpi}dpi",
            BuildWindow,
            content =>
            {
                var firstRow = ThemedWindowRender.Find(
                    content, fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0, "AccountRowBorder");
                DependencyObject? node = firstRow;
                while (node is not null and not ItemsControl)
                {
                    node = VisualTreeHelper.GetParent(node);
                }

                var roster = (FrameworkElement)(node ?? throw new InvalidOperationException("no ItemsControl above the row"));
                var ground = content.TryFindResource("BgBrush") as Brush
                    ?? throw new InvalidOperationException("BgBrush did not resolve; refusing to paint a white PNG.");
                var scale = dpi / 96.0;
                var w = (int)Math.Round(roster.ActualWidth * scale);
                var h = (int)Math.Round(roster.ActualHeight * scale);

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(ground, null, new Rect(0, 0, roster.ActualWidth, roster.ActualHeight));
                    dc.DrawRectangle(
                        new VisualBrush(roster) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                        null, new Rect(0, 0, roster.ActualWidth, roster.ActualHeight));
                }

                var bitmap = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            });

    [OptInFact]
    public void WriteTheRowCaptures_InEveryBuiltInTheme_At96And120()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-facts-capture-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn && t.Id is "brand" or "midnight" or "magenta-heat" or "flatline").ToList();
        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch (IOException) { }
        Assert.Equal(4, themes.Count);

        var dir = CompareDir();
        foreach (var theme in themes)
        {
            foreach (var dpi in new[] { 96d, 120d })
            {
                var png = Render(theme, dpi);
                var path = Path.Combine(dir, $"item6-facts-row-{theme.Id}-{(int)dpi}dpi.png");
                File.WriteAllBytes(path, png);
                _output.WriteLine($"{theme.Id} @{dpi}: {path} ({png.Length} bytes)");
            }
        }
    }
}
