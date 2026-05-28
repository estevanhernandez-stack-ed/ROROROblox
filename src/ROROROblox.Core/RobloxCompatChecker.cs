using System.Diagnostics;
using System.IO;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.Core;

/// <summary>
/// Implementation of <see cref="IRobloxCompatChecker"/>. Reads installed Roblox via
/// <c>FileVersionInfo</c> on <c>RobloxPlayerBeta.exe</c>; fetches remote config from
/// <c>GitHub Releases / latest / download / roblox-compat.json</c>. Both calls are
/// best-effort — any failure returns a no-drift result so the user sees a clean window.
/// </summary>
public sealed class RobloxCompatChecker : IRobloxCompatChecker
{
    private const string CompatConfigUrl =
        "https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/roblox-compat.json";

    private const string IssuesUrl =
        "https://github.com/estevanhernandez-stack-ed/ROROROblox/issues";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string LastKnownMutexFileName = "last-known-mutex.txt";

    private readonly HttpClient _httpClient;
    private readonly Func<string?> _readLastKnownMutex;
    private readonly Action<string> _writeLastKnownMutex;

    /// <summary>
    /// ONE public ctor only — the typed-HttpClient DI activator requires exactly one applicable
    /// constructor (two would make it throw "Multiple constructors" at resolve time). The DI
    /// registration supplies just the <see cref="HttpClient"/>; the last-known-good cache read/write
    /// default to the real <c>%LOCALAPPDATA%\ROROROblox\last-known-mutex.txt</c> seams. Unit tests
    /// pass fakes to drive the resolver's fallback ladder without touching disk.
    /// </summary>
    public RobloxCompatChecker(
        HttpClient httpClient,
        Func<string?>? readLastKnownMutex = null,
        Action<string>? writeLastKnownMutex = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _readLastKnownMutex = readLastKnownMutex ?? ReadLastKnownMutexFromDisk;
        _writeLastKnownMutex = writeLastKnownMutex ?? WriteLastKnownMutexToDisk;
    }

    public async Task<CompatCheckResult> CheckAsync()
    {
        var installed = GetInstalledRobloxVersion();
        if (installed is null)
        {
            // Roblox isn't installed — banner is item 9's "Roblox not installed" modal,
            // not the version-drift banner.
            return new CompatCheckResult(HasDrift: false, Banner: null);
        }

        var config = await FetchConfigAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        if (config is null)
        {
            // No network or no published config — fail-quiet. Returning no-drift means the user
            // doesn't see a stale banner. If multi-instance breaks, the symptoms still point them
            // at the issue tracker via the "Roblox not installed" / "session expired" surfaces.
            return new CompatCheckResult(HasDrift: false, Banner: null);
        }

        if (!Version.TryParse(installed, out var installedVer)
            || !Version.TryParse(config.KnownGoodVersionMin, out var minVer)
            || !Version.TryParse(config.KnownGoodVersionMax, out var maxVer))
        {
            return new CompatCheckResult(HasDrift: false, Banner: null);
        }

        if (installedVer >= minVer && installedVer <= maxVer)
        {
            return new CompatCheckResult(HasDrift: false, Banner: null);
        }

        var direction = installedVer > maxVer ? "updated to" : "downgraded to";
        var banner =
            $"Roblox {direction} {installed}. We've tested up to {config.KnownGoodVersionMax}. " +
            $"Multi-instance might not work — let us know at {IssuesUrl}.";

        return new CompatCheckResult(HasDrift: true, Banner: banner);
    }

    private async Task<RobloxCompatConfig?> FetchConfigAsync(TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            return await _httpClient.GetFromJsonAsync<RobloxCompatConfig>(CompatConfigUrl, JsonOptions, cts.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<(string Name, MutexNameSource Source)> ResolveMutexNameAsync()
    {
        try
        {
            // Own 2s budget (vs the 8s banner fetch) so name resolution can run before mutex.Acquire
            // without holding first paint hostage to the network.
            var config = await FetchConfigAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            if (config is not null && MutexHolder.IsValidName(config.MutexName))
            {
                TryPersistLastKnown(config.MutexName);
                return (config.MutexName, MutexNameSource.RemoteConfig);
            }

            var cached = TryReadLastKnown();
            if (MutexHolder.IsValidName(cached))
            {
                return (cached!, MutexNameSource.LastKnownGood);
            }

            return (MutexHolder.DefaultMutexName, MutexNameSource.Default);
        }
        catch
        {
            // The resolver promises no-throw; any unexpected failure binds the safe default so a
            // broken roblox-compat.json can never brick multi-instance.
            return (MutexHolder.DefaultMutexName, MutexNameSource.Default);
        }
    }

    private string? TryReadLastKnown()
    {
        try
        {
            return _readLastKnownMutex();
        }
        catch
        {
            return null;
        }
    }

    private void TryPersistLastKnown(string name)
    {
        try
        {
            _writeLastKnownMutex(name);
        }
        catch
        {
            // Best-effort cache. A persist failure must never break resolution.
        }
    }

    private static string LastKnownMutexPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        LastKnownMutexFileName);

    private static string? ReadLastKnownMutexFromDisk()
    {
        var path = LastKnownMutexPath;
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static void WriteLastKnownMutexToDisk(string name)
    {
        var path = LastKnownMutexPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, name);
    }

    /// <summary>
    /// Reads the file-version string of the newest installed <c>RobloxPlayerBeta.exe</c>
    /// (the current <c>version-*</c> dir). Returns <c>null</c> when Roblox isn't installed or the
    /// read fails. Exposed <c>internal</c> so the v1.7.0 <c>RobloxUpdateProbe</c> reuses the exact
    /// same installed-version read (spec §"Components > 1. Update-pending detection").
    /// </summary>
    internal static string? GetInstalledRobloxVersion()
    {
        try
        {
            var versionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox",
                "Versions");

            if (!Directory.Exists(versionsDir))
            {
                return null;
            }

            var folders = new DirectoryInfo(versionsDir)
                .GetDirectories("version-*")
                .OrderByDescending(d => d.LastWriteTimeUtc);

            foreach (var folder in folders)
            {
                var exePath = Path.Combine(folder.FullName, "RobloxPlayerBeta.exe");
                if (!File.Exists(exePath))
                {
                    continue;
                }
                var info = FileVersionInfo.GetVersionInfo(exePath);
                return info.FileVersion ?? info.ProductVersion;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
