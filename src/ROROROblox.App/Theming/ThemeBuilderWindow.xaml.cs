using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using ROROROblox.Core.Theming;

namespace ROROROblox.App.Theming;

/// <summary>
/// In-app theme builder. Mirrors Sanduhr's flow: a "Copy AI prompt" button puts the canonical
/// prompt on the clipboard so the user can paste into any chat AI; a multiline TextBox accepts
/// the JSON they paste back; "Save and apply" validates + persists + applies it. Removes the
/// file-fiddling step entirely so users can build themes without ever opening Notepad.
/// </summary>
internal partial class ThemeBuilderWindow : Window
{
    // The full embedded prompt is loaded once on first paint. ID matches the LogicalName in
    // ROROROblox.App.csproj's EmbeddedResource entry.
    private const string PromptResourceName = "ROROROblox.App.Themes.AGENT_PROMPT.md";

    private readonly IThemeStore _themeStore;
    private readonly ThemeService _themeService;
    private string? _cachedPromptText;

    /// <summary>The theme that was saved + applied. Null if the user cancelled.</summary>
    public Theme? SavedTheme { get; private set; }

    public ThemeBuilderWindow(IThemeStore themeStore, ThemeService themeService)
    {
        _themeStore = themeStore ?? throw new ArgumentNullException(nameof(themeStore));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        InitializeComponent();
        Loaded += (_, _) => JsonInput.Focus();
    }

    private void OnCopyPromptClick(object sender, RoutedEventArgs e)
    {
        var prompt = LoadPrompt();
        if (string.IsNullOrEmpty(prompt))
        {
            StatusText.Text = "Couldn't load the prompt — see logs.";
            return;
        }
        try
        {
            Clipboard.SetText(prompt);
            StatusText.Text = "Prompt copied. Paste it into Claude / ChatGPT / etc., describe a vibe, and paste the JSON it returns below.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Clipboard set failed: {ex.Message}";
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var json = JsonInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(json))
        {
            StatusText.Text = "Paste the JSON the AI returned first.";
            return;
        }
        SaveButton.IsEnabled = false;
        StatusText.Text = "Saving...";
        try
        {
            var theme = await _themeStore.SaveUserThemeAsync(json);
            await _themeService.SetActiveAsync(theme.Id);
            SavedTheme = theme;
            DialogResult = true;
            Close();
        }
        catch (InvalidThemeException ex)
        {
            // Validation failures get a friendly inline message — no stack, no MessageBox.
            StatusText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_themeStore.UserThemesFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = _themeStore.UserThemesFolder,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch
        {
            // best-effort
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Read the embedded AGENT_PROMPT.md. Falls back to a stub message if the resource is
    /// missing (project misconfiguration). Cached after first read to avoid repeat IO.
    /// </summary>
    private string LoadPrompt()
    {
        if (_cachedPromptText is not null)
        {
            return _cachedPromptText;
        }
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(PromptResourceName);
            if (stream is null)
            {
                _cachedPromptText = string.Empty;
                return _cachedPromptText;
            }
            using var reader = new StreamReader(stream);
            _cachedPromptText = reader.ReadToEnd();
            return _cachedPromptText;
        }
        catch
        {
            _cachedPromptText = string.Empty;
            return _cachedPromptText;
        }
    }
}
