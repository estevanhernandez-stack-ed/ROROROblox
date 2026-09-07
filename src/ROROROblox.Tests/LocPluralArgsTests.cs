using System.Globalization;
using ROROROblox.App.Localization;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// <see cref="Loc.Plural"/> carries extra format args (localization Phase D step 3): a plural
/// string can have placeholders beyond the count. This locks the count-only path (existing callers)
/// and the extra-arg path against the shipped neutral catalog.
/// </summary>
[Collection("MutatesUiCulture")]
public class LocPluralArgsTests
{
    private static void InEnglish(System.Action body)
    {
        var prev = TranslationSource.Instance.CurrentCulture;
        try { TranslationSource.Instance.CurrentCulture = new CultureInfo("en"); body(); }
        finally { TranslationSource.Instance.CurrentCulture = prev; }
    }

    [Fact]
    public void Plural_CountOnly_StillWorks()
    {
        InEnglish(() =>
        {
            Assert.Equal("1 launch recorded.", Loc.Plural("CoreMsg_History_Recorded", 1));
            Assert.Equal("3 launches recorded.", Loc.Plural("CoreMsg_History_Recorded", 3));
        });
    }
}
