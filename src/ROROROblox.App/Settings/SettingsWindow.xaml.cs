using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ROROROblox.App.Modals;
using ROROROblox.Core;

namespace ROROROblox.App.Settings;

internal partial class SettingsWindow : Window
{
    private readonly IFavoriteGameStore _favorites;
    private readonly IPrivateServerStore _servers;
    private readonly IRobloxApi _api;
    private readonly ObservableCollection<FavoriteGame> _items = [];
    private readonly ObservableCollection<GameSearchResult> _searchItems = [];
    private readonly ObservableCollection<SavedPrivateServer> _serverItems = [];

    public SettingsWindow(IFavoriteGameStore favorites, IPrivateServerStore servers, IRobloxApi api)
    {
        _favorites = favorites;
        _servers = servers;
        _api = api;
        InitializeComponent();
        FavoritesList.ItemsSource = _items;
        SearchResultsList.ItemsSource = _searchItems;
        ServersList.ItemsSource = _serverItems;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
        await ReloadServersAsync();
        SearchInput.Focus();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSearchClick(sender, e);
            e.Handled = true;
        }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        var query = SearchInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            StatusText.Text = "Type a game name to search.";
            return;
        }

        SearchButton.IsEnabled = false;
        StatusText.Text = $"Searching for \"{query}\"...";

        try
        {
            var results = await _api.SearchGamesAsync(query);
            _searchItems.Clear();
            foreach (var r in results)
            {
                _searchItems.Add(r);
            }

            if (results.Count == 0)
            {
                SearchResultsContainer.Visibility = Visibility.Collapsed;
                StatusText.Text = $"No results for \"{query}\". Try a different name or paste a URL below.";
            }
            else
            {
                SearchResultsContainer.Visibility = Visibility.Visible;
                StatusText.Text = $"Found {results.Count} match{(results.Count == 1 ? "" : "es")}.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private async void OnAddSearchResultClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not GameSearchResult result)
        {
            return;
        }

        try
        {
            await _favorites.AddAsync(result.PlaceId, result.UniverseId, result.Name, result.IconUrl);
            StatusText.Text = $"Added {result.Name}.";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't add: {ex.Message}";
        }
    }

    private async Task ReloadAsync()
    {
        _items.Clear();
        var list = await _favorites.ListAsync();
        foreach (var fav in list)
        {
            _items.Add(fav);
        }
        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ReloadServersAsync()
    {
        _serverItems.Clear();
        var list = await _servers.ListAsync();
        foreach (var server in list)
        {
            _serverItems.Add(server);
        }
        ServersEmptyState.Visibility = _serverItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var input = UrlInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            StatusText.Text = "Paste a Roblox game URL or place id first.";
            return;
        }

        var placeId = RobloxLauncher.ExtractPlaceId(input);
        if (placeId is null)
        {
            StatusText.Text = "Couldn't find a place id in that input. Expected: roblox.com/games/<id>/<slug> or just <id>.";
            return;
        }

        AddButton.IsEnabled = false;
        StatusText.Text = "Looking up game...";

        try
        {
            var meta = await _api.GetGameMetadataByPlaceIdAsync(placeId.Value);
            if (meta is null)
            {
                StatusText.Text = $"Roblox didn't return metadata for place id {placeId}. Check the URL and try again.";
                return;
            }

            await _favorites.AddAsync(meta.PlaceId, meta.UniverseId, meta.Name, meta.IconUrl);
            UrlInput.Text = string.Empty;
            StatusText.Text = $"Added {meta.Name}.";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't add: {ex.Message}";
        }
        finally
        {
            AddButton.IsEnabled = true;
        }
    }

    private async void OnSetDefaultClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not long placeId)
        {
            return;
        }

        try
        {
            await _favorites.SetDefaultAsync(placeId);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't set default: {ex.Message}";
        }
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not long placeId)
        {
            return;
        }

        var game = _items.FirstOrDefault(f => f.PlaceId == placeId);
        var confirm = MessageBox.Show(
            this,
            $"Remove {game?.RenderName ?? "this game"} from your library?",
            "Remove game",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _favorites.RemoveAsync(placeId);
            await ReloadAsync();
            StatusText.Text = "Removed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't remove: {ex.Message}";
        }
    }

    private async void OnRemoveServerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Guid id)
        {
            return;
        }

        var server = _serverItems.FirstOrDefault(s => s.Id == id);
        var confirm = MessageBox.Show(
            this,
            $"Remove {server?.RenderName ?? "this server"} from your library?",
            "Remove server",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _servers.RemoveAsync(id);
            await ReloadServersAsync();
            StatusText.Text = "Removed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't remove: {ex.Message}";
        }
    }

    // ---------- v1.3.x — rename handlers (button + right-click context menu both target these) ----------

    private async void OnRenameGameClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not FavoriteGame game)
        {
            return;
        }

        var target = new RenameTarget(
            RenameTargetKind.Game,
            game.PlaceId,
            game.Name,
            game.LocalName);
        var result = await RenameWindow.ShowAsync(this, target);
        if (result.Kind == RenameResultKind.Cancel)
        {
            return;
        }
        try
        {
            await _favorites.UpdateLocalNameAsync(game.PlaceId, result.NewName);
            await ReloadAsync();
        }
        catch (KeyNotFoundException)
        {
            StatusText.Text = "That game isn't saved any more.";
            await ReloadAsync();
        }
        catch (System.IO.IOException ex)
        {
            StatusText.Text = $"Couldn't save name change. Disk error? ({ex.Message})";
        }
    }

    private async void OnResetGameNameClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not FavoriteGame game)
        {
            return;
        }
        try
        {
            await _favorites.UpdateLocalNameAsync(game.PlaceId, null);
            await ReloadAsync();
        }
        catch (KeyNotFoundException)
        {
            StatusText.Text = "That game isn't saved any more.";
            await ReloadAsync();
        }
    }

    private async void OnRenameServerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not SavedPrivateServer server)
        {
            return;
        }

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
            await _servers.UpdateLocalNameAsync(server.Id, result.NewName);
            await ReloadServersAsync();
        }
        catch (KeyNotFoundException)
        {
            StatusText.Text = "That server isn't saved any more.";
            await ReloadServersAsync();
        }
        catch (System.IO.IOException ex)
        {
            StatusText.Text = $"Couldn't save name change. Disk error? ({ex.Message})";
        }
    }

    private async void OnResetServerNameClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not SavedPrivateServer server)
        {
            return;
        }
        try
        {
            await _servers.UpdateLocalNameAsync(server.Id, null);
            await ReloadServersAsync();
        }
        catch (KeyNotFoundException)
        {
            StatusText.Text = "That server isn't saved any more.";
            await ReloadServersAsync();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
