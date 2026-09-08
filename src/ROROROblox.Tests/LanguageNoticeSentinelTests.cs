using ROROROblox.App.Localization;

namespace ROROROblox.Tests;

/// <summary>
/// The one-time "RoRoRo now follows your system language" tray notice (localization completeness).
/// Same show-once discipline as the first-run tour: the pure rule decides, and the caller marks the
/// sentinel ONLY when it returns true — marking unconditionally is the bug WelcomeWindow documents.
/// </summary>
public class LanguageNoticeSentinelTests
{
    [Fact]
    public void PendingWithALocalizedLanguageAvailable_Shows()
    {
        Assert.True(LanguageNoticeSentinel.ShouldShow(pending: true, aLocalizedLanguageIsAvailable: true));
    }

    [Fact]
    public void AlreadyShown_NeverShowsAgain()
    {
        // The sentinel already exists — pending is false. Must not re-fire even when languages ship.
        Assert.False(LanguageNoticeSentinel.ShouldShow(pending: false, aLocalizedLanguageIsAvailable: true));
    }

    [Fact]
    public void NoLocalizedLanguageAvailable_StaysSilent()
    {
        // English-only build: "follows your language" would just mean English and the picker offers
        // nothing else, so the notice would be noise. The caller must also not mark the sentinel here.
        Assert.False(LanguageNoticeSentinel.ShouldShow(pending: true, aLocalizedLanguageIsAvailable: false));
        Assert.False(LanguageNoticeSentinel.ShouldShow(pending: false, aLocalizedLanguageIsAvailable: false));
    }
}
