using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core;

/// <summary>
/// Implementation of <see cref="IRobloxCompatChecker"/>. Reads installed Roblox via
/// <c>FileVersionInfo</c> on <c>RobloxPlayerBeta.exe</c>; fetches remote config from
/// <c>GitHub Releases / latest / download / roblox-compat.json</c>. Both calls are
/// best-effort — any failure returns a no-drift result so the user sees a clean window.
///
/// <para>The remote fetch is signed. <see cref="FetchConfigAsync"/> pulls the config bytes AND the
/// detached <c>.sig</c> sibling, and verifies the raw bytes against <see cref="RobloxCompatSigningKey"/>
/// (ECDSA P-256/SHA-256, ported from 626-mod-launcher's manifest-signing pattern) BEFORE anything is
/// deserialized. Missing/invalid/unparseable signature is treated exactly like a network failure —
/// no update available, logged locally at Debug, never a crash, and the unverified bytes are never
/// deserialized or cached. Stricter than the mod-launcher's cache-then-verify-at-next-launch design:
/// this feed verifies at fetch time, in memory, so nothing unverified is ever trusted.</para>
/// </summary>
public sealed class RobloxCompatChecker : IRobloxCompatChecker
{
    private const string CompatConfigUrl =
        "https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/roblox-compat.json";

    private const string CompatConfigSignatureUrl = CompatConfigUrl + ".sig";

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
    private readonly ILogger<RobloxCompatChecker> _log;
    private readonly byte[] _pinnedPublicKey;

    /// <summary>
    /// ONE public ctor only — the typed-HttpClient DI activator requires exactly one applicable
    /// constructor (two would make it throw "Multiple constructors" at resolve time). The DI
    /// registration supplies just the <see cref="HttpClient"/> (DI fills the optional
    /// <see cref="ILogger{T}"/>); the last-known-good cache read/write default to the real
    /// <c>%LOCALAPPDATA%\ROROROblox\last-known-mutex.txt</c> seams, and <paramref name="pinnedPublicKey"/>
    /// defaults to the pinned production key (<see cref="RobloxCompatSigningKey.PublicKeySpki"/>).
    /// Unit tests pass fakes/an explicit test keypair to drive the resolver's fallback ladder and the
    /// signature-verify gate without touching disk, the network, or the production private key
    /// (which lives only in CI).
    /// </summary>
    public RobloxCompatChecker(
        HttpClient httpClient,
        Func<string?>? readLastKnownMutex = null,
        Action<string>? writeLastKnownMutex = null,
        ILogger<RobloxCompatChecker>? log = null,
        byte[]? pinnedPublicKey = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _readLastKnownMutex = readLastKnownMutex ?? ReadLastKnownMutexFromDisk;
        _writeLastKnownMutex = writeLastKnownMutex ?? WriteLastKnownMutexToDisk;
        _log = log ?? NullLogger<RobloxCompatChecker>.Instance;
        _pinnedPublicKey = pinnedPublicKey ?? RobloxCompatSigningKey.PublicKeySpki;
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
        byte[] configBytes;
        byte[] signatureBytes;
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            configBytes = await _httpClient.GetByteArrayAsync(CompatConfigUrl, cts.Token).ConfigureAwait(false);
            signatureBytes = await _httpClient.GetByteArrayAsync(CompatConfigSignatureUrl, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // No network, timeout, or missing .sig (404 -> HttpRequestException). Fail-quiet: treated
            // exactly like "no published config" — the caller sees no drift / the default mutex name.
            _log.LogDebug(ex, "roblox-compat.json/.sig fetch failed; degrading to no update available.");
            return null;
        }

        // Verify the EXACT bytes fetched off the wire, before anything is deserialized. Reject-and-
        // fail-quiet: an invalid/missing signature is never distinguished from a network failure to
        // the caller, and the unverified bytes are never deserialized, cached, or trusted.
        if (!RobloxCompatSignature.Verify(_pinnedPublicKey, configBytes, signatureBytes))
        {
            _log.LogDebug("roblox-compat.json signature did not verify; degrading to no update available.");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RobloxCompatConfig>(configBytes, JsonOptions);
        }
        catch (JsonException ex)
        {
            _log.LogDebug(ex, "roblox-compat.json failed to parse after a valid signature; degrading to no update available.");
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
    /// Reads the file-version string of the most-recently-written installed <c>RobloxPlayerBeta.exe</c>.
    /// Returns <c>null</c> when Roblox isn't installed or the read fails.
    ///
    /// <para><b>This does not answer "what will run" and must not be used as if it did (F-104).</b>
    /// A launch runs whatever the <c>roblox-player</c> handler is pinned to — see
    /// <see cref="GetHandlerRobloxVersion"/>. Two independent reasons this read cannot substitute:</para>
    /// <list type="bullet">
    /// <item><b>The ordering clock is wrong.</b> <c>LastWriteTimeUtc</c> bumps when a client RUNS,
    /// not when one installs. Measured 2026-08-12: three version folders whose real install dates
    /// were 08-09, 08-10 and 08-12 all carried write times inside the same three-minute window,
    /// which was an eight-client launch batch. During a batch this ordering is a coin flip.</item>
    /// <item><b>No timestamp orders by version anyway.</b> On the same box
    /// <c>version-7d4de67b</c> is <c>0,733,603</c> created 08-09 while <c>version-082eb75e</c> is
    /// the LOWER <c>0,733,448</c> created 08-10. Creation order does not track version order, so
    /// swapping the clock would not fix it either.</item>
    /// </list>
    /// <para>It is kept as the compat banner's read and as a fallback for when the handler is
    /// unreadable or owned by a strap. Exposed <c>internal</c> so <c>RobloxUpdateProbe</c> shares it.</para>
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

    /// <summary>
    /// The registry subkey holding the <c>roblox-player:</c> protocol handler command. Same key
    /// <see cref="BloxstrapDetector"/> reads to classify the handler; here we want the version it
    /// is pinned to rather than which binary owns it.
    /// </summary>
    private const string HandlerCommandSubKey = @"Software\Classes\roblox-player\shell\open\command";

    /// <summary>
    /// The version a launch will ACTUALLY run: the <c>FileVersion</c> of the
    /// <c>RobloxPlayerBeta.exe</c> that the <c>roblox-player</c> handler points at. This is the
    /// number the pre-warm gate needs (F-104) — <see cref="GetInstalledRobloxVersion"/> answers a
    /// different question and the two disagree exactly during an update, which is the only window
    /// where the gate matters.
    ///
    /// <para>Returns <c>null</c> when the handler is absent, unreadable, owned by a strap (no
    /// <c>version-*</c> segment in the path), or points at a binary that is gone. Every failure
    /// degrades to <c>null</c> so the caller falls back rather than blocking a launch.</para>
    ///
    /// <para>Posture: one registry read and one file read. No network, no handler takeover, no
    /// bootstrapper behaviour — clean under spec §7.1.</para>
    /// </summary>
    internal static string? GetHandlerRobloxVersion()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(HandlerCommandSubKey);
            var exePath = ExtractHandlerExePath(key?.GetValue(null) as string);
            if (exePath is null || !File.Exists(exePath))
            {
                return null;
            }

            var info = FileVersionInfo.GetVersionInfo(exePath);
            return info.FileVersion ?? info.ProductVersion;
        }
        catch
        {
            // Registry locked down, path malformed, file vanished mid-read — all "we don't know",
            // which is the fallback signal, not an error worth surfacing.
            return null;
        }
    }

