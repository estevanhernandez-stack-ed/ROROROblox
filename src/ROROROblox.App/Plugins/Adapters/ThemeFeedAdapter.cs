using ROROROblox.Core.Theming;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Bridges the app's theme service to the plugin event bus, and holds the latest palette so a
/// plugin can ask for it at any moment rather than only hearing about changes.
/// <para>
/// <b>Why an adapter and not an injected bus.</b> <c>ThemeService</c> raises a plain
/// <c>Action&lt;ResolvedPalette&gt;</c> and knows nothing about plugins. Handing it an
/// <see cref="IPluginEventBus"/> would point <c>Theming</c> at <c>Plugins</c> and invert the
/// direction every other bridge in this folder runs. The seam belongs on the plugin side.
/// </para>
/// <para>
/// <b>The cache is not an optimisation.</b> It is what makes <c>GetTheme</c> answerable before the
/// user has ever changed a theme — which is nearly always, since most sessions never touch the
/// picker. A subscribe-only feed would leave a plugin on its fallback colour indefinitely.
/// </para>
/// </summary>
public sealed class ThemeFeedAdapter : IThemePaletteSource, IDisposable
{
    private readonly IThemeAppliedSource _themes;
    private readonly InProcessPluginEventBus _bus;
    private bool _disposed;

    public ThemeFeedAdapter(IThemeAppliedSource themes, InProcessPluginEventBus bus)
    {
        _themes = themes ?? throw new ArgumentNullException(nameof(themes));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));

        // Seeded before subscribing. On the real startup path ApplyAtStartup has already run by
        // the time DI builds this (App.OnStartup applies the theme long before the plugin host
        // binds its pipe), so Latest is populated from the first moment anything can read it.
        Latest = _themes.CurrentPalette;
        _themes.ThemeApplied += OnThemeApplied;
    }

    /// <summary>The palette currently on screen, or null if no theme has been applied yet.</summary>
    public ResolvedPalette? Latest { get; private set; }

    private void OnThemeApplied(ResolvedPalette palette)
    {
        Latest = palette;
        _bus.RaiseThemeChanged(palette);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _themes.ThemeApplied -= OnThemeApplied;
    }
}

/// <summary>
/// The host's current palette, for whoever needs to answer "what colour are you right now".
/// Exists so <c>PluginHostService</c> depends on the question rather than on the WPF-flavoured
/// class that happens to answer it.
/// </summary>
public interface IThemePaletteSource
{
    ResolvedPalette? Latest { get; }
}
