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

    [Fact]
    public void IdleSummary_PluralAndThreshold()
    {
        InEnglish(() =>
        {
            Assert.Equal(string.Empty, IdleSummary.Format(0, 5));
            Assert.Equal("1 account idle > 5m", IdleSummary.Format(1, 5));
            Assert.Equal("3 accounts idle > 5m", IdleSummary.Format(3, 5));
        });
    }

    [Fact]
    public void MemoryFooter_ZeroOneMany_Localized()
    {
        InEnglish(() =>
        {
            Assert.Equal("No Roblox clients running", MemoryChipFormatter.FormatFooter(0, 0, false));
            Assert.Equal("1 Roblox client running", MemoryChipFormatter.FormatFooter(1, 0, false));
            Assert.Equal("3 Roblox clients running", MemoryChipFormatter.FormatFooter(3, 0, false));
            Assert.Equal("2 Roblox clients running · 5.0 GB",
                MemoryChipFormatter.FormatFooter(2, 5L * 1024 * 1024 * 1024, false));
        });
    }

    private static ROROROblox.Core.Account ColdAccount(DateTimeOffset? lastLaunched = null) =>
        new(
            Id: Guid.NewGuid(),
            DisplayName: "TestAlt",
            AvatarUrl: "https://example.com/a.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: lastLaunched,
            RobloxUserId: 12345L);

    [Fact]
    public void AccountSummary_StatusStates_Localized()
    {
        InEnglish(() =>
        {
            Assert.Equal("Ready", new AccountSummary(ColdAccount()).SecondaryStatusText);
            Assert.Equal("Session expired",
                new AccountSummary(ColdAccount()) { SessionExpired = true }.SecondaryStatusText);
        });
    }

    [Fact]
    public void AccountSummary_DaysAgo_PluralFix()
    {
        // The old code always said "days ago" — "1 days ago". The plural family fixes it.
        InEnglish(() =>
        {
            Assert.Equal("Last launched 1 day ago",
                new AccountSummary(ColdAccount(DateTimeOffset.UtcNow.AddHours(-25))).SecondaryStatusText);
            Assert.Equal("Last launched 3 days ago",
                new AccountSummary(ColdAccount(DateTimeOffset.UtcNow.AddDays(-3))).SecondaryStatusText);
        });
    }
}
