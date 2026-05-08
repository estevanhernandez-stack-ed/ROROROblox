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
}
