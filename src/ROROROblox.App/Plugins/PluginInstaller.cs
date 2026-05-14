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
    private readonly Func<string, Task> _stopRunningPluginAsync;

    /// <param name="stopRunningPluginAsync">
    /// Invoked with the plugin id just before the install dir is wiped + re-extracted, so a
    /// running instance releases its locked DLLs first. No-op when the plugin isn't running.
    /// </param>
    public PluginInstaller(HttpClient http, string pluginsRoot, Func<string, Task> stopRunningPluginAsync)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _pluginsRoot = pluginsRoot ?? throw new ArgumentNullException(nameof(pluginsRoot));
        _stopRunningPluginAsync = stopRunningPluginAsync ?? throw new ArgumentNullException(nameof(stopRunningPluginAsync));
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

        // 3. Stop any running instance of this plugin before we touch the install dir.
        // A live plugin process holds its own EXE + DLLs locked, so Directory.Delete
        // (or the extract below) fails with "access denied" on a re-install. No-op when
        // the plugin isn't running. Mirrors PluginsViewModel.RevokeAsync's stop-first dance.
        await _stopRunningPluginAsync(manifest.Id).ConfigureAwait(false);

        // 4. Unpack to install dir.
        var installDir = Path.Combine(_pluginsRoot, manifest.Id);
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

        return new InstalledPlugin
        {
            Manifest = manifest,
            InstallDir = installDir,
            Consent = new ConsentRecord
            {
                PluginId = manifest.Id,
                GrantedCapabilities = Array.Empty<string>(),
                AutostartEnabled = false,
            },
        };
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
