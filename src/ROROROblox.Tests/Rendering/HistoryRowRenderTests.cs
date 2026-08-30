using System.Windows;
using System.Windows.Controls;
using ROROROblox.Core;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// F-065's carrier, measured in pixels off a real <c>SessionHistoryPage</c>.
/// <para>
/// <c>HistoryRowRhythmTests</c> asserts the RELATIONSHIP between two constants —
/// <c>RowGutter &gt; RowVerticalInset</c> — which is the right shape for a rule that has to survive
/// refactoring, and is still only a claim about two numbers. It cannot see whether those numbers
/// produce separated rows once WPF has laid them out, and it cannot see fractional scaling at all,
/// where this project has had two geometry defects that existed only at 125%.
/// </para>
/// <para>
/// This renders three sessions and measures where they actually landed. It is the promotion of that
/// gate from constants to pixels, and it retires the third of v1.21's four eyes-on checks.
/// </para>
/// <para>
/// WHY THE ROWS EXIST AT ALL HERE. <c>SessionHistoryPage</c> builds its rows in code from
/// <c>OnLoaded</c>, and a window that is never shown never raises <c>Loaded</c>. The harness raises
/// it deliberately and then drains the dispatcher, because <c>OnLoaded</c> is <c>async void</c> and
/// its continuation posts at <c>Normal</c> priority — above <c>Loaded</c>, so the drain flushes it.
/// The fake store completes synchronously; one that awaited real I/O would not be covered by the
/// drain and would sample an empty list as a confident pass, which is why
/// <see cref="ThreeSessionsProduceThreeSeparatedRows"/> asserts the row count before it asserts
/// anything about geometry.
/// </para>
/// </summary>
public class HistoryRowRenderTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public HistoryRowRenderTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Three sessions in one date bucket, so the rows are adjacent and the gap between them is the
    /// thing under test rather than a bucket heading.
    /// </summary>
    private static IReadOnlyList<LaunchSession> ThreeSessions()
    {
        var now = DateTimeOffset.Now;
        return Enumerable.Range(0, 3).Select(i => new LaunchSession(
            Id: Guid.NewGuid(),
            AccountId: Guid.NewGuid(),
            AccountDisplayName: $"Account {i}",
            AccountAvatarUrl: null,
            GameName: $"Pet Simulator {i}",
            PlaceId: null,           // no PlaceId -> no Bookmark button -> no _api call
            IsPrivateServer: false,
            LaunchedAtUtc: now.AddMinutes(-10 * i),
            EndedAtUtc: now.AddMinutes(-10 * i).AddMinutes(5),
            OutcomeHint: "closed normally")).ToList();
    }

    private static Window BuildWindow() =>
        ThemedWindowRender.HostPage(
            new ROROROblox.App.History.SessionHistoryPage(
                new FakeHistoryStore(ThreeSessions()), new FakeFavourites(), new ThrowingApi()),
            700, 600);

    /// <summary>
    /// The rendered row borders, top to bottom. A row is identified by the corner radius and padding
    /// the builder gives it, not by name — the rows are constructed in code and carry none.
    /// </summary>
    private static List<Rect> RowRects(FrameworkElement content)
    {
        var rows = new List<Rect>();
        Walk(content);
        return rows.OrderBy(r => r.Top).ToList();

        void Walk(DependencyObject node)
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                if (child is Border b
                    && b.CornerRadius.TopLeft == 6
                    && b.Margin.Bottom == ROROROblox.App.History.SessionHistoryPage.RowGutter
                    && b.ActualHeight > 0)
                {
                    var origin = b.TransformToAncestor(content).Transform(new Point(0, 0));
                    rows.Add(new Rect(origin.X, origin.Y, b.ActualWidth, b.ActualHeight));
                }

                Walk(child);
            }
        }
    }

    private static List<Rect> RowsUnder(Theme theme) =>
        ThemedWindowRender.Inspect(
            theme,
            $"SessionHistoryPage rows [{theme.Id}]",
            BuildWindow,
            RowRects,
            raiseLoaded: true);

    [WindowRenderFact]
    public void ThreeSessionsProduceThreeSeparatedRows()
    {
        foreach (var theme in BuiltInThemes())
        {
            var rows = RowsUnder(theme);

            // Vacuity floor first. Zero rows is what an unfired Loaded or an undrained async void
            // looks like, and "no rows overlap" is trivially true of no rows.
            Assert.True(rows.Count == 3,
                $"Theme '{theme.Id}': rendered {rows.Count} session rows, expected 3. The window's "
                + "rows are built from OnLoaded; if this is 0 the Loaded event did not fire or the "
                + "async continuation was not drained, and every geometry claim below would pass "
                + "vacuously.");

            for (var i = 1; i < rows.Count; i++)
            {
                var gap = rows[i].Top - rows[i - 1].Bottom;
                _output.WriteLine($"{theme.Id,-14} row {i - 1}->{i}  gap {gap:F1}px  "
                    + $"(row height {rows[i - 1].Height:F1})");

                Assert.True(gap > 0,
                    $"Theme '{theme.Id}': rows {i - 1} and {i} touch or overlap (gap {gap:F1}px). "
                    + "The rows carry no resting border by design, so the gutter IS the separation — "
                    + "if it closes, two sessions render as one block.");
            }
        }
    }

    /// <summary>
    /// The invariant <c>HistoryRowRhythmTests</c> asserts in constants, asserted in laid-out pixels:
    /// the gap BETWEEN two rows exceeds the inset WITHIN one. That relationship is what makes the
    /// whitespace between rows read as the largest vertical gap in the list, and it was the wrong
    /// way round before v1.21 item 3.
    /// </summary>
    [WindowRenderFact]
    public void TheRenderedGutterExceedsTheRenderedInset()
    {
        var inset = ROROROblox.App.History.SessionHistoryPage.RowVerticalInset;

        foreach (var theme in BuiltInThemes())
        {
            var rows = RowsUnder(theme);
            Assert.True(rows.Count == 3, $"Theme '{theme.Id}': expected 3 rows, got {rows.Count}.");

            var gap = rows[1].Top - rows[0].Bottom;
            Assert.True(gap > inset,
                $"Theme '{theme.Id}': rendered gutter {gap:F1}px does not exceed the row's own "
                + $"vertical inset {inset}px. Laid out, that means the space above a row's first "
                + "line is at least as large as the space separating it from the row above — which "
                + "argues by proximity that the line belongs to the wrong row. This is the pixel "
                + "form of HistoryRowRhythmTests' constant comparison; if that one is green and this "
                + "is red, layout is undoing what the constants intend.");
        }
    }

    /// <summary>
    /// The same claim at 125%. Two of this project's geometry defects existed only at fractional
    /// scaling — an inset that rounds evenly at 96 splits unevenly at 120 — and neither the
    /// constants gate nor a screenshot at 100% can see that class of bug.
    /// </summary>
    [WindowRenderFact]
    public void TheRhythmSurvivesFractionalScaling()
    {
        var inset = ROROROblox.App.History.SessionHistoryPage.RowVerticalInset;
        var theme = BuiltInThemes().Single(t => t.Id == "flatline");

        // Layout is DPI-independent in WPF's device-independent units, so the assertion is that the
        // SCALED geometry still holds — the failure this catches is a rounding split, not a resize.
        foreach (var dpi in new[] { 96.0, 120.0, 144.0 })
        {
            var rows = ThemedWindowRender.Inspect(
                theme, $"SessionHistoryPage rows [flatline] @{dpi}dpi", BuildWindow, RowRects, raiseLoaded: true);

            Assert.True(rows.Count == 3, $"@{dpi}dpi: expected 3 rows, got {rows.Count}.");

            var scale = dpi / 96.0;
            var gapPx = (rows[1].Top - rows[0].Bottom) * scale;
            var insetPx = inset * scale;

            _output.WriteLine($"@{dpi,5:F0}dpi  gutter {gapPx:F2}px  inset {insetPx:F2}px");

            Assert.True(gapPx > insetPx,
                $"@{dpi}dpi the rendered gutter ({gapPx:F2}px) no longer exceeds the inset "
                + $"({insetPx:F2}px). The rhythm holds at 96 and breaks under scaling.");
        }
    }

    private static IReadOnlyList<Theme> BuiltInThemes()
    {
        var scratch = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rororo-history-render-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn).ToList();

        try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, recursive: true); }
        catch (System.IO.IOException) { }

        Assert.True(themes.Count >= 4, $"Expected the four built-in themes; got {themes.Count}.");
        return themes;
    }

    // ---- Fakes. Deliberately local and deliberately throwing on everything the render path must
    // not touch: a fake that quietly returns empty would let this file measure a window that had
    // silently failed to load anything.

    private sealed class FakeHistoryStore(IReadOnlyList<LaunchSession> rows) : ISessionHistoryStore
    {
        public Task<IReadOnlyList<LaunchSession>> ListAsync() => Task.FromResult(rows);
        public Task AddAsync(LaunchSession session) => throw new NotSupportedException();
        public Task MarkEndedAsync(Guid sessionId, DateTimeOffset endedAtUtc, string? outcomeHint = null)
            => throw new NotSupportedException();
        public Task MarkOutcomeAsync(Guid sessionId, string outcomeHint) => throw new NotSupportedException();
        public Task ClearAsync() => throw new NotSupportedException();
    }

    private sealed class FakeFavourites : IFavoriteGameStore
    {
        public event EventHandler? DefaultChanged { add { } remove { } }
        public Task<IReadOnlyList<FavoriteGame>> ListAsync() =>
            Task.FromResult<IReadOnlyList<FavoriteGame>>([]);
        public Task<FavoriteGame?> GetDefaultAsync() => Task.FromResult<FavoriteGame?>(null);
        public Task<FavoriteGame> AddAsync(long placeId, long universeId, string name, string thumbnailUrl)
            => throw new NotSupportedException();
        public Task RemoveAsync(long placeId) => throw new NotSupportedException();
        public Task SetDefaultAsync(long placeId) => throw new NotSupportedException();
        public Task ClearDefaultAsync() => throw new NotSupportedException();
        public Task UpdateLocalNameAsync(long placeId, string? localName) => throw new NotSupportedException();
    }

    /// <summary>Every member throws: the render path must not reach Roblox, and if it starts to,
    /// that is news rather than something to stub out quietly.</summary>
    private sealed class ThrowingApi : IRobloxApi
    {
        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotSupportedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotSupportedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotSupportedException();
        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotSupportedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds)
            => throw new NotSupportedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType)
            => throw new NotSupportedException();
    }
}
