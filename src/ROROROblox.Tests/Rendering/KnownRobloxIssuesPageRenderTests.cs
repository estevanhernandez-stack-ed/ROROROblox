using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.KnownIssues;
using ROROROblox.Core;
using ROROROblox.Core.KnownIssues;
using ROROROblox.Core.Theming;
using Xunit.Abstractions;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The page with realistic entries, rendered off-screen in every built-in theme, saved as PNGs for the
/// owner to look at before this is called done — there is no approved mock, so the render is the review
/// surface. The assertion is structural: every entry and its buttons made it into the tree.
/// </summary>
public class KnownRobloxIssuesPageRenderTests(ITestOutputHelper output)
{
    private static readonly KnownIssue[] Sample =
    [
        new("window-freeze-on-drag-2026-09", new DateOnly(2026, 9, 23), true,
            "The Roblox window freezes when you drag or resize it",
            "Dragging or resizing a Roblox window stops responding until you press the Windows key.",
            "Press the Windows key, then Escape. Updating Roblox helps.",
            new KnownIssueHelp(KnownIssueFeatures.FpsCaps, "Set this account's frame-rate cap below your monitor's refresh rate."),
            [new KnownIssueLink("Roblox DevForum thread", "https://devforum.roblox.com/t/4032374")],
            new KnownIssueVersions(null, new Version(0, 740))),
        new("memory-leak-2026-08", new DateOnly(2026, 8, 30), false,
            "Roblox's memory use climbs the longer it runs",
            "Each client's memory grows over hours until the PC slows down.",
            "Restart long-running clients now and then.",
            new KnownIssueHelp(KnownIssueFeatures.MemoryWatchdog, "RoRoRo can watch memory and warn you before it gets bad."),
            [],
            null),
    ];

    private sealed class Inline : IUiDispatcher
    {
        public void Invoke(Action action) => action();
    }

    private sealed class NoShell : IShellOpener
    {
        public void Open(string path) { }
    }

    [WindowRenderFact]
    public void EveryEntryAndItsButtonsRender_InEveryTheme()
    {
        var themesDir = Path.Combine(Path.GetTempPath(), "rororo-kri-themes-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(themesDir).ListAsync().GetAwaiter().GetResult().Where(t => t.IsBuiltIn).ToList();
        Assert.True(themes.Count >= 4);

        foreach (var theme in themes)
        {
            var dismissed = Path.Combine(Path.GetTempPath(), "rororo-kri-" + Guid.NewGuid().ToString("N") + ".json");
            var (entries, buttons, png) = ThemedWindowRender.Inspect(
                theme,
                $"KnownRobloxIssuesPage [{theme.Id}]",
                () =>
                {
                    var model = new KnownIssuesNoticeModel(new KnownIssuesDismissals(dismissed), () => Version.Parse("0.739.0.7390687"), new Inline());
                    model.Apply(new KnownIssuesSnapshot(Sample, KnownIssuesSource.Release, DateTimeOffset.Now));
                    return ThemedWindowRender.HostPage(new KnownRobloxIssuesPage(model, _ => { }, new NoShell()), 760, 900);
                },
                content =>
                {
                    var list = (ItemsControl)ThemedWindowRender.Find(content, fe => fe is ItemsControl { Name: "IssueList" }, "the issue list");
                    var count = CountButtons(list); // inside the list only, so shared page chrome cannot move the count
                    var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);

                    // The page subscribes to the process-wide TranslationSource.Instance.CultureChanged
                    // (same pattern as SettingsPage/ShellWindow/MainViewModel) and this harness's STA
                    // thread is gone once this call returns. Undisposed, that subscription outlives the
                    // thread that owns the page's DependencyObjects — a later, unrelated test flipping
                    // CurrentCulture (several localization tests do) then invokes Rebuild() cross-thread
                    // and crashes with "The calling thread cannot access this object". Disposing here,
                    // after the bitmap is already captured, unsubscribes before that can happen.
                    (content as IDisposable)?.Dispose();

                    return (list.Items.Count, count, stream.ToArray());
                },
                raiseLoaded: true);

            var path = Path.Combine(Path.GetTempPath(), $"rororo-known-issues-page-{theme.Id}.png");
            File.WriteAllBytes(path, png);
            output.WriteLine($"{theme.Id}: {path}");

            Assert.Equal(2, entries);
            Assert.Equal(3, buttons); // two "RoRoRo can help" buttons and one link
        }
    }

    private static int CountButtons(DependencyObject root)
    {
        var total = root is Button ? 1 : 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            total += CountButtons(VisualTreeHelper.GetChild(root, i));
        }

        return total;
    }
}
