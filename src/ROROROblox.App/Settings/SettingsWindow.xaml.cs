using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ROROROblox.Core;

namespace ROROROblox.App.Settings;

internal partial class SettingsWindow : Window
{
    private readonly IFavoriteGameStore _favorites;
    private readonly IRobloxApi _api;
    private readonly ObservableCollection<FavoriteGame> _items = [];

    public SettingsWindow(IFavoriteGameStore favorites, IRobloxApi api)
    {
        _favorites = favorites;
        _api = api;
        InitializeComponent();
        FavoritesList.ItemsSource = _items;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
        UrlInput.Focus();
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
            $"Remove {game?.Name ?? "this game"} from your library?",
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

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
