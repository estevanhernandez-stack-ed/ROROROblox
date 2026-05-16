using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Installs a plugin from a base URL: pulls manifest.json + manifest.sha256 + plugin.zip,
/// SHA-verifies the zip, parses + validates the manifest (including required-capability
/// presence check), stops any running instance of the plugin, then unpacks to
/// <c>%LOCALAPPDATA%\ROROROblox\plugins\&lt;id&gt;\</c>.
/// User-initiated only — the call originates from the plugin install dialog,
/// never from auto-discovery or background polling. Store-policy 10.2.2 clean.
/// </summary>
public sealed class PluginInstaller
{
    private readonly HttpClient _http;
    private readonly string _pluginsRoot;
    private readonly Func<string, string, Task> _stopRunningPluginAsync;
    private readonly Version _hostVersion;

    /// <param name="stopRunningPluginAsync">
    /// Invoked with (pluginId, installDir) just before the install dir is wiped + re-extracted,
    /// so any process running out of that dir — a tracked instance OR an orphan — releases its
    /// locked DLLs first. No-op when nothing is running there.
    /// </param>
    /// <param name="hostVersion">
    /// The running RoRoRo's assembly version. Compared against <c>manifest.minHostVersion</c>
    /// to refuse installs that need a newer host than the user is on.
    /// </param>
    public PluginInstaller(HttpClient http, string pluginsRoot, Func<string, string, Task> stopRunningPluginAsync, Version hostVersion)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _pluginsRoot = pluginsRoot ?? throw new ArgumentNullException(nameof(pluginsRoot));
        _stopRunningPluginAsync = stopRunningPluginAsync ?? throw new ArgumentNullException(nameof(stopRunningPluginAsync));
        _hostVersion = hostVersion ?? throw new ArgumentNullException(nameof(hostVersion));
    }

    public async Task<InstalledPlugin> InstallAsync(string baseUrl, IReadOnlyList<string> requireCapabilities)
    {
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        // 1. Fetch manifest, parse, sanity-check required capabilities.
        var manifestJson = await GetStringAsync(new Uri(baseUrl + "manifest.json")).ConfigureAwait(false);
        PluginManifest manifest;
        try
        {
            manifest = PluginManifest.Parse(manifestJson);
        }
        catch (PluginManifestException ex)
        {
            throw new PluginInstallerException($"Manifest validation failed: {ex.Message}", ex);
        }

        foreach (var required in requireCapabilities)
        {
            if (!manifest.Capabilities.Contains(required))
            {
                throw new PluginInstallerException(
                    $"Plugin manifest does not declare required capability '{required}'.");
            }
        }

        // 1a. Refuse early if the manifest needs a newer host than we are. Cheap pre-check
        // before we burn bandwidth on the zip download — and the user gets a clear "update
        // RoRoRo" message instead of a downstream symptom (the plugin's gRPC client failing
        // because it expects a method this host doesn't expose, for instance).
        if (!string.IsNullOrWhiteSpace(manifest.MinHostVersion))
        {
            if (!TryParseHostVersion(manifest.MinHostVersion, out var minVersion))
            {
                throw new PluginInstallerException(
                    $"Plugin manifest minHostVersion '{manifest.MinHostVersion}' is not a valid version.");
            }
            if (_hostVersion < minVersion)
            {
                throw new PluginInstallerException(
                    $"This plugin requires RoRoRo {manifest.MinHostVersion} or newer. You're running {_hostVersion}. Update RoRoRo and try again.");
            }
        }

        // 2. Fetch SHA256, fetch zip, verify.
        var expectedSha = (await GetStringAsync(new Uri(baseUrl + "manifest.sha256")).ConfigureAwait(false))
            .Trim().ToLowerInvariant();
        var zipBytes = await GetByteArrayAsync(new Uri(baseUrl + "plugin.zip")).ConfigureAwait(false);
        var actualSha = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
        if (!string.Equals(actualSha, expectedSha, StringComparison.Ordinal))
        {
            throw new PluginInstallerException(
                $"Plugin zip SHA256 mismatch. Expected {expectedSha}, got {actualSha}.");
        }

        // 3. Stop everything running out of the install dir before we touch it. A live
        // plugin process — tracked OR an orphan from a prior RoRoRo session — holds its
        // EXE + DLLs locked, so Directory.Delete / extract fails with "access denied" on a
        // re-install. The hook finds processes by image path so orphans can't hide from it.
        var installDir = Path.Combine(_pluginsRoot, manifest.Id);
        await _stopRunningPluginAsync(manifest.Id, installDir).ConfigureAwait(false);

        // 4. Unpack to install dir.
        if (Directory.Exists(installDir))
        {
            Directory.Delete(installDir, recursive: true);
        }
        Directory.CreateDirectory(installDir);
        try
        {
            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            var installRootFull = Path.GetFullPath(installDir);
            var prefix = installRootFull.EndsWith(Path.DirectorySeparatorChar)
                ? installRootFull
                : installRootFull + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                var dest = Path.Combine(installDir, entry.FullName);
                var fullDestPath = Path.GetFullPath(dest);
                if (!fullDestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new PluginInstallerException("Zip-slip detected — zip contains paths outside install dir.");
                }
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(dest);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }
        catch
        {
            // Best-effort cleanup on failure.
            try { Directory.Delete(installDir, recursive: true); } catch { }
            throw;
        }

        // 5. If the manifest declares an entrypoint, confirm it actually shipped in the zip.
        // Caught here, not at launch time — better to refuse the install than to leave the
        // user with a broken plugin row that mysteriously won't start.
        if (!string.IsNullOrWhiteSpace(manifest.Entrypoint))
        {
            var entrypointPath = Path.Combine(installDir, manifest.Entrypoint);
            if (!File.Exists(entrypointPath))
            {
                try { Directory.Delete(installDir, recursive: true); } catch { }
                throw new PluginInstallerException(
                    $"Plugin manifest declares entrypoint '{manifest.Entrypoint}' but that file is not present in the unpacked zip.");
            }
        }

        return new InstalledPlugin
        {
            Manifest = manifest,
            InstallDir = installDir,
            Consent = new ConsentRecord
            {
                PluginId = manifest.Id,
                GrantedCapabilities = Array.Empty<string>(),
                // autostartDefault=="on" turns ON the initial consent record. Re-installs
                // hit the consent-merge path upstream (PluginsViewModel.InstallAsync) so
                // this only governs first-time installs.
                AutostartEnabled = manifest.AutostartDefault == "on",
            },
        };
    }

    /// <summary>
    /// Parse a manifest's minHostVersion string into a <see cref="Version"/>. Accepts
    /// 4-part numeric (matches the host's own version scheme) plus 2/3-part shorthand.
    /// Tolerates semver-style pre-release tags by splitting on the first <c>-</c> and
    /// parsing the numeric head — `1.4.3-beta` is treated as `1.4.3`.
    /// </summary>
    private static bool TryParseHostVersion(string input, out Version version)
    {
        var head = input;
        var dash = head.IndexOf('-');
        if (dash >= 0) head = head[..dash];
        return Version.TryParse(head, out version!);
    }

    private async Task<string> GetStringAsync(Uri uri)
    {
        using var response = await _http.GetAsync(uri).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new PluginInstallerException($"GET {uri} returned {(int)response.StatusCode}.");
        }
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private async Task<byte[]> GetByteArrayAsync(Uri uri)
    {
        using var response = await _http.GetAsync(uri).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new PluginInstallerException($"GET {uri} returned {(int)response.StatusCode}.");
        }
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }
}

public sealed class PluginInstallerException : Exception
{
    public PluginInstallerException(string message) : base(message) { }
    public PluginInstallerException(string message, Exception inner) : base(message, inner) { }
}
