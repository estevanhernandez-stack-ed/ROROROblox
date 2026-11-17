using System.Globalization;

namespace ROROROblox.App.Localization;

/// <summary>
/// Dates and times shown to the user, formatted for the UI language. The app never sets
/// <c>CurrentCulture</c> (only the UI culture, via <see cref="UiCulture"/> and
/// <see cref="TranslationSource"/>), so a bare <c>ToString("MMM d")</c> formats with the Windows
/// regional setting and reads English for a French UI on an English-region machine. Every date
/// that reaches the screen goes through here instead. Dates written to files or logs do not:
/// those stay culture-invariant.
/// <para>
/// Nothing here changes <c>CurrentCulture</c> globally, which would also change parsing and file
/// formats across the app.
/// </para>
/// </summary>
internal static class DisplayDate
{
    private static CultureInfo Ui => TranslationSource.Instance.CurrentCulture;

    /// <summary>"Nov 3" in English, "3 nov." in French: the culture's month-day order with the
    /// month abbreviated. Local time.</summary>
    public static string ShortMonthDay(DateTimeOffset when, CultureInfo? culture = null)
    {
        culture ??= Ui;
        // MonthDayPattern is the full-month form ("MMMM d", "d. MMMM", "d 'de' MMMM"). Dropping
        // one M keeps the order and any literal text the language needs.
        var pattern = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);
        return when.ToLocalTime().ToString(pattern, culture);
    }

    /// <summary>"Monday, November 3" in English; weekday plus the culture's month-day order.</summary>
    public static string WeekdayMonthDay(DateTime localDate, CultureInfo? culture = null)
    {
        culture ??= Ui;
        return localDate.ToString("dddd, " + culture.DateTimeFormat.MonthDayPattern, culture);
    }

    /// <summary>"3:45 PM" in English, "15:45" where the language uses 24 hours. Local time.</summary>
    public static string TimeOfDay(DateTimeOffset when, CultureInfo? culture = null)
        => when.ToLocalTime().ToString("t", culture ?? Ui);
}