    /// <summary>
    /// Pure: pull the <c>version-*</c> folder name out of a handler command string. Returns
    /// <c>null</c> when there is no such segment — which is the normal answer when Bloxstrap or
    /// Fishstrap owns the handler, since those point at their own binary. Exposed for tests.
    /// </summary>
    internal static string? ExtractVersionFolder(string? handlerCommand)
    {
        if (string.IsNullOrWhiteSpace(handlerCommand))
        {
            return null;
        }

        // Split on both separators: the registry value uses backslashes, but a hand-written or
        // migrated value can carry forward slashes and Windows accepts them.
        var segments = handlerCommand.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var trimmed = segment.Trim('"', ' ');
            // "version-" prefixed, and something after the dash. A bare "version-" or a lookalike
            // like "versions" or "notaversion" is not a match.
            if (trimmed.Length > "version-".Length &&
                trimmed.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
        }

        return null;
    }

    /// <summary>
    /// Pure-ish: turn a handler command into the full path of the pinned <c>RobloxPlayerBeta.exe</c>,
    /// or <c>null</c> when the command carries no <c>version-*</c> segment. Rebuilt from the known
    /// Versions root rather than parsed out of the command line, so a quoted path with spaces and a
    /// trailing <c>%1</c> needs no argv splitting.
    /// </summary>
    private static string? ExtractHandlerExePath(string? handlerCommand)
    {
        var folder = ExtractVersionFolder(handlerCommand);
        if (folder is null)
        {
            return null;
        }

        return Path.Combine(VersionsDirectory, folder, "RobloxPlayerBeta.exe");
    }

    /// <summary>The per-user Roblox <c>Versions</c> root. Both version reads hang off this.</summary>
    private static string VersionsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox",
        "Versions");

    /// <summary>
    /// How far back <see cref="CountRecentVersionInstalls(TimeSpan)"/> looks by default. Long enough
    /// to span a Roblox update landing mid-batch, short enough that yesterday's update is not still
    /// counted as churn.
    /// </summary>
    internal static readonly TimeSpan DefaultChurnWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How many Roblox versions were INSTALLED inside <paramref name="window"/>. More than one means
    /// updates are landing on top of each other, which is a reason to hold a batch regardless of
    /// what any version comparison says — and it costs no network at all (F-104's second signal).
    /// </summary>
    internal static int CountRecentVersionInstalls(TimeSpan window)
        => CountRecentVersionInstalls(VersionsDirectory, window, DateTime.UtcNow);

    /// <summary>
    /// Testable core of <see cref="CountRecentVersionInstalls(TimeSpan)"/>.
    ///
    /// <para><b>Creation time, deliberately, and this is the whole subtlety.</b>
    /// <c>LastWriteTimeUtc</c> on a <c>version-*</c> folder moves when a client RUNS. Counting on it
    /// would report churn every time someone multilaunches — the exact moment the answer must be
    /// trusted — so it would fire hardest precisely when it is most wrong. <c>CreationTimeUtc</c> is
    /// the install clock. (It is NOT a "which is newest" clock; see
    /// <see cref="GetInstalledRobloxVersion"/>. Recency and ordering are different questions and
    /// only the first one is being asked here.)</para>
    /// </summary>
    internal static int CountRecentVersionInstalls(string versionsDir, TimeSpan window, DateTime nowUtc)
    {
        try
        {
            if (!Directory.Exists(versionsDir))
            {
                return 0;
            }

            var cutoff = nowUtc - window;
            return new DirectoryInfo(versionsDir)
                .GetDirectories("version-*")
                .Count(d => d.CreationTimeUtc >= cutoff);
        }
        catch
        {
            // Degrade-safe like the rest of this family: unreadable is not churn.
            return 0;
        }
    }
}
