using System.IO;
using System.Text.Json;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// All tests use a temp directory as the "root" containing both candidate Roblox install layouts.
/// The writer accepts override paths (test ctor) so we don't have to mutate the real
/// %LOCALAPPDATA% during tests.
/// </summary>
public sealed class ClientAppSettingsWriterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _standaloneRoot;
    private readonly string _packagesRoot;

    public ClientAppSettingsWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rororo-fps-" + Guid.NewGuid().ToString("N"));
        _standaloneRoot = Path.Combine(_tempRoot, "Roblox", "Versions");
        _packagesRoot = Path.Combine(_tempRoot, "Packages");
        Directory.CreateDirectory(_standaloneRoot);
        Directory.CreateDirectory(_packagesRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private string MakeVersionFolder(string root, string name, DateTime lastWrite)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "RobloxPlayerBeta.exe");
        File.WriteAllText(exe, "stub");
        File.SetLastWriteTimeUtc(exe, lastWrite);
        return folder;
    }

    [Fact]
    public async Task WriteFpsAsync_StandaloneOnly_WritesToStandaloneVersionFolder()
    {
        var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
        var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

        await writer.WriteFpsAsync(60);

        var jsonPath = Path.Combine(versionFolder, "ClientSettings", "ClientAppSettings.json");
        Assert.True(File.Exists(jsonPath));
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        Assert.Equal(60, doc.RootElement.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
    }

    [Fact]
    public async Task WriteFpsAsync_UwpOnly_WritesToUwpVersionFolder()
    {
        var package = Path.Combine(_packagesRoot, "ROBLOXCORPORATION.ROBLOX_55nm5eh3cm0pr", "LocalCache", "Local", "Roblox", "Versions");
        Directory.CreateDirectory(package);
        var versionFolder = MakeVersionFolder(package, "version-uwp-1", DateTime.UtcNow);
        var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

        await writer.WriteFpsAsync(144);

        var jsonPath = Path.Combine(versionFolder, "ClientSettings", "ClientAppSettings.json");
        Assert.True(File.Exists(jsonPath));
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        Assert.Equal(144, doc.RootElement.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
    }

    [Fact]
    public async Task WriteFpsAsync_BothActiveWithin30Days_WritesToBoth()
    {
        var standaloneFolder = MakeVersionFolder(_standaloneRoot, "version-stand", DateTime.UtcNow.AddDays(-2));
        var package = Path.Combine(_packagesRoot, "ROBLOXCORPORATION.ROBLOX_55nm5eh3cm0pr", "LocalCache", "Local", "Roblox", "Versions");
        Directory.CreateDirectory(package);
        var uwpFolder = MakeVersionFolder(package, "version-uwp", DateTime.UtcNow);
        var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

        await writer.WriteFpsAsync(120);

        Assert.True(File.Exists(Path.Combine(standaloneFolder, "ClientSettings", "ClientAppSettings.json")));
        Assert.True(File.Exists(Path.Combine(uwpFolder, "ClientSettings", "ClientAppSettings.json")));
    }

    [Fact]
    public async Task WriteFpsAsync_StaleStandalonePlusFreshUwp_OnlyWritesUwp()
    {
        MakeVersionFolder(_standaloneRoot, "version-old", DateTime.UtcNow.AddDays(-90));
        var package = Path.Combine(_packagesRoot, "ROBLOXCORPORATION.ROBLOX_55nm5eh3cm0pr", "LocalCache", "Local", "Roblox", "Versions");
        Directory.CreateDirectory(package);
        var uwpFolder = MakeVersionFolder(package, "version-uwp", DateTime.UtcNow);
        var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

        await writer.WriteFpsAsync(60);

        var standaloneJson = Path.Combine(_standaloneRoot, "version-old", "ClientSettings", "ClientAppSettings.json");
        Assert.False(File.Exists(standaloneJson));
        Assert.True(File.Exists(Path.Combine(uwpFolder, "ClientSettings", "ClientAppSettings.json")));
    }

    [Fact]
    public async Task WriteFpsAsync_NoVersionFolder_Throws()
    {
        var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);
        await Assert.ThrowsAsync<ClientAppSettingsWriteException>(() => writer.WriteFpsAsync(60));
    }

    [Fact]
    public async Task WriteFpsAsync_PreservesOtherFFlags()
    {
        var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
        var clientSettings = Path.Combine(versionFolder, "ClientSettings");
        Directory.CreateDirectory(clientSettings);
        var jsonPath = Path.Combine(clientSettings, "ClientAppSettings.json");
        await File.WriteAllTextAsync(jsonPath, "{\"FStringSomeOtherFlag\": \"foo\"}");
        var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

        await writer.WriteFpsAsync(90);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        Assert.Equal("foo", doc.RootElement.GetProperty("FStringSomeOtherFlag").GetString());
        Assert.Equal(90, doc.RootElement.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
    }

    [Fact]
    public async Task WriteFpsAsync_MalformedJson_ReplacesWithFreshFile()
    {
        var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
        var clientSettings = Path.Combine(versionFolder, "ClientSettings");
        Directory.CreateDirectory(clientSettings);
        var jsonPath = Path.Combine(clientSettings, "ClientAppSettings.json");
        await File.WriteAllTextAsync(jsonPath, "this is not json {");
        var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

        await writer.WriteFpsAsync(60);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        Assert.Equal(60, doc.RootElement.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
    }

    [Fact]
    public async Task WriteFpsAsync_NullClearsKey()
    {
        var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
        var clientSettings = Path.Combine(versionFolder, "ClientSettings");
        Directory.CreateDirectory(clientSettings);
        var jsonPath = Path.Combine(clientSettings, "ClientAppSettings.json");
        await File.WriteAllTextAsync(jsonPath, "{\"DFIntTaskSchedulerTargetFps\": 60, \"FStringOther\": \"keep\"}");
        var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

        await writer.WriteFpsAsync(null);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        Assert.False(doc.RootElement.TryGetProperty("DFIntTaskSchedulerTargetFps", out _));
        Assert.Equal("keep", doc.RootElement.GetProperty("FStringOther").GetString());
    }

    [Fact]
    public async Task WriteFpsAsync_AboveCapThreshold_WritesCapRemovalFlag()
    {
        var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
        var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

        await writer.WriteFpsAsync(360);

        var jsonPath = Path.Combine(versionFolder, "ClientSettings", "ClientAppSettings.json");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        Assert.Equal(360, doc.RootElement.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
        Assert.False(doc.RootElement.GetProperty("FFlagTaskSchedulerLimitTargetFpsTo2402").GetBoolean());
    }

    [Fact]
    public async Task WriteFpsAsync_AtOrBelowThreshold_OmitsCapRemovalFlag()
    {
        var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
        var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

        await writer.WriteFpsAsync(144);

        var jsonPath = Path.Combine(versionFolder, "ClientSettings", "ClientAppSettings.json");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        Assert.False(doc.RootElement.TryGetProperty("FFlagTaskSchedulerLimitTargetFpsTo2402", out _));
    }
}
