using System.Globalization;
using System.Resources;
using ROROROblox.Core;

namespace ROROROblox.App.Localization;

/// <summary>
/// The UI-language surface (localization, 2026-09-07): which languages the app can actually
/// display, the startup culture bootstrap, and the native display names the picker shows.
///
/// <para><b>The AVAILABLE-list guard.</b> A language is offered ONLY when its satellite catalog
/// (<c>Strings.&lt;culture&gt;.resources</c>) actually ships — probed against the resource
/// manager, not assumed. So the picker is honest by construction: with no translated catalogs
/// yet it offers English alone; each catalog that lands makes its language appear, and the
/// package manifest declares exactly the languages that ship. English is always available (the
/// neutral catalog is compiled in).</para>
///
/// <para><b>Startup order.</b> The XAML binds its strings once (<c>x:Static</c>), so the UI
/// culture must be set before ANY window loads — earlier than the theme. The bootstrap runs in
/// <c>Program.Main</c>, before <c>new App()</c>, reading the saved value synchronously.</para>
/// </summary>
internal static class UiCulture
{
    /// <summary>English, plus the wave-1 languages (matching the Store listing set). Order is the
    /// picker order; English first.</summary>
    public static readonly IReadOnlyList<CultureOption> Candidates =
    [
        new("", "English"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("ru", "Русский"),
        new("pt-BR", "Português (Brasil)"),
        new("pl", "Polski"),
        new("es", "Español"),
    ];

    private static readonly ResourceManager Probe =
        new("ROROROblox.App.Properties.Strings", typeof(UiCulture).Assembly);

    /// <summary>
    /// The candidates whose catalog actually ships. English (the empty culture) is always in;
    /// each other candidate is included only when a satellite resource set exists for it WITHOUT
    /// falling back to the neutral one. Never throws — a probe failure just excludes that
    /// candidate.
    /// </summary>
    public static IReadOnlyList<CultureOption> Available()
    {
        var available = new List<CultureOption>();
        foreach (var c in Candidates)
        {
            if (c.CultureName.Length == 0)
            {
                available.Add(c); // English — the neutral catalog is compiled in.
                continue;
            }
            if (HasCatalog(c.CultureName)) available.Add(c);
        }
        return available;
    }

    /// <summary>True when a satellite catalog for this culture is present (no neutral fallback).</summary>
    public static bool HasCatalog(string cultureName)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            // tryParents: false — a 'fr' probe must not be satisfied by the neutral catalog, or
            // every culture would read as available.
            return Probe.GetResourceSet(culture, createIfNotExists: true, tryParents: false) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Apply the saved UI language to the current thread and to every future thread, before any
    /// UI is constructed. Called from <c>Program.Main</c>. A saved value whose catalog is not
    /// available (a rolled-back build, a hand-edited settings file) is ignored — the app stays on
    /// the OS default rather than pinning a language it cannot render past English fallback.
    /// Best-effort: never throws into Main.
    /// </summary>
    public static void ApplyFromSettings(string? filePath = null)
    {
        try
        {
            var saved = AppSettings.ReadUiLanguageFast(filePath);
            if (string.IsNullOrWhiteSpace(saved) || !HasCatalog(saved)) return;
            var culture = CultureInfo.GetCultureInfo(saved);
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture; // startup owns the process default
            TranslationSource.Instance.CurrentCulture = culture;  // so {loc:Loc} renders it from the first frame
        }
        catch
        {
            // Fall through to the OS default — a bad culture must never stop the app starting.
        }
    }
}

/// <summary>A selectable UI language: the culture name persisted (empty = follow OS / English)
/// and the native name the picker shows.</summary>
internal sealed record CultureOption(string CultureName, string DisplayName);
