using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Writes PNGs of the main window with the rating ask showing, for a person to look at.
/// <para>
/// OFF BY DEFAULT: the normal suite must not write files into the repo. Set
/// <c>RORORO_CAPTURE_RATING=1</c>:
/// <c>$env:RORORO_CAPTURE_RATING=1; dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter FullyQualifiedName~RatingAskCaptureTests</c>.
/// Every account and game name is made up. Layout runs at 96 DPI.
/// </para>
/// </summary>
[Collection("MutatesUiCulture")]
public class RatingAskCaptureTests
{
    private sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_CAPTURE_RATING") != "1")
            {
                Skip = "Opt-in capture: set RORORO_CAPTURE_RATING=1 to write the rating-ask PNGs under docs/cycles/2026-11-10-growth-and-quality/captures/.";
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
        var target = Path.Combine(dir!, "docs", "cycles", "2026-11-10-growth-and-quality", "captures");
        Directory.CreateDirectory(target);
        return target;
    }

    private static Theme ThemeNamed(string id)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-rating-capture-" + Guid.NewGuid().ToString("N"));
        var theme = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult().Single(t => t.IsBuiltIn && t.Id == id);
        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch (IOException) { }
        return theme;
    }

    private static RatingAsk DueAsk(string dir)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero));
        var ask = new RatingAsk(dir, InstallChannel.Store, time);
        for (var i = 0; i < RatingAsk.DaysNeeded; i++)
        {
            ask.RecordSuccessfulLaunch();
            time.Advance(TimeSpan.FromDays(1));
        }
        return ask;
    }

    private static byte[] Render(string themeId, string? culture, bool crash = false)
    {
        var stateDir = Path.Combine(Path.GetTempPath(), "rororo-rating-capture-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stateDir);
        var previous = TranslationSource.Instance.CurrentCulture;
        if (culture is not null) TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
        try
        {
            return ThemedWindowRender.Inspect(
                ThemeNamed(themeId),
                $"rating ask [{themeId}] {culture ?? "en"}",
                () =>
                {
                    var window = PanelWindowFixtures.Build(accounts: 3, expanded: false);
                    var vm = (MainViewModel)window.DataContext;
                    if (crash)
                    {
                        vm.PreviousExit = new LastExit(LastExitRecorder.Crash, "System.InvalidOperationException 0x80131509 made up", DateTimeOffset.UnixEpoch);
                    }
                    else
                    {
                        vm.RatingAsk = DueAsk(stateDir);
                    }
                    return window;
                },
                root =>
                {
                    var ground = root.TryFindResource("BgBrush") as Brush
                        ?? throw new InvalidOperationException("BgBrush did not resolve; refusing to paint a white PNG.");
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(ground, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                        dc.DrawRectangle(
                            new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                            null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                    }

                    var bitmap = new RenderTargetBitmap((int)Math.Round(root.ActualWidth), (int)Math.Round(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
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
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [OptInFact]
    public void WriteTheRatingAskCaptures()
    {
        foreach (var (theme, culture) in new[] { ("brand", (string?)null), ("flatline", null), ("brand", "de"), ("brand", "ru") })
        {
            File.WriteAllBytes(Path.Combine(CaptureDir(), $"rating-ask-{theme}-{culture ?? "en"}.png"), Render(theme, culture));
        }
    }

    /// <summary>The crash notice in the same strip: English, and the two longest translations.</summary>
    [OptInFact]
    public void WriteTheCrashNoticeCaptures()
    {
        foreach (var (theme, culture) in new[] { ("brand", (string?)null), ("brand", "de"), ("brand", "fr") })
        {
            File.WriteAllBytes(Path.Combine(CaptureDir(), $"crash-notice-{theme}-{culture ?? "en"}.png"), Render(theme, culture, crash: true));
        }
    }
}
