using System.Globalization;

namespace ROROROblox.App.Localization;

/// <summary>
/// Code-side localization for composed strings (Phase D) — the ViewModel/formatter counterpart to
/// the XAML <see cref="LocExtension"/>. Both resolve through the one <see cref="TranslationSource"/>,
/// so a live culture toggle affects code lookups and XAML bindings alike. A ViewModel reads its text
/// through <see cref="Get"/>/<see cref="Format"/>/<see cref="Plural"/> and re-raises its display
/// properties on <see cref="TranslationSource.CultureChanged"/>.
/// </summary>
public static class Loc
{
    /// <summary>Resolve a key for the current UI culture.</summary>
    public static string Get(string key) => TranslationSource.Instance[key];

    /// <summary>Resolve a key and <see cref="string.Format(IFormatProvider, string, object?[])"/> it
    /// with the current culture (so numbers/dates format per the UI language too).</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(TranslationSource.Instance.CurrentCulture, TranslationSource.Instance[key], args);

    /// <summary>
    /// Resolve a plural family and format it. Picks the CLDR category for the current culture
    /// (<see cref="Plurals.Category"/>), looks up <c>{baseKey}_{category}</c>, and formats it with
    /// the count as <c>{0}</c> and any <paramref name="args"/> as <c>{1}, {2}, …</c> — so a plural
    /// string can carry extra placeholders (e.g. <c>"{0} accounts idle &gt; {1}m"</c>). A language
    /// supplies only the categories it needs (en/fr/de/es/pt: one+other; ru/pl: one+few+many); the
    /// lint guard enforces the family is complete.
    /// </summary>
    public static string Plural(string baseKey, long count, params object?[] args)
    {
        var culture = TranslationSource.Instance.CurrentCulture;
        var category = Plurals.Category(culture, count);
        var template = TranslationSource.Instance[$"{baseKey}_{category}"];
        if (args is null || args.Length == 0)
        {
            return string.Format(culture, template, count);
        }
        var all = new object?[args.Length + 1];
        all[0] = count;
        args.CopyTo(all, 1);
        return string.Format(culture, template, all);
    }
}
