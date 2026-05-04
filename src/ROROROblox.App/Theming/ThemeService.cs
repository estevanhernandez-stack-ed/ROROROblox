using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;
using ROROROblox.Core.Theming;

namespace ROROROblox.App.Theming;

/// <summary>
/// App-side theme orchestrator. Reads the saved theme id at startup, looks it up via
/// <see cref="IThemeStore"/>, and mutates the application-level brush <c>Color</c> properties
/// so every <c>{StaticResource}</c> reference re-renders with the new colors. SolidColorBrush
/// is unfrozen by default — assigning to <c>Color</c> triggers WPF's render invalidation.
/// </summary>
internal sealed class ThemeService
{
    private readonly IThemeStore _store;
    private readonly IAppSettings _settings;
    private readonly ILogger<ThemeService> _log;

    public ThemeService(IThemeStore store, IAppSettings settings, ILogger<ThemeService>? log = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? NullLogger<ThemeService>.Instance;
    }

    public Theme? CurrentTheme { get; private set; }

    /// <summary>
    /// Synchronous startup apply. Called from <c>App.OnStartup</c> before any window resolves
    /// resources — must NOT use <c>await</c> on a context-capturing call, otherwise the UI
    /// thread we're already on can deadlock. Walks file IO inline; the saved-id lookup +
    /// theme list are both small JSON reads.
    /// </summary>
    public void ApplyAtStartup()
    {
        Theme? theme = null;
        try
        {
            // GetAwaiter().GetResult() is safe HERE because the underlying AppSettings call
            // ends in ConfigureAwait(false) → no UI-thread continuation needed for the gate.
            // (We keep this self-contained rather than threading a sync API through Core.)
            var savedId = _settings.GetActiveThemeIdAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(savedId))
            {
                theme = _store.GetByIdAsync(savedId).ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Reading active theme id at startup failed; falling back to brand.");
        }

        if (theme is null)
        {
            try
            {
                theme = _store.GetByIdAsync("brand").ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Loading built-in brand theme failed; brushes stay at XAML defaults.");
                return;
            }
        }
        if (theme is not null)
        {
            ApplyToResources(theme);
        }
    }

    public async Task SetActiveAsync(string themeId)
    {
        var theme = await _store.GetByIdAsync(themeId).ConfigureAwait(true);
        if (theme is null)
        {
            _log.LogWarning("Theme {Id} not found; ignoring.", themeId);
            return;
        }
        try
        {
            await _settings.SetActiveThemeIdAsync(themeId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Saving active theme id failed; applying live anyway.");
        }
        ApplyToResources(theme);
    }

    private void ApplyToResources(Theme theme)
    {
        // Marshal to the UI thread so it's safe to call from any context — settings change
        // handlers, startup boot, file watcher in a future build, etc.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ApplyToResources(theme));
            return;
        }

        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }
        ApplySlot(resources, ThemeSlots.Bg, theme.Bg);
        ApplySlot(resources, ThemeSlots.Cyan, theme.Cyan);
        ApplySlot(resources, ThemeSlots.Magenta, theme.Magenta);
        ApplySlot(resources, ThemeSlots.White, theme.White);
        ApplySlot(resources, ThemeSlots.MutedText, theme.MutedText);
        ApplySlot(resources, ThemeSlots.Divider, theme.Divider);
        ApplySlot(resources, ThemeSlots.RowBg, theme.RowBg);
        ApplySlot(resources, ThemeSlots.RowExpiredBg, theme.RowExpiredBg);
        ApplySlot(resources, ThemeSlots.RowExpiredAccent, theme.RowExpiredAccent);
        ApplySlot(resources, ThemeSlots.Navy, theme.Navy);
        CurrentTheme = theme;
        _log.LogInformation("Applied theme {Id} ({Name}).", theme.Id, theme.Name);
    }

    /// <summary>
    /// Always REPLACE the brush instance. The MainWindow consumes themed brushes via
    /// <c>{DynamicResource}</c> — DynamicResource subscribers re-bind when the dictionary
    /// entry changes, but ignore mutations to the held brush instance. Replacement is the
    /// only path that propagates to already-rendered visuals.
    /// </summary>
    private static void ApplySlot(ResourceDictionary resources, string key, string hex)
    {
        if (!TryParseHex(hex, out var color))
        {
            return;
        }
        resources[key] = new SolidColorBrush(color);
    }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrEmpty(hex)) return false;
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is Color c)
            {
                color = c;
                return true;
            }
        }
        catch
        {
        }
        return false;
    }
}
