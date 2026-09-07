using System.Globalization;

namespace ROROROblox.App.Localization;

/// <summary>
/// CLDR cardinal plural-category selection for the shipped languages (localization Phase D,
/// docs/store/localization-plan.md). The decision: a plural string is a FAMILY of resx keys, one
/// per category the language needs — <c>Key_one</c>/<c>Key_other</c> for en/fr/de/es/pt,
/// <c>Key_one</c>/<c>Key_few</c>/<c>Key_many</c> for ru/pl — and this picks the category. That
/// keeps every plural form a plain translatable string (the agents + verifier handle them like any
/// other) instead of embedding ICU MessageFormat syntax translators would have to work around, and
/// no new dependency.
///
/// <para>UI counts are always non-negative integers (accounts, minutes, sessions), so only the
/// integer branch of each CLDR rule is implemented — the fractional "other" arms of ru/pl never
/// apply here. An unrecognised language falls back to the English one/other split.</para>
/// </summary>
internal static class Plurals
{
    /// <summary>The category families each language must supply (drives the lint plural guard).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Required = new Dictionary<string, string[]>
    {
        ["en"] = ["one", "other"],
        ["fr"] = ["one", "other"],
        ["de"] = ["one", "other"],
        ["es"] = ["one", "other"],
        ["pt"] = ["one", "other"],
        ["ru"] = ["one", "few", "many"],
        ["pl"] = ["one", "few", "many"],
    };

    /// <summary>The CLDR cardinal category ("one" | "few" | "many" | "other") for this count in
    /// this culture. Non-negative-integer rules only (see class summary).</summary>
    public static string Category(CultureInfo culture, long count)
    {
        var n = Math.Abs(count);
        var m10 = n % 10;
        var m100 = n % 100;
        return culture.TwoLetterISOLanguageName switch
        {
            "ru" => m10 == 1 && m100 != 11 ? "one"
                  : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "few"
                  : "many",
            "pl" => n == 1 ? "one"
                  : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "few"
                  : "many",
            "fr" or "pt" => n is 0 or 1 ? "one" : "other",
            _ => n == 1 ? "one" : "other", // en, de, es, and the neutral fallback
        };
    }
}
