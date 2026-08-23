using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.App.History;

/// <summary>
/// The History destination, hosted by the shell (F-013 — formerly <c>SessionHistoryWindow</c>).
/// Implements <see cref="IDisposable"/> for the streamer-identity unsubscribe the window used to
/// do on <c>Closed</c>; the shell disposes its pages when it closes.
/// </summary>
internal partial class SessionHistoryPage : UserControl, IDisposable
{
    private readonly ISessionHistoryStore _store;
    private readonly IFavoriteGameStore _favorites;
    private readonly IRobloxApi _api;
    private readonly IStreamerIdentityProvider? _streamerIdentity;
    private HashSet<long> _knownPlaceIds = new();
    // Cached last-fetched rows so a streamer-mode toggle (provider.Changed) can re-render with
    // fresh fake/real identities WITHOUT re-hitting disk (same pattern as FriendFollowWindow's
    // _inGame/_online/_offline cache). _hasData guards OnStreamerIdentityChanged from rebuilding
    // over a not-yet-loaded/empty state.
    private IReadOnlyList<LaunchSession> _rows = [];
    private bool _hasData;

    // F-038: what the last read actually did, kept because "zero rows" answers two very different
    // questions and the window used to give the reassuring answer to both.
    private SessionHistoryOutcome _outcome = SessionHistoryOutcome.Empty;
    private string? _readError;

    /// <summary>
    /// The gutter BETWEEN two session rows, and the inset WITHIN one. The invariant is
    /// <c>RowGutter &gt; RowVerticalInset</c>, and it is the whole of F-065's fix — see the block
    /// comment in <see cref="BuildRow"/> for why this carries the separation instead of a rule.
    /// <para>
    /// Named constants rather than inline numbers so the relationship is assertable:
    /// <c>HistoryRowRhythmTests</c> reads these two and fails if the gutter stops exceeding the
    /// inset. A gate that matched on the literal <c>6</c> would be checking the spelling; this
    /// checks the reason.
    /// </para>
    /// </summary>
    internal const double RowGutter = 12;

    /// <inheritdoc cref="RowGutter"/>
    internal const double RowVerticalInset = 8;

    // A bookmark writes the favorites store, and the old modal refreshed the view model's library
    // when ShowDialog returned. A page has no close moment; the composition root hands in the
    // refresh instead (F-013).
    private readonly Action? _libraryChanged;

    // v1.23 session stats (spec §1). Both optional so every existing construction — tests
    // included — keeps compiling; a page built without them simply never shows the block.
    private readonly ISessionStatsStore? _stats;
    private readonly Func<IReadOnlyList<AccountSummary>>? _roster;

    public SessionHistoryPage(
        ISessionHistoryStore store, IFavoriteGameStore favorites, IRobloxApi api,
        IStreamerIdentityProvider? streamerIdentity = null,
        Action? libraryChanged = null,
        ISessionStatsStore? stats = null,
        Func<IReadOnlyList<AccountSummary>>? roster = null)
    {
        _stats = stats;
        _roster = roster;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _streamerIdentity = streamerIdentity;
        _libraryChanged = libraryChanged;
        InitializeComponent();
        Loaded += OnLoaded;

        // Streamer mode can be toggled (or rerolled) while this page is visible — re-render the
        // already-fetched rows with the new fake/real identities instead of forcing a reload.
        // Unsubscribed in Dispose (the shell disposes pages on close) so a discarded page never
        // stays rooted via the provider's Changed event (same leak concern
        // AccountSummary.DetachIdentityProvider guards against for account rows).
        if (_streamerIdentity is not null)
        {
            _streamerIdentity.Changed += OnStreamerIdentityChanged;
        }
    }

    public void Dispose()
    {
        if (_streamerIdentity is not null)
        {
            _streamerIdentity.Changed -= OnStreamerIdentityChanged;
        }
    }

