using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public AppSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-settings-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetDefaultPlaceUrlAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        using var settings = new AppSettings(_filePath);

        var url = await settings.GetDefaultPlaceUrlAsync();

        Assert.Null(url);
    }

    [Fact]
    public async Task SetThenGet_RoundTrips()
    {
        using var settings = new AppSettings(_filePath);

        await settings.SetDefaultPlaceUrlAsync("https://www.roblox.com/games/920587237/Adopt-Me");

        var url = await settings.GetDefaultPlaceUrlAsync();
        Assert.Equal("https://www.roblox.com/games/920587237/Adopt-Me", url);
    }

    [Fact]
    public async Task SetDefaultPlaceUrlAsync_RejectsEmptyOrWhitespace()
    {
        using var settings = new AppSettings(_filePath);

        await Assert.ThrowsAsync<ArgumentException>(() => settings.SetDefaultPlaceUrlAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => settings.SetDefaultPlaceUrlAsync("   "));
    }

    [Fact]
    public async Task ColdStart_ReadsBackPersistedValue()
    {
        var first = new AppSettings(_filePath);
        await first.SetDefaultPlaceUrlAsync("https://x.example/place");
        first.Dispose();

        using var second = new AppSettings(_filePath);
        var url = await second.GetDefaultPlaceUrlAsync();

        Assert.Equal("https://x.example/place", url);
    }

    [Fact]
    public async Task TamperedJson_ReturnsNullDefault()
    {
        File.WriteAllText(_filePath, "{ this is not valid JSON");
        using var settings = new AppSettings(_filePath);

        var url = await settings.GetDefaultPlaceUrlAsync();

        Assert.Null(url);
    }

    [Fact]
    public async Task SaveAsync_RemovesTempFile_OnSuccess()
    {
        using var settings = new AppSettings(_filePath);
        await settings.SetDefaultPlaceUrlAsync("https://x");

        var tempPath = _filePath + ".tmp";
        Assert.False(File.Exists(tempPath));
        Assert.True(File.Exists(_filePath));
    }

    [Fact]
    public async Task SetDefaultPlaceUrlAsync_OverwritesExistingValue()
    {
        using var settings = new AppSettings(_filePath);

        await settings.SetDefaultPlaceUrlAsync("https://first");
        await settings.SetDefaultPlaceUrlAsync("https://second");

        var url = await settings.GetDefaultPlaceUrlAsync();
        Assert.Equal("https://second", url);
    }

    [Fact]
    public async Task GetLaunchMainOnStartupAsync_DefaultsToFalse()
    {
        using var settings = new AppSettings(_filePath);

        Assert.False(await settings.GetLaunchMainOnStartupAsync());
    }

    [Fact]
    public async Task LaunchMainOnStartup_RoundTripsTrueAndFalse()
    {
        using var settings = new AppSettings(_filePath);

        await settings.SetLaunchMainOnStartupAsync(true);
        Assert.True(await settings.GetLaunchMainOnStartupAsync());

        await settings.SetLaunchMainOnStartupAsync(false);
        Assert.False(await settings.GetLaunchMainOnStartupAsync());
    }

    [Fact]
    public async Task LaunchMainOnStartup_PersistsAcrossInstances()
    {
        {
            using var first = new AppSettings(_filePath);
            await first.SetLaunchMainOnStartupAsync(true);
        }

        using var second = new AppSettings(_filePath);
        Assert.True(await second.GetLaunchMainOnStartupAsync());
    }

    [Fact]
    public async Task LaunchMainOnStartup_AndDefaultPlaceUrl_AreIndependent()
    {
        using var settings = new AppSettings(_filePath);

        await settings.SetDefaultPlaceUrlAsync("https://place");
        await settings.SetLaunchMainOnStartupAsync(true);

        Assert.Equal("https://place", await settings.GetDefaultPlaceUrlAsync());
        Assert.True(await settings.GetLaunchMainOnStartupAsync());

        // Toggling one doesn't disturb the other.
        await settings.SetLaunchMainOnStartupAsync(false);
        Assert.Equal("https://place", await settings.GetDefaultPlaceUrlAsync());
    }

    [Fact]
    public async Task BloxstrapWarningDismissed_DefaultsToFalse()
    {
        using var settings = new AppSettings(_filePath);

        Assert.False(await settings.GetBloxstrapWarningDismissedAsync());
    }

    [Fact]
    public async Task BloxstrapWarningDismissed_PersistsAcrossInstances()
    {
        {
            using var first = new AppSettings(_filePath);
            await first.SetBloxstrapWarningDismissedAsync(true);
        }

        using var second = new AppSettings(_filePath);
        Assert.True(await second.GetBloxstrapWarningDismissedAsync());
    }

    [Fact]
    public async Task BloxstrapWarningDismissed_RoundTripsTrueAndFalse()
    {
        using var settings = new AppSettings(_filePath);

        await settings.SetBloxstrapWarningDismissedAsync(true);
        Assert.True(await settings.GetBloxstrapWarningDismissedAsync());

        await settings.SetBloxstrapWarningDismissedAsync(false);
        Assert.False(await settings.GetBloxstrapWarningDismissedAsync());
    }

    [Fact]
    public async Task DismissedFpsCapWarningSignature_DefaultsNull_ThenRoundTripsAcrossInstances()
    {
        {
            using var first = new AppSettings(_filePath);
            Assert.Null(await first.GetDismissedFpsCapWarningSignatureAsync());

            await first.SetDismissedFpsCapWarningSignatureAsync("none,20,45");
            Assert.Equal("none,20,45", await first.GetDismissedFpsCapWarningSignatureAsync());
        }

        using var second = new AppSettings(_filePath);
        Assert.Equal("none,20,45", await second.GetDismissedFpsCapWarningSignatureAsync());
    }

    [Fact]
    public async Task DismissedFpsCapWarningSignature_CanBeClearedBackToNull()
    {
        using var settings = new AppSettings(_filePath);

        await settings.SetDismissedFpsCapWarningSignatureAsync("20,45");
        Assert.Equal("20,45", await settings.GetDismissedFpsCapWarningSignatureAsync());

        await settings.SetDismissedFpsCapWarningSignatureAsync(null);
        Assert.Null(await settings.GetDismissedFpsCapWarningSignatureAsync());
    }

    /// <summary>
    /// A v1-shaped settings file written before this field existed -- no
    /// dismissedFpsCapWarningSignature property at all, not even null -- must still load cleanly
    /// with the field defaulting to null, exactly like every other field added to SettingsBlob
    /// after v1. Proves the back-compat comment on SettingsBlob is actually true, not just
    /// asserted.
    /// </summary>
    [Fact]
    public async Task OlderSettingsFile_MissingDismissedFpsCapWarningSignature_LoadsWithNullDefault()
    {
        File.WriteAllText(_filePath, """
            {
              "version": 1,
              "defaultPlaceUrl": "https://www.roblox.com/games/920587237/Adopt-Me",
              "bloxstrapWarningDismissed": true
            }
            """);

        using var settings = new AppSettings(_filePath);

        Assert.Null(await settings.GetDismissedFpsCapWarningSignatureAsync());
        // And the older fields the file DID carry still load correctly alongside the new default.
        Assert.Equal("https://www.roblox.com/games/920587237/Adopt-Me", await settings.GetDefaultPlaceUrlAsync());
        Assert.True(await settings.GetBloxstrapWarningDismissedAsync());
    }

    [Fact]
    public async Task MuteIdleAlerts_DefaultsFalse_RoundTrips()
    {
        using var settings = new AppSettings(_filePath);

        Assert.False(await settings.GetMuteIdleAlertsAsync());

        await settings.SetMuteIdleAlertsAsync(true);
        Assert.True(await settings.GetMuteIdleAlertsAsync());
    }

    [Fact]
    public async Task IdleWarnThresholdMinutes_DefaultsFifteen_RoundTrips()
    {
        using var settings = new AppSettings(_filePath);

        Assert.Equal(15, await settings.GetIdleWarnThresholdMinutesAsync());

        await settings.SetIdleWarnThresholdMinutesAsync(12);
        Assert.Equal(12, await settings.GetIdleWarnThresholdMinutesAsync());
    }

    [Fact]
    public async Task CarefulSquadLaunch_DefaultsFalse_RoundTripsAcrossInstances()
    {
        {
            using var first = new AppSettings(_filePath);
            Assert.False(await first.GetCarefulSquadLaunchAsync());

            await first.SetCarefulSquadLaunchAsync(true);
            Assert.True(await first.GetCarefulSquadLaunchAsync());
        }

        using var second = new AppSettings(_filePath);
        Assert.True(await second.GetCarefulSquadLaunchAsync());
    }

    [Fact]
    public async Task StreamerMode_DefaultsFalse_ThenPersists()
    {
        {
            using var first = new AppSettings(_filePath);
            Assert.False(await first.GetStreamerModeAsync());

            await first.SetStreamerModeAsync(true);
            Assert.True(await first.GetStreamerModeAsync());
        }

        using var second = new AppSettings(_filePath);
        Assert.True(await second.GetStreamerModeAsync());
    }

    [Fact]
    public async Task MemoryWatchdogEnabled_DefaultsTrue_ThenPersists()
    {
        {
            using var first = new AppSettings(_filePath);
            Assert.True(await first.GetMemoryWatchdogEnabledAsync());

            await first.SetMemoryWatchdogEnabledAsync(false);
            Assert.False(await first.GetMemoryWatchdogEnabledAsync());
        }

        using var second = new AppSettings(_filePath);
        Assert.False(await second.GetMemoryWatchdogEnabledAsync());
    }

    [Fact]
    public async Task ProjectionWarnMinutes_DefaultsOneTwenty_ThenPersists()
    {
        {
            using var first = new AppSettings(_filePath);
            Assert.Equal(120, await first.GetProjectionWarnMinutesAsync());

            await first.SetProjectionWarnMinutesAsync(45);
            Assert.Equal(45, await first.GetProjectionWarnMinutesAsync());
        }

        using var second = new AppSettings(_filePath);
        Assert.Equal(45, await second.GetProjectionWarnMinutesAsync());
    }

    [Fact]
    public async Task MemoryReserveMb_DefaultsNull_ThenPersists()
    {
        {
            using var first = new AppSettings(_filePath);
            Assert.Null(await first.GetMemoryReserveMbAsync());

            await first.SetMemoryReserveMbAsync(1536);
            Assert.Equal(1536, await first.GetMemoryReserveMbAsync());
        }

        using var second = new AppSettings(_filePath);
        Assert.Equal(1536, await second.GetMemoryReserveMbAsync());
    }

    /// <summary>
    /// The one that matters most: MemoryCapMb == 0 is a deliberate user choice ("disable the cap
    /// trigger") and must survive a save/load cycle as 0, never silently reverting to null
    /// ("never set" -> re-derive). Proves both states are distinguishable, not just that one of
    /// them happens to survive.
    /// </summary>
    [Fact]
    public async Task MemoryCapMb_ZeroRoundTripsDistinctlyFromNull()
    {
        {
            using var first = new AppSettings(_filePath);
            Assert.Null(await first.GetMemoryCapMbAsync());   // untouched instance: null, not 0

            await first.SetMemoryCapMbAsync(0);
            Assert.Equal(0, await first.GetMemoryCapMbAsync());
        }

        using var second = new AppSettings(_filePath);
        Assert.Equal(0, await second.GetMemoryCapMbAsync());   // fresh instance after reload: 0, not null
        Assert.NotNull(await second.GetMemoryCapMbAsync());
    }
}
