using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Games;
using ROROROblox.App.Localization;
using ROROROblox.Core.Feeds;
using ROROROblox.Core.Library;
using Xunit.Abstractions;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// v1.41 item 12: the Library's pulse, measured in a window that is really shown (off screen), because the page
/// tells the pulse what is on screen through <c>Loaded</c>, <c>Unloaded</c>, <c>DataContextChanged</c> and
/// <c>IsVisibleChanged</c> on its template roots, and the bare-window harness never raises any of them from WPF.
/// <para>
/// THE MEASUREMENT THE SPEC OWED (§20, "Recycled containers and Loaded"). The spec stated, from WPF's documented
/// behaviour for <c>VirtualizingStackPanel</c>, that a recycled container does not raise <c>Loaded</c> again.
/// It had not been measured in this tree, where the grid is WPF-UI's <c>VirtualizingWrapPanel</c> in Recycling mode.
/// <see cref="ScrollingAHundredEntries_TheInterestSetIsTheRealisedSet"/> scrolls a 100-entry grid and compares what
/// the page declared with the containers that are actually realised, at every stop, and writes the numbers.
/// </para>
/// </summary>
[Collection("MutatesUiCulture")]
public class LibraryPulseRenderTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    /// <summary>Lets layout, container generation, <c>Loaded</c> and the bindings settle. Host thread only.</summary>
    private static void Settle()
    {
        for (var i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private static ListBox Grid(FrameworkElement content) =>
        (ListBox)ThemedWindowRender.Find(content, fe => fe.Name == "LibraryGrid", "LibraryGrid");

    private static ListBox Rows(FrameworkElement content) =>
        (ListBox)ThemedWindowRender.Find(content, fe => fe.Name == "LibraryRows", "LibraryRows");

    /// <summary>The universe ids of the tiles (fixed 144 wide) or rows (no fixed width) a list has realised and is showing.</summary>
    private static long[] RealisedUniverses(ListBox list, bool tiles)
        => Descendants<Border>(list)
            .Where(b => b.DataContext is LibraryItem && (tiles ? b.Width == 144 : b.Padding == new Thickness(12, 8, 12, 8)) && b.IsVisible)
            .Select(b => ((LibraryItem)b.DataContext).UniverseId!.Value)
            .Distinct().OrderBy(i => i).ToArray();

    private static void ScrollTo(ListBox list, double offset)
    {
        Descendants<ScrollViewer>(list).First().ScrollToVerticalOffset(offset);
        Settle();
    }

    [WindowRenderFact]
    public void ScrollingAHundredEntries_TheInterestSetIsTheRealisedSet()
    {
        var lines = WindowRenderHost.Run(() =>
        {
            var log = new List<string>();
            var pulse = new LibraryPageFixtures.FakePulse();
            var built = LibraryPageFixtures.Build(LibraryScene.Hundred, show: true, pulse: pulse);
            try
            {
                Settle();
                var grid = Grid((FrameworkElement)built.Window.Content);
                var scroller = Descendants<ScrollViewer>(grid).First();
                log.Add($"viewport {scroller.ViewportHeight} px, extent {scroller.ExtentHeight} px, 100 entries");

                // Raw events on every root the grid ever realises, so the numbers say what WPF did, not only what the page made of it.
                var raw = new Dictionary<Border, int[]>();
                void Hook(ListBox list, bool tiles)
                {
                    foreach (var root in Descendants<Border>(list).Where(b => tiles ? b.Width == 144 : b.Padding == new Thickness(12, 8, 12, 8)))
                    {
                        if (raw.ContainsKey(root)) continue;
                        var c = new int[4];
                        raw[root] = c;
                        root.Loaded += (_, _) => c[0]++;
                        root.Unloaded += (_, _) => c[1]++;
                        root.DataContextChanged += (_, _) => c[2]++;
                        root.IsVisibleChanged += (_, _) => c[3]++;
                    }
                }

                void Pass(ListBox list, bool tiles, double[] offsets)
                {
                    foreach (var offset in offsets)
                    {
                        ScrollTo(list, offset);
                        Hook(list, tiles);
                        var realised = RealisedUniverses(list, tiles);
                        var declared = pulse.Interest.TryGetValue(built.ViewModel, out var set) ? set : [];
                        log.Add($"{(tiles ? "grid" : "list")} offset {offset,6}: realised {realised.Length,2}, declared {declared.Length,2}, equal {realised.SequenceEqual(declared)}");
                        // Vacuity floor: a list that realised nothing also "equals" an empty declaration.
                        Assert.True(realised.Length >= 4, $"only {realised.Length} realised at offset {offset}; the list did not lay out");
                        Assert.True(realised.Length < 100, "every entry was realised: virtualisation is off");
                        Assert.Equal(realised, declared);
                    }
                }

                Hook(grid, tiles: true);
                Pass(grid, tiles: true, [0.0, 300.0, 900.0, 2500.0, 6000.0, 0.0]);
                log.Add($"grid raw events over the pass: {raw.Count} roots, Loaded {raw.Values.Sum(c => c[0])}, Unloaded {raw.Values.Sum(c => c[1])}, DataContextChanged {raw.Values.Sum(c => c[2])}, IsVisibleChanged {raw.Values.Sum(c => c[3])}");

                raw.Clear();
                built.ViewModel.IsListView = true;
                Settle();
                var rows = Rows((FrameworkElement)built.Window.Content);
                var rowScroller = Descendants<ScrollViewer>(rows).First();
                log.Add($"list viewport {rowScroller.ViewportHeight}, extent {rowScroller.ExtentHeight}");
                Hook(rows, tiles: false);
                // The list scrolls by item (CanContentScroll), so its offsets are entry indexes, not pixels.
                Pass(rows, tiles: false, [0.0, 12.0, 25.0, 50.0, 93.0, 0.0]);
                log.Add($"list raw events over the pass: {raw.Count} roots, Loaded {raw.Values.Sum(c => c[0])}, Unloaded {raw.Values.Sum(c => c[1])}, DataContextChanged {raw.Values.Sum(c => c[2])}, IsVisibleChanged {raw.Values.Sum(c => c[3])}");

                return log;
            }
            finally
            {
                built.Window.Close();
            }
        }, "scroll 100 entries");

        foreach (var line in lines) output.WriteLine(line);
    }

    private sealed class CountingApi : IGamePulseApi
    {
        public List<long[]> Requests { get; } = [];

        public Task<GamePulseRead> ReadAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default)
        {
            var ids = universeIds.ToArray();
            Requests.Add(ids);
            return Task.FromResult(new GamePulseRead(FeedOutcome.Ok, new GamePulseAnswer(
                ids.ToDictionary(id => id, id => new GamePulseReading(1000 + (int)(id % 1000), Start.AddDays(-2))))));
        }
    }

    [WindowRenderFact]
    public void AHundredSavedGames_OneScreenfulIsOneRequest_ScrollingAsksForTheNewOnly_SwitchingAsksForNothingRead()
    {
        var lines = WindowRenderHost.Run(() =>
        {
            var log = new List<string>();
            var clock = new FakeTimeProvider(Start);
            var api = new CountingApi();
            using var service = new GamePulseService(() => api, clock);
            service.AllowReadsFrom(Start);
            var built = LibraryPageFixtures.Build(LibraryScene.Hundred, show: true, pulse: service);
            try
            {
                Settle();
                var content = (FrameworkElement)built.Window.Content;
                var grid = Grid(content);
                var asked = new HashSet<long>();

                int Step(string what, long[] expectedNew)
                {
                    var before = api.Requests.Count;
                    clock.Advance(GamePulseService.Debounce);
                    var made = api.Requests.Skip(before).ToList();
                    var ids = made.SelectMany(r => r).ToArray();
                    log.Add($"{what}: {made.Count} request(s), {ids.Length} ids");
                    Assert.True(ids.All(asked.Add), $"{what}: an id that was already read was asked for again");
                    Assert.Equal(expectedNew.OrderBy(i => i).ToArray(), ids.OrderBy(i => i).ToArray());
                    return made.Count;
                }

                // One screenful: every realised tile, one request.
                var first = RealisedUniverses(grid, tiles: true);
                Assert.True(first.Length >= 4);
                Assert.Equal(1, Step("first screenful", first));

                // Scroll: only what was not read.
                ScrollTo(grid, 900);
                var second = RealisedUniverses(grid, tiles: true);
                var fresh = second.Except(first).ToArray();
                Assert.NotEmpty(fresh);
                Assert.Equal(1, Step("scroll to 900", fresh));

                // Back to the top: all of it was read within five minutes.
                ScrollTo(grid, 0);
                Assert.Equal(0, Step("back to the top", []));

                // To the list: only the rows nobody has read.
                built.ViewModel.IsListView = true;
                Settle();
                var rows = RealisedUniverses(Rows(content), tiles: false);
                Assert.True(rows.Length >= 4, "the list realised no rows");
                var read = first.Union(second).ToHashSet();
                var newRows = rows.Where(r => !read.Contains(r)).ToArray();
                Step("switch to the list", newRows);

                // The grid is collapsed, not unloaded: its tiles must no longer count as on screen.
                Assert.Equal(rows.Length, built.ViewModel.OnScreenCount);

                // Scroll the list: again only what nobody has read.
                ScrollTo(Rows(content), 40);
                var scrolledRows = RealisedUniverses(Rows(content), tiles: false);
                read.UnionWith(rows);
                var newScrolled = scrolledRows.Where(r => !read.Contains(r)).ToArray();
                Assert.NotEmpty(newScrolled);
                Assert.Equal(1, Step("scroll the list", newScrolled));

                // And back: nothing is new.
                built.ViewModel.IsGridView = true;
                Settle();
                Assert.Equal(0, Step("switch back to the grid", []));
                return log;
            }
            finally
            {
                built.Window.Close();
            }
        }, "request counts");

        foreach (var line in lines) output.WriteLine(line);
    }

    [WindowRenderFact]
    public void MinimisedOrHidden_IsAnEmptySet_AndRestoredDeclaresItAgain()
    {
        var lines = WindowRenderHost.Run(() =>
        {
            var log = new List<string>();
            var pulse = new LibraryPageFixtures.FakePulse();
            var built = LibraryPageFixtures.Build(LibraryScene.Hundred, show: true, pulse: pulse);
            try
            {
                Settle();
                var grid = Grid((FrameworkElement)built.Window.Content);
                var shown = RealisedUniverses(grid, tiles: true);
                Assert.True(shown.Length >= 4);
                Assert.Equal(shown, pulse.Interest[built.ViewModel]);

                built.Window.WindowState = WindowState.Minimized;
                Settle();
                log.Add($"minimised: declared {(pulse.Interest.TryGetValue(built.ViewModel, out var m) ? m.Length : 0)}");
                Assert.False(pulse.Interest.ContainsKey(built.ViewModel), "a minimised window still declares interest");

                built.Window.WindowState = WindowState.Normal;
                Settle();
                log.Add($"restored: declared {pulse.Interest[built.ViewModel].Length}");
                Assert.Equal(shown, pulse.Interest[built.ViewModel]);

                built.Window.Hide();
                Settle();
                log.Add($"hidden: declared {(pulse.Interest.TryGetValue(built.ViewModel, out var h) ? h.Length : 0)}");
                Assert.False(pulse.Interest.ContainsKey(built.ViewModel), "a hidden window still declares interest");

                built.Window.Show();
                Settle();
                log.Add($"shown again: declared {pulse.Interest[built.ViewModel].Length}");
                Assert.Equal(shown, pulse.Interest[built.ViewModel]);
                return log;
            }
            finally
            {
                built.Window.Close();
            }
        }, "minimise and hide");

        foreach (var line in lines) output.WriteLine(line);
    }

    [WindowRenderFact]
    public void LeavingThePage_DeclaresNothing()
    {
        var pulse = new LibraryPageFixtures.FakePulse();
        var result = WindowRenderHost.Run(() =>
        {
            var built = LibraryPageFixtures.Build(LibraryScene.Hundred, show: true, pulse: pulse);
            Settle();
            var before = pulse.Interest.TryGetValue(built.ViewModel, out var set) ? set.Length : 0;
            built.Window.Close();
            Settle();
            return (before, after: pulse.Interest.Count);
        }, "leave the page");

        Assert.True(result.before >= 4);
        Assert.Equal(0, result.after);
    }

    [WindowRenderFact]
    public void TheTile_ShowsTheCountOnly_TheRowShowsTheJoinedLine_APrivateServerShowsTheDateAlone()
    {
        var lines = WindowRenderHost.Run(() =>
        {
            var pulse = new LibraryPageFixtures.FakePulse();
            // Place 2001 is a favourite and has a saved private server; both share universe 20010.
            pulse.Readings[20010] = new GamePulseView(81_827, DateTimeOffset.UtcNow.AddDays(-2));
            var built = LibraryPageFixtures.Build(LibraryScene.Showcase, show: true, pulse: pulse);
            try
            {
                Settle();
                var content = (FrameworkElement)built.Window.Content;
                var items = built.ViewModel.Items.ToList();
                var game = items.Single(i => i.Entry.Kind == ROROROblox.Core.LibraryEntryKind.Favourite && i.PlaceId == 2001);
                var server = items.Single(i => i.IsPrivate);
                var unread = items.Single(i => i.PlaceId == 2002);
                pulse.Raise(20010);
                Settle();

                var res = new List<string>
                {
                    $"game: count '{game.PulseCountText}', updated '{game.PulseUpdatedText}', joined '{game.PulseText}'",
                    $"server: count '{server.PulseCountText}', updated '{server.PulseUpdatedText}', joined '{server.PulseText}'",
                    $"unread: count '{unread.PulseCountText}', joined '{unread.PulseText}'",
                };
                Assert.Equal("81,827 playing", game.PulseCountText);
                Assert.Equal("updated 2 days ago", game.PulseUpdatedText);
                Assert.Equal("81,827 playing · updated 2 days ago", game.PulseText);
                Assert.Equal(string.Empty, server.PulseCountText);
                Assert.Equal("updated 2 days ago", server.PulseText);
                Assert.Equal(string.Empty, unread.PulseText);
                Assert.False(unread.HasPulse);

                // Grid: the count is drawn in the game's tile and the date is not; the server's tile draws neither.
                var tile = Descendants<Border>(Grid(content)).Single(b => b.Width == 144 && b.DataContext == game);
                var tileTexts = Descendants<TextBlock>(tile).Where(t => t.IsVisible && t.Text.Length > 0).Select(t => t.Text).ToList();
                Assert.Contains("81,827 playing", tileTexts);
                Assert.DoesNotContain(tileTexts, t => t.Contains("updated"));
                var serverTile = Descendants<Border>(Grid(content)).Single(b => b.Width == 144 && b.DataContext == server);
                Assert.DoesNotContain(Descendants<TextBlock>(serverTile).Where(t => t.IsVisible), t => t.Text.Contains("playing") || t.Text.Contains("updated"));

                // "Last played" stays: the pulse is beside it, never in its place.
                Assert.Contains(game.MetaText, tileTexts);

                // Accessible name and tooltip.
                var container = Descendants<ListBoxItem>(Grid(content)).First(c => Descendants<Border>(c).First(b => b.Width == 144).DataContext == game);
                Assert.Equal(game.EntryName, AutomationProperties.GetName(container));
                Assert.Contains("81,827 playing", game.EntryName);
                Assert.Contains("updated 2 days ago", game.EntryName);
                Assert.Contains("Petal Simulator", game.EntryName);
                Assert.Contains("81,827 playing · updated 2 days ago", game.TooltipText);
                Assert.DoesNotContain("playing", server.TooltipText);
                Assert.Contains("updated 2 days ago", server.TooltipText);

                // The buttons keep their own names.
                Assert.Equal(game.PlayName, AutomationProperties.GetName(Descendants<Button>(tile).First(b => AutomationProperties.GetAutomationId(b) == "DangerPlay")));

                // List: the second line under the name, joined; the server's is the date alone.
                built.ViewModel.IsListView = true;
                Settle();
                var rowTexts = Descendants<TextBlock>(Rows(content)).Where(t => t.Name == "RowPulse" && t.IsVisible).Select(t => t.Text).ToList();
                res.Add("list second lines: " + string.Join(" | ", rowTexts));
                Assert.Contains("81,827 playing · updated 2 days ago", rowTexts);
                Assert.Contains("updated 2 days ago", rowTexts);
                Assert.Equal(2, rowTexts.Count);
                return res;
            }
            finally
            {
                built.Window.Close();
            }
        }, "tile and row words");

        foreach (var line in lines) output.WriteLine(line);
    }

    [WindowRenderFact]
    public void NoSpinnerAndNoPlaceholder_TheLineIsAbsentUntilAnAnswerArrives()
    {
        var drawn = WindowRenderHost.Run(() =>
        {
            var pulse = new LibraryPageFixtures.FakePulse();
            var built = LibraryPageFixtures.Build(LibraryScene.Showcase, show: true, pulse: pulse);
            try
            {
                Settle();
                var content = (FrameworkElement)built.Window.Content;
                var tilePulse = Descendants<TextBlock>(Grid(content)).Where(t => t.Name == "TilePulse").ToList();
                Assert.NotEmpty(tilePulse);
                Assert.Empty(Descendants<ProgressBar>(content));
                return tilePulse.Count(t => t.IsVisible && t.ActualHeight > 0);
            }
            finally
            {
                built.Window.Close();
            }
        }, "no answer");

        Assert.Equal(0, drawn);
    }

    /// <summary>
    /// C3's tile has a fixed height (the grid virtualises on a known item size), so the content must fit it: the tile
    /// is measured with its height removed, in every language, with the widest tile (a private-server-free favourite
    /// with the longest count, a wrapped "last played" line and a DEFAULT tag) and with no pulse at all.
    /// </summary>
    [WindowRenderFact]
    public void TheTileHoldsItsContent_InEverySupportedLanguage_WithAndWithoutAPulse()
    {
        var brand = LibraryPageFixtures.Themes().Single(t => t.Id == "brand");
        var previous = TranslationSource.Instance.CurrentCulture;
        var over = new List<string>();
        try
        {
            foreach (var culture in new[] { "en", "de", "es", "fr", "pl", "pt-BR", "ru" })
            {
                TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
                foreach (var withPulse in new[] { false, true })
                {
                    var (declared, natural) = ThemedWindowRender.Inspect(brand, $"tile height [{culture}, pulse={withPulse}]",
                        () =>
                        {
                            var pulse = new LibraryPageFixtures.FakePulse();
                            if (withPulse) pulse.ReadEverything([2001, 2002, 2003, 2004, 2005]);
                            return LibraryPageFixtures.Build(LibraryScene.Showcase, filters: "Recent,Favourites,PrivateServers", pulse: pulse).Window;
                        },
                        content =>
                        {
                            var tiles = Descendants<Border>(Grid(content)).Where(b => b.Width == 144 && b.DataContext is LibraryItem).ToList();
                            Assert.True(tiles.Count >= 5, $"only {tiles.Count} tiles realised");
                            var declaredHeight = tiles[0].Height;
                            var worst = 0.0;
                            foreach (var tile in tiles)
                            {
                                tile.Height = double.NaN;
                                tile.Measure(new Size(144, double.PositiveInfinity));
                                worst = Math.Max(worst, tile.DesiredSize.Height);
                                tile.Height = declaredHeight;
                            }

                            return (declaredHeight, worst);
                        });
                    output.WriteLine($"[{culture}] pulse={withPulse}: the widest tile needs {natural:F1} DIP of {declared:F0}");
                    if (natural > declared + 0.5) over.Add($"[{culture}] pulse={withPulse}: needs {natural:F1}, the tile is {declared:F0}");
                }
            }
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }

        Assert.True(over.Count == 0, "A tile's content is taller than the tile:" + Environment.NewLine + string.Join(Environment.NewLine, over));
    }

    // ---- opt-in captures for the screenshot compare against the C1 mock's picks (C3, D, E) ----

    private sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_CAPTURE_PULSE") != "1")
            {
                Skip = "Opt-in capture: set RORORO_CAPTURE_PULSE=1 to write pulse-<surface>-<theme>.png under docs/cycles/2026-11-26-roblox-feeds/mocks/compare/.";
            }
        }
    }

    private static string CompareDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var target = Path.Combine(dir!, "docs", "cycles", "2026-11-26-roblox-feeds", "mocks", "compare");
        Directory.CreateDirectory(target);
        return target;
    }

    /// <summary>The mock's own numbers, so the pair reads the same strings: 81,827 and two days; 1,204 and six hours; zero and a month and year.</summary>
    private static LibraryPageFixtures.FakePulse MockPulse()
    {
        var now = DateTimeOffset.UtcNow;
        var pulse = new LibraryPageFixtures.FakePulse();
        pulse.Readings[2001L * 10] = new GamePulseView(81_827, now.AddDays(-2));
        pulse.Readings[2002L * 10] = new GamePulseView(1_204, now.AddHours(-6));
        pulse.Readings[2003L * 10] = new GamePulseView(0, now.AddMonths(-20));
        pulse.Readings[8737899170L * 10] = new GamePulseView(81_827, now.AddDays(-2));
        pulse.Readings[920587237L * 10] = new GamePulseView(1_204, now.AddHours(-6));
        pulse.Readings[126884695634066L * 10] = new GamePulseView(0, now.AddMonths(-20));
        return pulse;
    }

    private static byte[] RenderPng(FrameworkElement content, FrameworkElement target)
    {
        var ground = content.TryFindResource("BgBrush") as Brush
            ?? throw new InvalidOperationException("BgBrush did not resolve; refusing to paint a white PNG.");
        var w = (int)Math.Round(target.ActualWidth);
        var h = (int)Math.Round(target.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(ground, null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            dc.DrawRectangle(
                new VisualBrush(target) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        }

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [OptInFact]
    public void WriteThePulseCaptures()
    {
        foreach (var themeId in new[] { "brand", "flatline" })
        {
            var theme = LibraryPageFixtures.Themes().Single(t => t.Id == themeId);
            foreach (var (surface, build, pick) in new (string, Func<Window>, Func<FrameworkElement, FrameworkElement>)[]
            {
                ("tile", () => LibraryPageFixtures.Build(LibraryScene.Showcase, filters: "Recent,Favourites,PrivateServers", pulse: MockPulse()).Window, Grid),
                ("row", () => LibraryPageFixtures.Build(LibraryScene.Showcase, view: "list", pulse: MockPulse()).Window, c => Descendants<ListBox>(c).Single(l => l.Name == "LibraryRows")),
                ("panel-entry", () => PanelWindowFixtures.Build(accounts: 1, content: PanelContent.Full, pulse: MockPulse()),
                    c => ThemedWindowRender.Find(c, fe => fe.Name == "AccountsItemsControl", "AccountsItemsControl")),
            })
            {
                var png = ThemedWindowRender.Inspect(theme, $"pulse capture [{surface}/{themeId}]", build, content => RenderPng(content, pick(content)));
                var name = $"pulse-{surface}-{themeId}.png";
                File.WriteAllBytes(Path.Combine(CompareDir(), name), png);
                output.WriteLine($"{name} ({png.Length} bytes)");
            }
        }
    }
}
