using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ROROROblox.App.Localization;
using ROROROblox.App.Modals;
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
    private readonly IAppSettings _settings;
    private readonly Func<string, Task<LaunchTarget?>> _resolveShareUrl;
    private readonly int _eligibleAccountCount;
    private readonly int _runningAccountCount;
    private readonly int _expiredAccountCount;
    private bool _suppressClickHandlers; // true while we set the initial check state.

    /// <summary>
    /// The target the user picked — null if the user closed without launching. Either a
    /// <see cref="LaunchTarget.PrivateServer"/> (saved or pasted) or, since v1.14, a
    /// <see cref="LaunchTarget.Place"/> pasted as a plain game link: the ViewModel lands the first
    /// account, reads which server it got, and sends the rest there.
    /// </summary>
    public LaunchTarget? SelectedTarget { get; private set; }

    public SquadLaunchWindow(
        IPrivateServerStore store,
        IRobloxApi api,
        IAppSettings settings,
        Func<string, Task<LaunchTarget?>> resolveShareUrl,
        int eligibleAccountCount,
        int runningAccountCount,
        int expiredAccountCount)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _resolveShareUrl = resolveShareUrl ?? throw new ArgumentNullException(nameof(resolveShareUrl));
        _eligibleAccountCount = eligibleAccountCount;
        _runningAccountCount = runningAccountCount;
        _expiredAccountCount = expiredAccountCount;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        EligibilityText.Text = BuildEligibilityText();
        _suppressClickHandlers = true;
        try
        {
            CarefulModeToggle.IsChecked = await _settings.GetCarefulSquadLaunchAsync();
        }
        finally
        {
            _suppressClickHandlers = false;
        }
        await RenderListAsync();
    }

    private async void OnCarefulModeToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        try
        {
            await _settings.SetCarefulSquadLaunchAsync(CarefulModeToggle.IsChecked == true);
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Pref_CouldntSavePreference", ex.Message);
            _suppressClickHandlers = true;
            CarefulModeToggle.IsChecked = await _settings.GetCarefulSquadLaunchAsync();
            _suppressClickHandlers = false;
        }
    }

    private string BuildEligibilityText()
    {
        var parts = new List<string>
        {
            Loc.Plural("Shell_Squad_Eligible", _eligibleAccountCount),
        };
        if (_runningAccountCount > 0) parts.Add(Loc.Format("Shell_Squad_Running", _runningAccountCount));
        if (_expiredAccountCount > 0) parts.Add(Loc.Format("Shell_Squad_Expired", _expiredAccountCount));
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
            StatusText.Text = Loc.Format("Shell_Squad_CouldntLoadServers", ex.Message);
            return;
        }

        if (servers.Count == 0)
        {
            SavedServersList.Children.Add(new TextBlock
            {
                Text = Loc.Get("Shell_Squad_NoSavedServers"),
                Foreground = (Brush)FindResource("MutedTextBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        // Default server first (pre-selected), then most-recently-launched. Pure + unit-tested.
        var sorted = SquadLaunchOrdering.Order(servers);
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
        if (server.IsDefault)
        {
            row.BorderBrush = (Brush)FindResource("CyanBrush");
            row.BorderThickness = new Thickness(1);
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // v1.3.x — server.RenderName falls back to Name when LocalName is null/empty.
        // Place placeholder kicks in only when both are empty (rare edge case).
        var renderName = !string.IsNullOrEmpty(server.RenderName)
            ? server.RenderName
            : Loc.Format("Shell_Squad_PlaceFallback", server.PlaceId);
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = renderName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("WhiteBrush"),
        });
        if (server.IsDefault)
        {
            var badge = new Border
            {
                Background = (Brush)FindResource("CyanBrush"),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(8, 1, 0, 0),
                Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock
                {
                    Text = Loc.Get("Shell_Squad_Badge_Default"),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("NavyBrush"),
                },
            };
            nameRow.Children.Add(badge);
        }
        info.Children.Add(nameRow);

        var subtitle = string.IsNullOrEmpty(server.PlaceName)
            ? Loc.Format("Shell_Squad_PlaceFallback", server.PlaceId)
            : server.PlaceName;
        if (server.LastLaunchedAt is { } last)
        {
            subtitle += Loc.Format("Shell_Squad_LastLaunched", RelativeAgo(last));
        }
        else
        {
            subtitle += Loc.Format("Shell_Squad_Added", RelativeAgo(server.AddedAt));
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

        // Takes the rank, and this one was the sharpest of the five. It is cyan-filled AND it ships
        // disabled whenever no account is eligible -- which is F-097's exact defect (a muted label
        // on a bright fill, measured at 1.37:1) at a site F-097's fix could not reach, because the
        // fix lives in a template this button was not using. Now it is.
        var launchBtn = new Button
        {
            Content = Loc.Get("Shell_Squad_LaunchAll"),
            Style = (Style)FindResource("CtaButtonStyle"),
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11,
            IsEnabled = _eligibleAccountCount > 0,
            ToolTip = _eligibleAccountCount > 0
                ? null
                : Loc.Get("Shell_Squad_NoEligibleTooltip"),
        };
        launchBtn.Click += async (_, _) => await OnLaunchSavedAsync(server);
        Grid.SetColumn(launchBtn, 1);
        grid.Children.Add(launchBtn);

        var removeBtn = new Button
        {
            Content = Loc.Get("Shell_Squad_Remove"),
            // The comment that used to sit here said it plainly at wave 5: "built in code, so wave
            // 5's markup sweep never saw it — and neither does any test in that wave, which all
            // parse XAML." That was written, left at one site, and then v1.20 shipped a brand-new
            // markup-only fence on top of it that claimed 99.1% coverage. The lesson was recorded
            // and not generalised, which is the more expensive half of missing it.
            // ButtonRankFenceTests now scans .cs construction too.
            Style = (Style)FindResource("SecondaryButtonStyle"),
            Padding = new Thickness(10, 6, 10, 6),
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
                StatusText.Text = Loc.Format("Shell_Games_CouldntRemove", ex.Message);
            }
        };
        Grid.SetColumn(removeBtn, 2);
        grid.Children.Add(removeBtn);

        // v1.3.x — right-click context menu for rename/reset. Existing Launch all + Remove
        // buttons stay; the rename actions are context-menu-only because they're rare.
        var menu = new ContextMenu();
        var renameItem = new MenuItem { Header = Loc.Get("Shell_Squad_Rename") };
        renameItem.Click += async (_, _) => await OnRenameSavedServerAsync(server);
        menu.Items.Add(renameItem);
        var resetItem = new MenuItem { Header = Loc.Get("Shell_Squad_ResetName"), IsEnabled = server.LocalName is not null };
        resetItem.Click += async (_, _) => await OnResetSavedServerNameAsync(server);
        menu.Items.Add(resetItem);
        row.ContextMenu = menu;

        row.Child = grid;
        return row;
    }

    private async Task OnRenameSavedServerAsync(SavedPrivateServer server)
    {
        var target = new RenameTarget(
            RenameTargetKind.PrivateServer,
            server.Id,
            server.Name,
            server.LocalName);
        var result = await RenameWindow.ShowAsync(this, target);
        if (result.Kind == RenameResultKind.Cancel)
        {
            return;
        }
        try
        {
            await _store.UpdateLocalNameAsync(server.Id, result.NewName);
            await RenderListAsync();
        }
        catch (KeyNotFoundException)
        {
            StatusText.Text = Loc.Get("Shell_Games_ServerNotSaved");
            await RenderListAsync();
        }
        catch (System.IO.IOException ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_CouldntSaveNameChangeDisk", ex.Message);
        }
    }

    private async Task OnResetSavedServerNameAsync(SavedPrivateServer server)
    {
        try
        {
            await _store.UpdateLocalNameAsync(server.Id, null);
            await RenderListAsync();
        }
        catch (KeyNotFoundException)
        {
            StatusText.Text = Loc.Get("Shell_Games_ServerNotSaved");
            await RenderListAsync();
        }
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
        SelectedTarget = new LaunchTarget.PrivateServer(server.PlaceId, server.Code, server.CodeKind);
        DialogResult = true;
        Close();
    }

    private async void OnAddAndLaunchClick(object sender, RoutedEventArgs e)
    {
        var input = UrlInput.Text?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            StatusText.Text = Loc.Get("Shell_Squad_PasteLinkFirst");
            return;
        }

        AddButton.IsEnabled = false;
        StatusText.Text = Loc.Get("Shell_Squad_Resolving");
        try
        {
            // Three URL forms supported via the resolver:
            //   privateServerLinkCode share URL  -> direct parse, LinkCode kind
            //   PlaceLauncher.ashx accessCode    -> direct parse, AccessCode kind
            //   roblox.com/share?code=X&type=Y   -> Roblox API resolve-link, LinkCode kind
            //   roblox.com/games/<id>            -> public place: the squad lands together in
            //                                       whichever server the first account gets (v1.14)
            var parsed = await _resolveShareUrl(input);

            if (parsed is LaunchTarget.Place place)
            {
                // Nothing to save — a public game isn't a server, and the server the squad ends up
                // in is decided at launch. Saving belongs to the Games library, not here.
                SelectedTarget = place;
                DialogResult = true;
                Close();
                return;
            }

            if (parsed is not LaunchTarget.PrivateServer ps)
            {
                StatusText.Text = Loc.Get("Shell_Squad_CouldntReadLink");
                return;
            }

            StatusText.Text = Loc.Get("Shell_Squad_LookingUp");
            var meta = await _api.GetGameMetadataByPlaceIdAsync(ps.PlaceId);
            var placeName = meta?.Name ?? $"Place {ps.PlaceId}";
            var thumbnail = meta?.IconUrl ?? string.Empty;

            // Default user-given name to the place name; the server row's Rename context-menu item
            // changes it later (no remove + re-add, which is what this comment said until 2026-08-30).
            var saved = await _store.AddAsync(ps.PlaceId, ps.Code, ps.Kind, placeName, placeName, thumbnail);
            await _store.TouchLastLaunchedAsync(saved.Id);

            SelectedTarget = ps;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Squad_CouldntAddLaunch", ex.Message);
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
        if (span < TimeSpan.Zero) return Loc.Get("Shell_Ago_InFuture");
        if (span < TimeSpan.FromMinutes(1)) return Loc.Get("Shell_Ago_JustNow");
        if (span < TimeSpan.FromHours(1)) return Loc.Format("Shell_Ago_Minutes", (int)span.TotalMinutes);
        if (span < TimeSpan.FromDays(1)) return Loc.Format("Shell_Ago_Hours", (int)span.TotalHours);
        if (span < TimeSpan.FromDays(7)) return Loc.Plural("Shell_Ago_Days", (int)span.TotalDays);
        return when.ToLocalTime().ToString("MMM d");
    }
}
