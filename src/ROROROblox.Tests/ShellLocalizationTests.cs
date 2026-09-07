using System;
using System.Globalization;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Localization Phase D step 3, batch 1 (shell). Each composer resolves its text through
/// <see cref="Loc"/> now; these assert the English (neutral resx) composition. Culture-mutating,
/// so isolated in the serial MutatesUiCulture collection.
/// </summary>
[Collection("MutatesUiCulture")]
public class ShellLocalizationTests
{
    private static void InEnglish(Action body)
    {
        var prev = TranslationSource.Instance.CurrentCulture;
        try { TranslationSource.Instance.CurrentCulture = new CultureInfo("en"); body(); }
        finally { TranslationSource.Instance.CurrentCulture = prev; }
    }

    [Fact]
    public void MultiInstanceCopy_ResolvesFromResx()
    {
        InEnglish(() =>
        {
            Assert.Equal("Still locked — Roblox is still running.", MultiInstanceCopy.StillLocked);
            Assert.StartsWith("Roblox has the multi-instance lock", MultiInstanceCopy.ContestedBanner);
            Assert.Contains("FPS cap", MultiInstanceCopy.FpsCapMismatchBanner);
        });
    }
}
