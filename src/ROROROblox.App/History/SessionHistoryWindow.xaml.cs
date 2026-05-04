using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ROROROblox.Core;

namespace ROROROblox.App.History;

internal partial class SessionHistoryWindow : Window
{
    private readonly ISessionHistoryStore _store;
    private readonly IFavoriteGameStore _favorites;
    private readonly IRobloxApi _api;
    private HashSet<long> _knownPlaceIds = new();

    public SessionHistoryWindow(ISessionHistoryStore store, IFavoriteGameStore favorites, IRobloxApi api)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        HistoryList.Children.Clear();
        IReadOnlyList<LaunchSession> rows;
        try
        {
            rows = await _store.ListAsync();
        }
        catch
        {
            rows = [];
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

        if (rows.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;

        // Group by date (today / yesterday / older) for readability — same pattern as
        // chat-app message lists.
        var today = DateTimeOffset.Now.Date;
        var yesterday = today.AddDays(-1);
        string? lastBucket = null;

        foreach (var row in rows)
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

    private Border BuildRow(LaunchSession row)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("RowBgBrush"),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Avatar circle.
        var avatarBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = (Brush)FindResource("NavyBrush"),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(row.AccountAvatarUrl))
        {
            try
            {
                avatarBorder.Child = new System.Windows.Controls.Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(row.AccountAvatarUrl)),
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
            Text = row.AccountDisplayName,
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
                    ToolTip = "Add this place to your saved Games library so you can launch into it any time.",
                    Tag = row,
                };
                bookmark.Click += OnBookmarkClick;
                rightPanel.Children.Add(bookmark);
            }
        }

        Grid.SetColumn(rightPanel, 2);
        grid.Children.Add(rightPanel);

        border.Child = grid;
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
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            btn.Content = oldContent;
            btn.IsEnabled = true;
            MessageBox.Show(this, $"Couldn't bookmark: {ex.Message}", "Bookmark game",
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
        var confirm = MessageBox.Show(
            this,
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
        }
        catch
        {
            // best-effort
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
