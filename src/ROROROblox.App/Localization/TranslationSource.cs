using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;

namespace ROROROblox.App.Localization;

/// <summary>
/// The live-toggle localization provider (localization Phase D, docs/store/localization-plan.md).
/// XAML binds strings to this singleton's indexer via <see cref="LocExtension"/> (<c>{loc:Loc Key}</c>);
/// setting <see cref="CurrentCulture"/> raises the indexer change so every bound element re-pulls and
/// the whole UI re-renders in the new language, with no restart. This is why Phase D moves off
/// <c>{x:Static loc:Strings.Key}</c> (a load-time-static value that cannot react to a culture change —
/// the reason v1.26 applied only on restart).
///
/// <para>Code paths that compose strings (ViewModels) read through <see cref="ROROROblox.App.Localization.Loc"/>
/// and re-raise their own display properties on <see cref="CultureChanged"/>.</para>
/// </summary>
public sealed class TranslationSource : INotifyPropertyChanged
{
    public static TranslationSource Instance { get; } = new();

    private readonly ResourceManager _rm =
        new("ROROROblox.App.Properties.Strings", typeof(TranslationSource).Assembly);

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    private TranslationSource() { }

    /// <summary>The active UI culture. Setting it flips the whole UI live: it updates the UI thread's
    /// culture (so not-yet-migrated <c>x:Static</c> and framework lookups on this thread agree),
    /// raises the indexer change (so existing <c>{loc:Loc}</c> bindings refresh), and fires
    /// <see cref="CultureChanged"/> (so ViewModels re-raise composed strings).</summary>
    public CultureInfo CurrentCulture
    {
        get => _culture;
        set
        {
            if (Equals(_culture, value)) return;
            _culture = value;
            // The UI thread's culture only. The PROCESS default (DefaultThreadCurrentUICulture) is a
            // STARTUP concern owned by UiCulture.ApplyFromSettings; a runtime toggle must not mutate
            // process-global state — it would leak across threads (including parallel test runs).
            Thread.CurrentThread.CurrentUICulture = value;
            // Binding.IndexerName ("Item[]") tells WPF every indexer binding on this source changed.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Resolve a key for the current culture; the key itself is the last-resort fallback so
    /// a missing string is visible, never a blank.</summary>
    public string this[string key] => _rm.GetString(key, _culture) ?? key;

    /// <summary>Fires after <see cref="CurrentCulture"/> changes — ViewModels subscribe to re-raise
    /// their composed display properties.</summary>
    public event EventHandler? CultureChanged;

    public event PropertyChangedEventHandler? PropertyChanged;
}
