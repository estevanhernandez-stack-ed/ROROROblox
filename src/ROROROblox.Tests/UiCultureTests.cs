using System.Globalization;
using System.Resources;
using ROROROblox.App.Localization;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The UI-language surface is honest by construction (localization, 2026-09-07): a language is
/// offered ONLY when its satellite catalog actually ships. English is always in (the neutral
/// catalog is compiled in); each translated <c>Strings.&lt;culture&gt;.resx</c> that lands makes
/// its language appear and earns its package-manifest entry. Wave 1 (2026-09-07) ships six
/// translated catalogs — pt-BR, fr, de, ru, pl, es. These lock the guard so a picker bug can't
/// start advertising a language the app can't render.
/// </summary>
[Collection("MutatesUiCulture")]
public class UiCultureTests
{
    [Fact]
    public void Available_OffersALanguageIffEnglishOrItsCatalogShips()
    {
        var available = UiCulture.Available();

        // English is always available and first.
        Assert.Equal("", available[0].CultureName);
        Assert.Equal("English", available[0].DisplayName);

        // Honest by construction: for EVERY candidate, it is offered exactly when it is English
        // or its satellite catalog is present — never otherwise. This is the whole guarantee.
        foreach (var c in UiCulture.Candidates)
        {
            var offered = available.Any(a => a.CultureName == c.CultureName);
            var shouldOffer = c.CultureName.Length == 0 || UiCulture.HasCatalog(c.CultureName);
            Assert.Equal(shouldOffer, offered);
        }
    }

    [Fact]
    public void ShippedLanguages_AreTheFullWaveOneSet()
    {
        // Ratchet — moves in the same commit catalogs land. Wave 1 (2026-09-07): all six. If any
        // satellite silently stops building (a publish setting strips it, a resx breaks), its
        // language drops out of Available() and this fails loudly.
        var shipped = UiCulture.Available().Select(c => c.CultureName).ToHashSet();
        foreach (var lang in new[] { "pt-BR", "fr", "de", "ru", "pl", "es" })
            Assert.Contains(lang, shipped);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ru")]
    [InlineData("pl")]
    [InlineData("es")]
    public void EachShippedCatalog_ResolvesTranslatedText_NotAnEnglishFallback(string culture)
    {
        // The strongest end-to-end proof, per language: the satellite loads and returns genuinely
        // translated content. If it never built, GetString falls back to English and NotEqual
        // fails — exactly the regression we want caught. "Keyboard shortcuts" differs in all six.
        var rm = new ResourceManager("ROROROblox.App.Properties.Strings", typeof(UiCulture).Assembly);
        var en = rm.GetString("AboutPage_KeyboardShortcuts", CultureInfo.InvariantCulture);
        var loc = rm.GetString("AboutPage_KeyboardShortcuts", CultureInfo.GetCultureInfo(culture));

        Assert.Equal("Keyboard shortcuts", en);
        Assert.False(string.IsNullOrWhiteSpace(loc));
        Assert.NotEqual(en, loc);
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
        // A well-formed culture that will never ship a satellite: English is the NEUTRAL catalog,
        // so there is no en-GB satellite and tryParents:false won't borrow the neutral one. Durable
        // past the fan-out, unlike naming a wave-1 language that is about to ship.
        Assert.False(UiCulture.HasCatalog("en-GB"));
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
            File.WriteAllText(tmp, "{\"uiLanguage\":\"en-GB\"}");
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
    public void ApplyFromSettings_AppliesAShippedCulture()
    {
        // The positive path: a saved language whose catalog ships IS applied to the thread.
        var before = System.Threading.Thread.CurrentThread.CurrentUICulture;
        var beforeTs = TranslationSource.Instance.CurrentCulture; // ApplyFromSettings now sets this too
        var tmp = Path.Combine(Path.GetTempPath(), $"rr-uiculture-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tmp, "{\"uiLanguage\":\"pt-BR\"}");
            UiCulture.ApplyFromSettings(tmp);
            Assert.Equal("pt-BR", System.Threading.Thread.CurrentThread.CurrentUICulture.Name);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentUICulture = before;
            TranslationSource.Instance.CurrentCulture = beforeTs;
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
