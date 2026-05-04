using System.Windows;
using ROROROblox.Core;

namespace ROROROblox.App.Settings;

internal partial class SettingsWindow : Window
{
    private readonly IAppSettings _settings;

    public SettingsWindow(IAppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var url = await _settings.GetDefaultPlaceUrlAsync();
        PlaceUrlInput.Text = url ?? string.Empty;
        PlaceUrlInput.Focus();
        PlaceUrlInput.SelectAll();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var url = PlaceUrlInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(
                this,
                "Paste a Roblox game URL first, or click Cancel.",
                "Default game URL is empty",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            await _settings.SetDefaultPlaceUrlAsync(url);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(
                this,
                $"Couldn't save settings: {ex.Message}",
                "Save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
