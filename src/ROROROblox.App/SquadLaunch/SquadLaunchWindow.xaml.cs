using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ROROROblox.Core;

namespace ROROROblox.App.SquadLaunch;

/// <summary>
/// Modal that lets the user pick a private server (saved or pasted) and signal "launch all
/// eligible accounts here." MainViewModel reads <see cref="SelectedTarget"/> after
/// <see cref="Window.ShowDialog"/> returns and dispatches the mass launch.
/// </summary>
internal partial class SquadLaunchWindow : Window
{
    private readonly IPrivateServerStore _store;
    private readonly IRobloxApi _api;
    private readonly int _eligibleAccountCount;
    private readonly int _runningAccountCount;
    private readonly int _expiredAccountCount;

    /// <summary>The target the user picked — null if the user closed without launching.</summary>
    public LaunchTarget.PrivateServer? SelectedTarget { get; private set; }

    public SquadLaunchWindow(
        IPrivateServerStore store,
        IRobloxApi api,
        int eligibleAccountCount,
        int runningAccountCount,
        int expiredAccountCount)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _eligibleAccountCount = eligibleAccountCount;
        _runningAccountCount = runningAccountCount;
        _expiredAccountCount = expiredAccountCount;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        EligibilityText.Text = BuildEligibilityText();
        await RenderListAsync();
    }

    private string BuildEligibilityText()
    {
        var parts = new List<string>
        {
            $"{_eligibleAccountCount} eligible {(_eligibleAccountCount == 1 ? "account" : "accounts")}",
        };
        if (_runningAccountCount > 0) parts.Add($"{_runningAccountCount} running (skipped)");
        if (_expiredAccountCount > 0) parts.Add($"{_expiredAccountCount} expired (skipped)");
        return string.Join(" · ", parts);
    }

    private async Task RenderListAsync()
    {
        SavedServersList.Children.Clear();
        IReadOnlyList<SavedPrivateServer> servers;
        try
        {
            servers = await _store.ListAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't load saved servers: {ex.Message}";
            return;
        }

        if (servers.Count == 0)
        {
            SavedServersList.Children.Add(new TextBlock
            {
                Text = "No saved servers yet. Paste a private server link below to add one — it'll save for next time.",
                Foreground = (Brush)FindResource("MutedTextBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        // Most-recently-launched first; ties fall back to addedAt.
        var sorted = servers.OrderByDescending(s => s.LastLaunchedAt ?? s.AddedAt);
        foreach (var server in sorted)
        {
            SavedServersList.Children.Add(BuildServerRow(server));
        }
    }

    private Border BuildServerRow(SavedPrivateServer server)
    {
        var row = new Border
        {
            Background = (Brush)FindResource("RowBgBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(server.Name) ? $"Place {server.PlaceId}" : server.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("WhiteBrush"),
        });

        var subtitle = string.IsNullOrEmpty(server.PlaceName)
            ? $"Place {server.PlaceId}"
            : server.PlaceName;
        if (server.LastLaunchedAt is { } last)
        {
            subtitle += $" · last launched {RelativeAgo(last)}";
        }
        else
        {
            subtitle += $" · added {RelativeAgo(server.AddedAt)}";
        }
        info.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = (Brush)FindResource("MutedTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var launchBtn = new Button
        {
            Content = "Launch all",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 6, 0),
            Background = (Brush)FindResource("CyanBrush"),
            Foreground = (Brush)FindResource("NavyBrush"),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            IsEnabled = _eligibleAccountCount > 0,
            ToolTip = _eligibleAccountCount > 0
                ? null
                : "No eligible accounts. Re-authenticate expired sessions or close running clients first.",
        };
        launchBtn.Click += async (_, _) => await OnLaunchSavedAsync(server);
        Grid.SetColumn(launchBtn, 1);
        grid.Children.Add(launchBtn);

        var removeBtn = new Button
        {
            Content = "Remove",
            Padding = new Thickness(10, 6, 10, 6),
            Background = (Brush)FindResource("NavyBrush"),
            Foreground = (Brush)FindResource("MutedTextBrush"),
            BorderBrush = (Brush)FindResource("DividerBrush"),
            BorderThickness = new Thickness(1),
            FontSize = 11,
        };
        removeBtn.Click += async (_, _) =>
        {
            try
            {
                await _store.RemoveAsync(server.Id);
                await RenderListAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Couldn't remove: {ex.Message}";
            }
        };
        Grid.SetColumn(removeBtn, 2);
        grid.Children.Add(removeBtn);

        row.Child = grid;
        return row;
    }

    private async Task OnLaunchSavedAsync(SavedPrivateServer server)
    {
        try
        {
            await _store.TouchLastLaunchedAsync(server.Id);
        }
        catch
        {
            // touch failure is cosmetic — proceed with launch anyway.
        }
        SelectedTarget = new LaunchTarget.PrivateServer(server.PlaceId, server.AccessCode);
        DialogResult = true;
        Close();
    }

    private async void OnAddAndLaunchClick(object sender, RoutedEventArgs e)
    {
        var input = UrlInput.Text?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            StatusText.Text = "Paste a private server share URL first.";
            return;
        }

        var parsed = LaunchTarget.FromUrl(input);
        if (parsed is not LaunchTarget.PrivateServer ps)
        {
            StatusText.Text = "That doesn't look like a private server link. The URL must include " +
                              "?privateServerLinkCode=... — copy the share link from the VIP server's page.";
            return;
        }

        AddButton.IsEnabled = false;
        StatusText.Text = "Looking up game info...";
        try
        {
            var meta = await _api.GetGameMetadataByPlaceIdAsync(ps.PlaceId);
            var placeName = meta?.Name ?? $"Place {ps.PlaceId}";
            var thumbnail = meta?.IconUrl ?? string.Empty;

            // Default user-given name to the place name; users can rename later by remove + re-add.
            var saved = await _store.AddAsync(ps.PlaceId, ps.AccessCode, placeName, placeName, thumbnail);
            await _store.TouchLastLaunchedAsync(saved.Id);

            SelectedTarget = ps;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't add and launch: {ex.Message}";
        }
        finally
        {
            AddButton.IsEnabled = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static string RelativeAgo(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span < TimeSpan.Zero) return "in the future";
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} hr ago";
        if (span < TimeSpan.FromDays(7)) return $"{(int)span.TotalDays} days ago";
        return when.ToLocalTime().ToString("MMM d");
    }
}