    /// <summary>Owner for message boxes: the shell when attached, else the plain overload.</summary>
    private MessageBoxResult ShowMessage(string text, string caption, MessageBoxButton button, MessageBoxImage image)
        => Window.GetWindow(this) is { } owner
            ? MessageBox.Show(owner, text, caption, button, image)
            : MessageBox.Show(text, caption, button, image);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
        await RenderStatsAsync();
    }

    /// <summary>
    /// Fill the stats block from the rollup. Failure collapses the block and says nothing louder
    /// than a log line — stats must never take the history page down with them (spec §5).
    /// </summary>
    private async Task RenderStatsAsync()
    {
        if (_stats is null || _roster is null) return;

        SessionStats snapshot;
        try
        {
            snapshot = await _stats.ReadAsync();
        }
        catch
        {
            StatsBlock.Visibility = Visibility.Collapsed;
            return;
        }

        // RenderName inside the presenter is streamer-aware, so this block follows the same rule
        // as the rows below: never the real roster while streamer mode is active.
        var view = SessionStatsPresenter.Build(snapshot, _roster());

        if (!view.HasAnything)
        {
            StatsBlock.Visibility = Visibility.Collapsed;
            return;
        }

        StatsBlock.Visibility = Visibility.Visible;
        PeakAltsText.Text = view.PeakConcurrentAlts.ToString();
        TotalUptimeText.Text = view.TotalUptime;
        MostPlayedText.Text = $"most played: {view.MostPlayedGame}";
        StreakText.Text = view.StreakDays.ToString();
        StreakCaption.Text = view.LongestStreakDays > view.StreakDays
            ? $"best: {view.LongestStreakDays} days · longest session {view.LongestSession}"
            : $"longest session {view.LongestSession}";

        LeaderboardList.Children.Clear();
        foreach (var row in view.Leaderboard)
        {
            var line = new DockPanel { Margin = new Thickness(2, 2, 2, 0) };
            var uptime = new TextBlock
            {
                Text = $"{SessionStatsPresenter.FormatUptime(row.Uptime)} · {row.Launches} launches{row.StreakSuffix}",
                FontSize = (double)FindResource("MetaFontSize"),
                Foreground = (Brush)FindResource("MutedTextBrush"),
            };
            DockPanel.SetDock(uptime, Dock.Right);
            line.Children.Add(uptime);
            line.Children.Add(new TextBlock
            {
                Text = row.Name,
                FontSize = (double)FindResource("BodyFontSize"),
                Foreground = (Brush)FindResource("WhiteBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            LeaderboardList.Children.Add(line);
        }

        IntegrityNoteText.Text = view.IntegrityNote;
        IntegrityNoteText.Visibility = view.IntegrityNote.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStreamerIdentityChanged(object? sender, EventArgs e)
    {
        if (_hasData)
        {
            RenderRows();
        }
        // The leaderboard shows account names, so it follows the same toggle (the roster's
        // RenderName re-resolves through the identity provider on every Build).
        _ = RenderStatsAsync();
    }

    private async Task ReloadAsync()
    {
        StatusText.Text = SessionHistoryStatus.Loading;

        IReadOnlyList<LaunchSession> rows;
        try
        {
            rows = await _store.ListAsync();
            _outcome = rows.Count == 0 ? SessionHistoryOutcome.Empty : SessionHistoryOutcome.Loaded;
            _readError = null;
        }
        catch (Exception ex)
        {
            // F-038. This catch used to end at `rows = []`, and an empty list renders "No launches
            // yet." A file that could not be opened — locked by another process, damaged — was
            // therefore reported to the user as a confident statement that they had never launched
            // anything. The exception is kept, not just the fact of it: "in use by another process"
            // is the difference between a shrug and a fix.
            rows = [];
            _outcome = SessionHistoryOutcome.Unreadable;
            _readError = ex.Message;
        }

        // Snapshot the favorites place ids so each row can decide whether to show "+ Bookmark"
        // or "Saved" without hitting disk N times. Best-effort: empty set on failure.
        try
        {
            var saved = await _favorites.ListAsync();
            _knownPlaceIds = saved.Select(f => f.PlaceId).ToHashSet();
        }
        catch
        {
            _knownPlaceIds = [];
        }

        _rows = rows;
        _hasData = true;
        RenderRows();
    }

    /// <summary>
    /// (Re)build the visible rows from the cached <see cref="_rows"/> + <see cref="_knownPlaceIds"/>
    /// — no disk call. Called after a successful <see cref="ReloadAsync"/> fetch AND from
    /// <see cref="OnStreamerIdentityChanged"/> so a streamer-mode flip (or reroll) while this window
    /// is open re-renders with the current fake/real identities instantly.
    /// </summary>
    private void RenderRows()
    {
        HistoryList.Children.Clear();
        StatusText.Text = SessionHistoryStatus.StatusLine(_outcome, _rows.Count, _readError);

        if (_rows.Count == 0)
        {
            var (headline, detail) = SessionHistoryStatus.Placeholder(_outcome);
            EmptyHeadline.Text = headline;
            EmptyDetail.Text = detail;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;

        // Group by date (today / yesterday / older) for readability — same pattern as
        // chat-app message lists.
        var today = DateTimeOffset.Now.Date;
        var yesterday = today.AddDays(-1);
        string? lastBucket = null;

        foreach (var row in _rows)
        {
            var local = row.LaunchedAtUtc.ToLocalTime().Date;
            var bucket = local == today ? "Today"
                : local == yesterday ? "Yesterday"
                : local.ToString("dddd, MMMM d");
            if (bucket != lastBucket)
            {
                HistoryList.Children.Add(BuildBucketHeader(bucket));
                lastBucket = bucket;
            }
            HistoryList.Children.Add(BuildRow(row));
        }
    }

    private TextBlock BuildBucketHeader(string label) => new()
    {
        Text = label,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)FindResource("CyanBrush"),
        Margin = new Thickness(4, 12, 0, 6),
    };

    private HistoryRowPresenter BuildRow(LaunchSession row)
    {
        // Streamer-mode-aware display identity (mirrors AccountSummary.RenderName /
        // AvatarDisplaySource and FriendFollowWindow's per-row ForFriend/ForAccount calls). One
        // tray-click into history must not show the real roster while streamer mode is active —
        // ForAccount internally no-ops to the real values when the provider is null/inactive, so
        // this is a straight swap-in with no behavior change when streamer mode is off. The
        // persisted LaunchSession row itself (AccountDisplayName/AccountAvatarUrl) is never
        // mutated — only what reaches this visible row.
        var display = _streamerIdentity?.ForAccount(row.AccountId, row.AccountDisplayName, row.AccountAvatarUrl ?? string.Empty)
                      ?? new DisplayIdentity(row.AccountDisplayName, row.AccountAvatarUrl ?? string.Empty);

        // F-065: "which session is which" must hold when the fill stops carrying it. RowBg against
        // the page field measures 1.09 brand / 1.08 midnight / 1.08 magenta-heat / 1.33 flatline, so
        // the card edge is invisible in every theme and the row separation rested on nothing.
        //
        // THE ROW TAKES F-065'S SECOND OPTION -- "a baseline rule OR FIXED LEADING RHYTHM" -- and
        // NOT a boundary. Three reasons, in the order they rule the first option out:
        //
        // 1. A row rule is a SEPARATOR, and WCAG 1.4.11's 3:1 does not govern separators. ThemeSlots
        //    .InteractiveEdge says so in as many words and InteractiveEdgeBindingTests enforces it:
        //    the derived edge is for interactive control boundaries only, because binding it to a
        //    card edge or a row rule "would repaint every user's theme from a hairline to mid grey
        //    to fix a problem those surfaces do not have". Deriving one here measures
        //    #1F3149 -> #647181 in brand, which is that exact repaint, arrived at from the other
        //    direction. The plain DividerBrush bind is the alternative and measures 1.05-1.16, a
        //    boundary that does not read -- chrome added to satisfy a gate.
        //
        // 2. A resting border would break the app's own row vocabulary. On the account list a row
        //    is a card -- fill, radius, gutter, no edge -- and a border means STATE: expired takes
        //    RowExpiredAccent at 1px, focused takes Cyan at 2px. Spending that channel on rows in no
        //    state would make History the one list that says "state" when it means "row", in the
        //    cycle whose whole thesis is one vocabulary.
        //
        // 3. Geometry cannot fail a theme. A rule has to be re-measured against every palette a user
        //    writes; a gutter is the same gutter in all of them, including the ones nobody has
        //    written yet.
        //
        // So the carrier is the rhythm: the gutter BETWEEN rows exceeds the inset WITHIN one, which
        // is what makes the whitespace between two rows read as the largest vertical gap in the
        // list. It was the wrong way round before -- a 6px gutter against a 10px inset argued that a
        // row's own first line belonged to the row above it. At the text layer the ratio is now
        // 28px between rows against 2px between a row's two lines.
        var border = new HistoryRowPresenter
        {
            Margin = new Thickness(0, 0, 0, RowGutter),
            Padding = new Thickness(12, RowVerticalInset, 12, RowVerticalInset),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("RowBgBrush"),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Avatar circle. display.AvatarSource is either the real AvatarUrl (http) or a fake
        // pack:// resource URI (streamer mode active) — both are valid absolute Uris for BitmapImage.
        var avatarBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = (Brush)FindResource("NavyBrush"),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(display.AvatarSource))
        {
            try
            {
                avatarBorder.Child = new System.Windows.Controls.Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(display.AvatarSource)),
                    Stretch = Stretch.UniformToFill,
                };
            }
            catch
            {
                // Bad URL — leave the navy disk.
            }
        }
        Grid.SetColumn(avatarBorder, 0);
        grid.Children.Add(avatarBorder);

        // Name + game + outcome line.
        var info = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(new TextBlock
        {
            Text = display.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("WhiteBrush"),
        });
        if (row.IsPrivateServer)
        {
            nameLine.Children.Add(new Border
            {
                Background = (Brush)FindResource("MagentaBrush"),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(8, 2, 0, 0),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "PRIVATE",
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("WhiteBrush"),
                },
            });
        }
        info.Children.Add(nameLine);

        var detail = $"{row.GameName ?? "(unknown game)"}";
        if (row.OutcomeHint is { Length: > 0 } hint)
        {
            detail += $"  ·  {hint}";
        }
        info.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 11,
            Foreground = (Brush)FindResource("MutedTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        // Right side: time-of-day + duration + optional "+ Bookmark game" button.
        var rightPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        rightPanel.Children.Add(new TextBlock
        {
            Text = row.LaunchedAtUtc.ToLocalTime().ToString("h:mm tt"),
            FontSize = 11,
            Foreground = (Brush)FindResource("WhiteBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
        });
        rightPanel.Children.Add(new TextBlock
        {
            Text = FormatDuration(row),
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 4),
        });

        if (row.PlaceId is long pid && pid > 0)
        {
            // Two states for the same slot — saved already vs not. Same widget so the row
            // doesn't reflow when the user clicks bookmark.
            if (_knownPlaceIds.Contains(pid))
            {
                rightPanel.Children.Add(new TextBlock
                {
                    Text = "Saved",
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("CyanBrush"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }
            else
            {
                var bookmark = new Button
                {
                    Content = "+ Bookmark",
                    Padding = new Thickness(8, 3, 8, 3),
                    FontSize = 10,
                    Background = (Brush)FindResource("NavyBrush"),
                    Foreground = (Brush)FindResource("CyanBrush"),
                    BorderBrush = (Brush)FindResource("CyanBrush"),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 2, 0, 0),
                    ToolTip = "Add this place to your saved games so you can launch into it any time.",
                    Tag = row,
                };
                bookmark.Click += OnBookmarkClick;
                rightPanel.Children.Add(bookmark);
            }
        }

        Grid.SetColumn(rightPanel, 2);
        grid.Children.Add(rightPanel);

        border.Child = grid;

        // F-072. Five TextBlocks inside an unnamed Border meant the automation tree showed 503
        // unpaired Text nodes across 100 sessions and not one container carrying a name — so a
        // screen reader announced "estehernandez", "Pet Sim", "4:57 PM", "1 min", "Saved" as five
        // unrelated fragments and left its listener to reassemble the row, a hundred times over.
        // Sighted users get that grouping free from geometry, which is exactly why it survived.
        //
        // The name is composed from what the row RENDERS, including the streamer-mode display
        // identity: announcing the real account while the screen shows an alias would defeat
        // streamer mode through the accessibility tree, which is the one route nobody watches.
        // The name goes on the row itself, which is a HistoryRowPresenter precisely so it HAS an
        // automation peer to carry it. Two earlier attempts are recorded there: a name on a plain
        // Border and then on a ContentControl both produced literally nothing in the tree, because
        // WPF builds peers for controls and neither is one.
        AutomationProperties.SetName(border, SessionHistoryRowName.Compose(
            display.Name,
            row.GameName,
            row.IsPrivateServer,
            row.LaunchedAtUtc.ToLocalTime().ToString("h:mm tt"),
            FormatDuration(row),
            row.OutcomeHint,
            row.PlaceId is { } placeId && _knownPlaceIds.Contains(placeId)));

        return border;
    }

    /// <summary>
    /// Bookmark a history row's place into the favorites store. Uses the row's recorded
    /// game name + a thumbnail fetched fresh from Roblox (best-effort). After save, reload
    /// the list so the row's state flips from "+ Bookmark" to "Saved".
    /// </summary>
    private async void OnBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LaunchSession row || row.PlaceId is not long placeId)
        {
            return;
        }
        btn.IsEnabled = false;
        var oldContent = btn.Content;
        btn.Content = "Saving...";
        try
        {
            // Fresh metadata fetch covers the case where the game name on the history row is
            // generic ("(unknown game)" / "Place 12345") — we want a real name + thumb in the
            // saved games list. Best-effort: fall back to the row's data if the API hiccups.
            string name = row.GameName ?? $"Place {placeId}";
            long universeId = 0;
            string thumbnail = string.Empty;
            try
            {
                var meta = await _api.GetGameMetadataByPlaceIdAsync(placeId);
                if (meta is not null)
                {
                    name = meta.Name;
                    universeId = meta.UniverseId;
                    thumbnail = meta.IconUrl;
                }
            }
            catch
            {
                // Use the fallbacks above.
            }

            await _favorites.AddAsync(placeId, universeId, name, thumbnail);
            _libraryChanged?.Invoke();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            btn.Content = oldContent;
            btn.IsEnabled = true;
            ShowMessage($"Couldn't bookmark: {ex.Message}", "Bookmark game",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string FormatDuration(LaunchSession row)
    {
        if (row.Duration is not TimeSpan d) return row.OutcomeHint is null ? "still running" : "—";
        if (d < TimeSpan.FromMinutes(1)) return "<1 min";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} min";
        return $"{(int)d.TotalHours}h {d.Minutes}m";
    }

    private async void OnClearClick(object sender, RoutedEventArgs e)
    {
        var confirm = ShowMessage(
            "Clear all session history? This can't be undone.",
            "Clear history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            await _store.ClearAsync();
            await ReloadAsync();

            // AFTER the reload, which sets its own status line. A clear that worked and a clear
            // that did nothing both used to leave the window looking identical — the rows vanish
            // either way when the store is unreadable, because an unreadable store lists as empty.
            //
            // Unless that reload ITSELF failed. Saying "History cleared." over a placeholder that
            // reads "History couldn't be read." would be two answers to one question, and the
            // reload's is the one the user can act on.
            if (_outcome != SessionHistoryOutcome.Unreadable)
            {
                StatusText.Text = SessionHistoryStatus.Cleared;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = SessionHistoryStatus.ClearFailed(ex.Message);
        }
    }
}
