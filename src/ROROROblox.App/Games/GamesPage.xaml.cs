using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ROROROblox.App.Localization;
using ROROROblox.App.Modals;
using ROROROblox.Core;

namespace ROROROblox.App.Games;

/// <summary>
/// The Games destination, hosted by the shell (F-013 — formerly <c>GamesWindow</c>). <c>Loaded</c>
/// refires on every navigation back here, so each visit reloads the stores — the same freshness
/// the per-open window had.
/// </summary>
internal partial class GamesPage : UserControl
{
    private readonly IFavoriteGameStore _favorites;
    private readonly IPrivateServerStore _servers;
    private readonly IRobloxApi _api;
    private readonly ObservableCollection<FavoriteGame> _items = [];
    private readonly ObservableCollection<GameSearchResult> _searchItems = [];
    private readonly ObservableCollection<SavedPrivateServer> _serverItems = [];

    // The old modal reloaded the view model's library once, when ShowDialog returned. A shell page
    // has no close moment, so the composition root hands in the refresh and the page invokes it
    // whenever its own lists reload — every mutation path already reloads, so every mutation
    // reaches the main window without waiting for anything to close (F-013).
    private readonly Action? _libraryChanged;

    public GamesPage(IFavoriteGameStore favorites, IPrivateServerStore servers, IRobloxApi api,
        Action? libraryChanged = null)
    {
        _favorites = favorites;
        _servers = servers;
        _api = api;
        _libraryChanged = libraryChanged;
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
            StatusText.Text = Loc.Get("Shell_Games_TypeToSearch");
            return;
        }

        SearchButton.IsEnabled = false;
        StatusText.Text = Loc.Format("Shell_Games_Searching", query);

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
                StatusText.Text = Loc.Format("Shell_Games_NoResults", query);
            }
            else
            {
                SearchResultsContainer.Visibility = Visibility.Visible;
                StatusText.Text = Loc.Plural("Shell_Games_FoundMatches", results.Count);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_SearchFailed", ex.Message);
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
            StatusText.Text = Loc.Format("Shell_Games_Added", result.Name);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_CouldntAdd", ex.Message);
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
        _libraryChanged?.Invoke();
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
        _libraryChanged?.Invoke();
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var input = UrlInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            StatusText.Text = Loc.Get("Shell_Games_PasteUrlFirst");
            return;
        }

        var placeId = RobloxLauncher.ExtractPlaceId(input);
        if (placeId is null)
        {
            StatusText.Text = Loc.Get("Shell_Games_NoPlaceId");
            return;
        }

        AddButton.IsEnabled = false;
        StatusText.Text = Loc.Get("Shell_Games_LookingUp");

        try
        {
            var meta = await _api.GetGameMetadataByPlaceIdAsync(placeId.Value);
            if (meta is null)
            {
                StatusText.Text = Loc.Format("Shell_Games_NoMetadata", placeId);
                return;
            }

            await _favorites.AddAsync(meta.PlaceId, meta.UniverseId, meta.Name, meta.IconUrl);
            UrlInput.Text = string.Empty;
            StatusText.Text = Loc.Format("Shell_Games_Added", meta.Name);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_CouldntAdd", ex.Message);
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
            StatusText.Text = Loc.Format("Shell_Games_CouldntSetDefault", ex.Message);
        }
    }

    private async void OnClearDefaultClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _favorites.ClearDefaultAsync();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_CouldntClearDefault", ex.Message);
        }
    }

    private async void OnSetDefaultServerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Guid id)
        {
            return;
        }

        try
        {
            await _servers.SetDefaultAsync(id);
            await ReloadServersAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_CouldntSetDefaultServer", ex.Message);
        }
    }

    private async void OnClearDefaultServerClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _servers.ClearDefaultAsync();
            await ReloadServersAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_CouldntClearDefaultServer", ex.Message);
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
            Window.GetWindow(this),
            Loc.Format("Shell_Games_RemoveGameConfirm", game?.RenderName ?? Loc.Get("Shell_Games_ThisGame")),
            Loc.Get("Shell_Games_RemoveGameTitle"),
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
            StatusText.Text = Loc.Get("Shell_Games_Removed");
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_CouldntRemove", ex.Message);
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
            Window.GetWindow(this),
            Loc.Format("Shell_Games_RemoveServerConfirm", server?.RenderName ?? Loc.Get("Shell_Games_ThisServer")),
            Loc.Get("Shell_Games_RemoveServerTitle"),
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
            StatusText.Text = Loc.Get("Shell_Games_Removed");
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_CouldntRemove", ex.Message);
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
        var result = await RenameWindow.ShowAsync(Window.GetWindow(this)!, target);
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
            StatusText.Text = Loc.Get("Shell_Msg_GameNotSaved");
            await ReloadAsync();
        }
        catch (System.IO.IOException ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_CouldntSaveNameChangeDisk", ex.Message);
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
            StatusText.Text = Loc.Get("Shell_Msg_GameNotSaved");
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
        var result = await RenameWindow.ShowAsync(Window.GetWindow(this)!, target);
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
            StatusText.Text = Loc.Get("Shell_Games_ServerNotSaved");
            await ReloadServersAsync();
        }
        catch (System.IO.IOException ex)
        {
            StatusText.Text = Loc.Format("Shell_Games_CouldntSaveNameChangeDisk", ex.Message);
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
            StatusText.Text = Loc.Get("Shell_Games_ServerNotSaved");
            await ReloadServersAsync();
        }
    }
}
