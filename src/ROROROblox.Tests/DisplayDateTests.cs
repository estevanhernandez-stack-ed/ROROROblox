using System.Globalization;
using ROROROblox.App.Input;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;

namespace ROROROblox.Tests;

/// <summary>
/// Stability part two, item 6. Dates past a week and the About shortcut list read English in every
/// language: the app sets the UI culture only, so a bare <c>ToString("MMM d")</c> used the Windows
/// regional culture. These pin the helper that formats for the UI language and the shortcut keys.
/// Tests that touch the process-wide <see cref="TranslationSource"/> sit in the serial collection
/// and restore the culture in a finally.
/// </summary>
[Collection("MutatesUiCulture")]
public class DisplayDateTests
{
    private static DateTimeOffset Nov3At1545 =>
        new(new DateTime(2026, 11, 3, 15, 45, 0, DateTimeKind.Local));

    [Fact]
    public void English_IsExactlyWhatTheLiteralsProduced()
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("Nov 3", DisplayDate.ShortMonthDay(Nov3At1545, en));
        Assert.Equal("Tuesday, November 3", DisplayDate.WeekdayMonthDay(new DateTime(2026, 11, 3), en));
        Assert.Equal("3:45 PM", DisplayDate.TimeOfDay(Nov3At1545, en));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("pl")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    public void EveryShippedLanguage_FormatsDayFirstAndNotInEnglish(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        var shortDate = DisplayDate.ShortMonthDay(Nov3At1545, culture);

        // All six write the day before the month, which the English "MMM d" literal never did.
        Assert.StartsWith("3", shortDate);
        Assert.DoesNotContain("Nov ", shortDate);
        Assert.NotEqual("Nov 3", shortDate);
        // 24-hour clocks: no AM/PM marker in any of the six.
        Assert.DoesNotContain("PM", DisplayDate.TimeOfDay(Nov3At1545, culture), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("15", DisplayDate.TimeOfDay(Nov3At1545, culture));
    }

    [Fact]
    public void ShortMonthDay_FollowsTheUiLanguage_NotTheRegionalCulture()
    {
        var originalUi = TranslationSource.Instance.CurrentCulture;
        var originalFormat = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); // an English-region machine
            TranslationSource.Instance.CurrentCulture = CultureInfo.GetCultureInfo("fr");

            Assert.StartsWith("3", DisplayDate.ShortMonthDay(Nov3At1545));
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = originalUi;
            CultureInfo.CurrentCulture = originalFormat;
        }
    }

    [Fact]
    public void RelativeAgo_PastAWeek_UsesTheUiLanguage()
    {
        var originalUi = TranslationSource.Instance.CurrentCulture;
        var originalFormat = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var when = Nov3At1545.ToUniversalTime();
            AccountSummary.ClockOverride.Value = () => when.AddDays(10);

            TranslationSource.Instance.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Nov 3", AccountSummary.RelativeAgo(when));

            TranslationSource.Instance.CurrentCulture = CultureInfo.GetCultureInfo("de");
            var de = AccountSummary.RelativeAgo(when);
            Assert.StartsWith("3.", de);
        }
        finally
        {
            AccountSummary.ClockOverride.Value = null;
            TranslationSource.Instance.CurrentCulture = originalUi;
            CultureInfo.CurrentCulture = originalFormat;
        }
    }

    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("pl")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    public void ShortcutDescriptions_FollowTheUiLanguage_AndKeyTextDoesNot(string name)
    {
        var original = TranslationSource.Instance.CurrentCulture;
        try
        {
            TranslationSource.Instance.CurrentCulture = CultureInfo.InvariantCulture;
            var english = KeyboardVocabulary.Shortcuts.Select(s => s.Label).ToList();
            var gestures = KeyboardVocabulary.Shortcuts.Select(s => s.GestureText).ToList();

            TranslationSource.Instance.CurrentCulture = CultureInfo.GetCultureInfo(name);
            var translated = KeyboardVocabulary.Shortcuts.Select(s => s.Label).ToList();

            Assert.All(translated, l => Assert.False(string.IsNullOrWhiteSpace(l)));
            Assert.DoesNotContain(KeyboardVocabulary.Shortcuts, s => s.Label == s.LabelKey); // no key leaked through
            // The one description every language writes differently from English.
            var keyboardRow = KeyboardVocabulary.Shortcuts.Single(s => s.Action == ShortcutAction.OpenShortcutsList);
            Assert.NotEqual("Keyboard shortcuts (this list)", keyboardRow.Label);
            Assert.Equal(gestures, KeyboardVocabulary.Shortcuts.Select(s => s.GestureText).ToList());
            Assert.Equal(english.Count, translated.Count);
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = original;
        }
    }

    [Fact]
    public void EveryShortcutKey_ExistsInEnglish()
    {
        var original = TranslationSource.Instance.CurrentCulture;
        try
        {
            TranslationSource.Instance.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (var s in KeyboardVocabulary.Shortcuts)
            {
                Assert.NotEqual(s.LabelKey, s.Label); // a missing key resolves to itself
            }
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = original;
        }
    }
}
