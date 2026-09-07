using ROROROblox.App.Localization;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The UI-language surface is honest by construction (localization, 2026-09-07): a language is
/// offered ONLY when its catalog actually ships. Until the translated `Strings.&lt;culture&gt;.resx`
/// files land, English is the only option — which is exactly the never-lie rule (don't offer a
/// language the app can't render) expressed as a runtime property. These lock it so a picker
/// bug can't start advertising empty languages.
/// </summary>
public class UiCultureTests
{
    [Fact]
    public void WithNoTranslatedCatalogs_OnlyEnglishIsAvailable()
    {
        // No Strings.<culture>.resx ship yet, so the satellite probe finds nothing and the
        // AVAILABLE list is English alone. When a catalog is added, this expectation updates in
        // the same commit — a ceiling that moves with reality, like the app's other fences.
        var available = UiCulture.Available();

        Assert.Single(available);
        Assert.Equal("", available[0].CultureName);
        Assert.Equal("English", available[0].DisplayName);
    }

    [Fact]
    public void EnglishIsAlwaysAvailableAndCandidatesCoverTheWaveOneSet()
    {
        var names = UiCulture.Candidates.Select(c => c.CultureName).ToList();
        Assert.Equal("", names[0]); // English first
        foreach (var wave1 in new[] { "fr", "de", "ru", "pt-BR", "pl", "es" })
            Assert.Contains(wave1, names);
    }

    [Fact]
    public void HasCatalog_IsFalseForAnUnshippedCulture_AndNeverThrows()
    {
        Assert.False(UiCulture.HasCatalog("fr"));
        Assert.False(UiCulture.HasCatalog("not-a-culture")); // bad name → false, no throw
    }

    [Fact]
    public void ApplyFromSettings_IgnoresACultureWhoseCatalogIsNotShipped()
    {
        // A settings file naming an unshipped culture must not pin the thread to it (English
        // fallback would render anyway, but the contract is: never apply an unavailable culture).
        var before = System.Threading.Thread.CurrentThread.CurrentUICulture;
        var tmp = Path.Combine(Path.GetTempPath(), $"rr-uiculture-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tmp, "{\"uiLanguage\":\"fr\"}");
            UiCulture.ApplyFromSettings(tmp);
            Assert.Equal(before, System.Threading.Thread.CurrentThread.CurrentUICulture);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentUICulture = before;
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void ReadUiLanguageFast_RoundTripsAndDefaultsToNull()
    {
        Assert.Null(AppSettings.ReadUiLanguageFast(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")));

        var tmp = Path.Combine(Path.GetTempPath(), $"rr-uilang-{Guid.NewGuid():N}.json");
        try
        {
            var s = new AppSettings(tmp);
            s.SetUiLanguageAsync("pt-BR").GetAwaiter().GetResult();
            Assert.Equal("pt-BR", AppSettings.ReadUiLanguageFast(tmp));

            s.SetUiLanguageAsync(null).GetAwaiter().GetResult();
            Assert.Null(AppSettings.ReadUiLanguageFast(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
