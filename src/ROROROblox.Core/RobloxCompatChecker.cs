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

    private readonly HttpClient _httpClient;

    public RobloxCompatChecker(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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

        var config = await FetchConfigAsync().ConfigureAwait(false);
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

    private async Task<RobloxCompatConfig?> FetchConfigAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            return await _httpClient.GetFromJsonAsync<RobloxCompatConfig>(CompatConfigUrl, JsonOptions, cts.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetInstalledRobloxVersion()
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
