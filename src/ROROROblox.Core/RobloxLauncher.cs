using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core;

/// <summary>
/// Implements <see cref="IRobloxLauncher"/>. Coordinates ticket fetch + URI build + process spawn.
/// Pure URI construction is exposed as <see cref="BuildLaunchUri"/> for snapshot testing without
/// invoking the full async flow.
/// </summary>
public sealed class RobloxLauncher : IRobloxLauncher
{
    private const string RobloxNotInstalledMessage = "Roblox does not appear to be installed.";
    private const string PlaceLauncherEndpoint = "https://assetgame.roblox.com/game/PlaceLauncher.ashx";

    private readonly IRobloxApi _api;
    private readonly IAppSettings _settings;
    private readonly IProcessStarter _processStarter;
    private readonly IFavoriteGameStore? _favorites;
    private readonly TimeProvider _timeProvider;
    private readonly Func<long> _browserTrackerIdFactory;
    private readonly IClientAppSettingsWriter? _clientAppSettings;
    private readonly IGlobalBasicSettingsWriter? _globalBasicSettings;
    private readonly IGlobalBasicSettingsProbe? _settingsProbe;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _launchGate = new(initialCount: 1, maxCount: 1);

    /// <summary>
    /// The settings file's mtime at the moment the MOST RECENTLY launched client's
    /// <c>Process.Start</c> returned, or <see langword="null"/> before the first launch of the
    /// session (or when no <see cref="IGlobalBasicSettingsProbe"/> is wired). Fed into the NEXT
    /// launch's <see cref="FpsCapSettler.SettleAsync"/> call as the proof-of-read baseline — see
    /// <see cref="ApplyFpsCapAsync"/>. Every access is already serialized by <see cref="_launchGate"/>
    /// (both <see cref="LaunchAsync(string, LaunchTarget, int?, long?)"/> and
    /// <see cref="LaunchAsync(string, string?, int?, long?)"/> hold it for the full launch), so no
    /// separate lock is needed here.
    /// </summary>
    private DateTimeOffset? _lastLaunchMtimeUtc;

    public RobloxLauncher(
        IRobloxApi api,
        IAppSettings settings,
        IProcessStarter processStarter,
        IFavoriteGameStore? favorites = null,
        IClientAppSettingsWriter? clientAppSettings = null,
        IGlobalBasicSettingsWriter? globalBasicSettings = null,
        IGlobalBasicSettingsProbe? settingsProbe = null,
        ILogger<RobloxLauncher>? logger = null)
        : this(api, settings, processStarter, TimeProvider.System,
              () => Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999),
              favorites, clientAppSettings, globalBasicSettings, settingsProbe, logger)
    {
    }

    // Visible for tests.
    internal RobloxLauncher(
        IRobloxApi api,
        IAppSettings settings,
        IProcessStarter processStarter,
        TimeProvider timeProvider,
        Func<long> browserTrackerIdFactory,
        IFavoriteGameStore? favorites = null,
        IClientAppSettingsWriter? clientAppSettings = null,
        IGlobalBasicSettingsWriter? globalBasicSettings = null,
        IGlobalBasicSettingsProbe? settingsProbe = null,
        ILogger<RobloxLauncher>? logger = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _browserTrackerIdFactory = browserTrackerIdFactory ?? throw new ArgumentNullException(nameof(browserTrackerIdFactory));
        _favorites = favorites;
        _clientAppSettings = clientAppSettings;
        _globalBasicSettings = globalBasicSettings;
        _settingsProbe = settingsProbe;
        _log = logger ?? (ILogger)NullLogger.Instance;
    }

    public async Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
    {
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }
        ArgumentNullException.ThrowIfNull(target);

        await _launchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (fpsCap.HasValue)
            {
                if (_clientAppSettings is not null)
                {
                    try
                    {
                        await _clientAppSettings.WriteFpsAsync(fpsCap.Value).ConfigureAwait(false);
                    }
                    catch (ClientAppSettingsWriteException)
                    {
                        // Spec §7.7: degraded, non-blocking. Continue with the launch.
                    }
                }

                await ApplyFpsCapAsync(fpsCap.Value).ConfigureAwait(false);
            }

            var result = await ExecuteLaunchAsync(cookie, target, browserTrackerId).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _launchGate.Release();
        }
    }

    /// <summary>
    /// Apply this account's FPS cap so it survives a close-together launch, then return. All the
    /// waiting happens HERE, before Process.Start — not after it. The party that overwrites our
    /// value is the previous client (which re-persists its own cap for ~9s), so there is nothing
    /// useful to wait for once our own process has started.
    /// </summary>
    private async Task ApplyFpsCapAsync(int fpsCap)
    {
        if (_globalBasicSettings is null)
        {
            return;
        }

        if (_settingsProbe is null)
        {
            // No probe wired. Not reachable in the shipped app today (App.xaml.cs always resolves
            // IGlobalBasicSettingsProbe via GetRequiredService alongside the writer) -- but a future
            // caller that constructs a launcher with a writer and no probe (a second registration, a
            // plugin host, an integration harness) must not silently land back on the exact
            // write-and-hope behaviour that shipped the 2026-08-01 wrong-cap bug. Attempt the write
            // (doing something beats doing nothing) but say loudly that the confirm-and-retry
            // protection is absent, so this degrade is visible in a support bundle instead of
            // discovered the same way the original bug was.
            _log.LogWarning(
                "No IGlobalBasicSettingsProbe wired; writing FPS cap {Cap} without confirming it survives " +
                "a close-together launch.", fpsCap);
            try
            {
                await _globalBasicSettings.WriteFramerateCapAsync(fpsCap).ConfigureAwait(false);
            }
            catch (GlobalBasicSettingsWriteException)
            {
                // Non-blocking. Roblox falls back to whatever cap is currently in the file.
            }
            return;
        }

        await FpsCapSettler.SettleAsync(
            _settingsProbe, _globalBasicSettings, fpsCap, _timeProvider, _log, CancellationToken.None,
            launchBaselineUtc: _lastLaunchMtimeUtc)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Remember the settings file's mtime right after a successful <c>Process.Start</c>, for the
    /// NEXT launch's proof-of-read gate (<see cref="ApplyFpsCapAsync"/>). Called unconditionally on
    /// every successful launch — not just ones with an <c>fpsCap</c> — because a launch with no cap
    /// this time still needs to leave a fresh baseline behind for whichever future launch does have
    /// one. No-ops (stays <see langword="null"/>) when no probe is wired, matching every other
    /// no-probe degrade in this class.
    /// </summary>
    private void RecordLaunchBaseline() => _lastLaunchMtimeUtc = _settingsProbe?.GetLastWriteTimeUtc();

    private async Task<LaunchResult> ExecuteLaunchAsync(string cookie, LaunchTarget target, long? stableBrowserTrackerId)
    {
        // FollowFriend doesn't need place resolution — Roblox follows the user wherever they are.
        // Place / PrivateServer are already concrete. DefaultGame resolves through favorites + settings.
        var resolved = target is LaunchTarget.DefaultGame
            ? await ResolveDefaultAsync().ConfigureAwait(false)
            : target;

        if (resolved is null)
        {
            // Defensive guard, not fully dead: ResolveDefaultAsync (the DefaultGame path) now always
            // returns non-null (falls back to LaunchTarget.Home per spec §5). This still catches null
            // from explicit-selection callers upstream (JoinByLinkWindow, MainViewModel) that resolve
            // a pasted/typed URL via LaunchTarget.FromUrl before reaching ExecuteLaunchAsync.
            return new LaunchResult.Failed(
                "No default Roblox game configured. Add one in Games (header button), or pass an explicit target.");
        }

        AuthTicket ticket;
        try
        {
            ticket = await _api.GetAuthTicketAsync(cookie).ConfigureAwait(false);
        }
        catch (CookieExpiredException)
        {
            return new LaunchResult.CookieExpired();
        }
        catch (SessionLimitedException)
        {
            return new LaunchResult.Limited();
        }
        catch (Exception ex)
        {
            return new LaunchResult.Failed($"Failed to obtain auth ticket: {ex.Message}");
        }

        // Stable per-account btid when the caller has one persisted (v1.8.1 trust hygiene);
        // random one-shot fallback preserves the pre-v1.8.1 behavior for callers without it.
        var browserTrackerId = (stableBrowserTrackerId ?? _browserTrackerIdFactory()).ToString();
        var launchTime = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        // Home skips place resolution entirely — no placelauncherurl, no BuildLaunchUri (which
        // hardcodes launchmode:play and requires a non-empty placeUrl).
        var uri = resolved is LaunchTarget.Home
            ? BuildAppLaunchUri(ticket.Ticket, launchTime, browserTrackerId)
            : BuildLaunchUri(ticket.Ticket, launchTime, browserTrackerId, BuildPlaceLauncherUrl(resolved, browserTrackerId));

        try
        {
            var launchedAtUtc = _timeProvider.GetUtcNow();
            var pid = _processStarter.StartViaShell(uri);
            RecordLaunchBaseline();
            return new LaunchResult.Started(pid, launchedAtUtc);
        }
        catch (Win32Exception)
        {
            return new LaunchResult.Failed(RobloxNotInstalledMessage);
        }
        catch (Exception ex)
        {
            return new LaunchResult.Failed($"Process.Start failed: {ex.Message}");
        }
    }

    // F-093: LaunchAsync(string cookie, string? placeUrl, ...) and ExecuteLegacyLaunchAsync
    // were deleted here. The overload resolved a place URL through three tiers and the last was
    // AppSettings.DefaultPlaceUrl, a setting nothing read and no UI could write. Nothing called
    // the overload either — MainViewModel has always used the LaunchTarget one. Removing the
    // setting alone would have left that tier calling a method that no longer exists, so the row
    // asked for both and both went.


    private async Task<LaunchTarget?> ResolveDefaultAsync()
    {
        if (_favorites is not null)
        {
            var defaultFavorite = await _favorites.GetDefaultAsync().ConfigureAwait(false);
            if (defaultFavorite is not null)
            {
                return new LaunchTarget.Place(defaultFavorite.PlaceId);
            }
        }

        // No favorite default -> open Roblox home (signed in). The legacy settings DefaultPlaceUrl is
        // vestigial per spec §5 and intentionally ignored by resolution; a user sets a real default game
        // to launch straight into it. Encourages, doesn't require, a default.
        return new LaunchTarget.Home();
    }

    /// <summary>
    /// Build the <c>placelauncherurl</c> Roblox expects for each launch target shape.
    /// Public for snapshot testing.
    /// </summary>
    public static string BuildPlaceLauncherUrl(LaunchTarget target, string browserTrackerId)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrEmpty(browserTrackerId))
        {
            throw new ArgumentException("Browser tracker id must not be empty.", nameof(browserTrackerId));
        }

        return target switch
        {
            LaunchTarget.Place place when place.PlaceId > 0 =>
                $"{PlaceLauncherEndpoint}?request=RequestGame" +
                $"&browserTrackerId={browserTrackerId}" +
                $"&placeId={place.PlaceId}" +
                "&isPlayTogetherGame=false",

            // One specific server. Verified live 2026-08-02: this shape put a recycled client back
            // into the job id it left, still holding 34 seconds later. placeId here is presence's
            // place, NOT the place the account originally launched into — see ServerInstance.
            LaunchTarget.GameJob job when job.PlaceId > 0 && !string.IsNullOrWhiteSpace(job.JobId) =>
                $"{PlaceLauncherEndpoint}?request=RequestGameJob" +
                $"&browserTrackerId={browserTrackerId}" +
                $"&placeId={job.PlaceId}" +
                $"&gameId={Uri.EscapeDataString(job.JobId)}" +
                "&isPlayTogetherGame=false",

            // Emit ONLY the matching slot. The two codes are not interchangeable — sending a
            // linkCode in the accessCode slot returns permission-denied even on owner servers.
            // Roblox resolves linkCode -> server-side at launch, so we hand off either form.
            LaunchTarget.PrivateServer ps when ps.PlaceId > 0 && !string.IsNullOrEmpty(ps.Code) =>
                $"{PlaceLauncherEndpoint}?request=RequestPrivateGame" +
                $"&browserTrackerId={browserTrackerId}" +
                $"&placeId={ps.PlaceId}" +
                (ps.Kind == PrivateServerCodeKind.LinkCode
                    ? $"&linkCode={Uri.EscapeDataString(ps.Code)}"
                    : $"&accessCode={Uri.EscapeDataString(ps.Code)}"),

            // RequestFollowUser doesn't carry placeId — Roblox follows the user wherever they are
            // and does the permission check server-side (works for public + private if allowed).
            LaunchTarget.FollowFriend ff when ff.UserId > 0 =>
                $"{PlaceLauncherEndpoint}?request=RequestFollowUser" +
                $"&browserTrackerId={browserTrackerId}" +
                $"&userId={ff.UserId}",

            LaunchTarget.DefaultGame =>
                throw new InvalidOperationException(
                    "DefaultGame must be resolved before building the placelauncherurl. " +
                    "Did you forget to call ResolveDefaultAsync?"),

            // Home has no placelauncherurl at all — it must never reach this method. Defensive
            // mirror of the DefaultGame throw arm above; callers branch on LaunchTarget.Home in
            // ExecuteLaunchAsync and route to BuildAppLaunchUri instead.
            LaunchTarget.Home =>
                throw new InvalidOperationException(
                    "Home has no placelauncherurl; build with BuildAppLaunchUri."),

            _ => throw new ArgumentException(
                $"Unsupported or invalid LaunchTarget: {target}", nameof(target)),
        };
    }

    /// <summary>
    /// Pull a numeric place id out of any of the input shapes <see cref="NormalizeToPlaceLauncherUrl"/>
    /// accepts. Returns null if no place id can be located. Used by the Games dialog to extract
    /// place ids from pasted URLs.
    /// </summary>
    public static long? ExtractPlaceId(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        var match = Regex.Match(input, @"placeId=(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && long.TryParse(match.Groups[1].Value, out var fromQuery))
        {
            return fromQuery;
        }

        match = Regex.Match(input, @"roblox\.com/games/(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && long.TryParse(match.Groups[1].Value, out var fromPath))
        {
            return fromPath;
        }

        if (long.TryParse(input.Trim(), out var bare) && bare > 0)
        {
            return bare;
        }

        return null;
    }

    /// <summary>
    /// Convert a user-friendly Roblox game URL into the <c>PlaceLauncher.ashx</c> form
    /// <c>RobloxPlayerLauncher</c> expects. Accepts:
    /// <list type="bullet">
    ///   <item><c>https://www.roblox.com/games/{id}/{slug}</c> -- normalized.</item>
    ///   <item><c>https://www.roblox.com/games/{id}</c> -- normalized.</item>
    ///   <item>An existing PlaceLauncher URL -- passed through unchanged.</item>
    ///   <item>Bare numeric place id -- wrapped in PlaceLauncher form.</item>
    ///   <item>Anything else -- passed through (caller may have a non-standard form).</item>
    /// </list>
    /// </summary>
    public static string NormalizeToPlaceLauncherUrl(string input, string browserTrackerId)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (input.Contains("PlaceLauncher.ashx", StringComparison.OrdinalIgnoreCase))
        {
            return input;
        }

        var match = Regex.Match(input, @"roblox\.com/games/(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var placeId = match.Groups[1].Value;
            return $"{PlaceLauncherEndpoint}" +
                   "?request=RequestGame" +
                   $"&browserTrackerId={browserTrackerId}" +
                   $"&placeId={placeId}" +
                   "&isPlayTogetherGame=false";
        }

        if (Regex.IsMatch(input.Trim(), @"^\d+$"))
        {
            var placeId = input.Trim();
            return $"{PlaceLauncherEndpoint}" +
                   "?request=RequestGame" +
                   $"&browserTrackerId={browserTrackerId}" +
                   $"&placeId={placeId}" +
                   "&isPlayTogetherGame=false";
        }

        return input;
    }

    /// <summary>
    /// Pure URI construction -- public for snapshot testing. Shape per spec §5.6 + the
    /// spike-time finding that <c>placelauncherurl</c> is required (not optional).
    /// </summary>
    public static string BuildLaunchUri(
        string ticket,
        long launchTime,
        string browserTrackerId,
        string placeUrl)
    {
        if (string.IsNullOrEmpty(ticket))
        {
            throw new ArgumentException("Ticket must not be empty.", nameof(ticket));
        }
        if (string.IsNullOrEmpty(placeUrl))
        {
            throw new ArgumentException("Place URL must not be empty.", nameof(placeUrl));
        }
        if (string.IsNullOrEmpty(browserTrackerId))
        {
            throw new ArgumentException("Browser tracker id must not be empty.", nameof(browserTrackerId));
        }

        var uri = new StringBuilder();
        uri.Append("roblox-player:1");
        uri.Append("+launchmode:play");
        uri.Append("+gameinfo:").Append(ticket);
        uri.Append("+launchtime:").Append(launchTime);
        uri.Append("+placelauncherurl:").Append(Uri.EscapeDataString(placeUrl));
        uri.Append("+browsertrackerid:").Append(browserTrackerId);
        uri.Append("+robloxLocale:en_us+gameLocale:en_us");
        return uri.ToString();
    }

    /// <summary>
    /// Pure URI construction for <see cref="LaunchTarget.Home"/> -- opens Roblox at home,
    /// authenticated (still carries the auth ticket via <c>gameinfo:</c>), joining nothing.
    /// No <c>placelauncherurl</c> segment and <c>launchmode:app</c> instead of <c>play</c> --
    /// distinct from <see cref="BuildLaunchUri"/>, not a variant of it. Public for snapshot testing.
    /// </summary>
    public static string BuildAppLaunchUri(string ticket, long launchTime, string browserTrackerId)
    {
        if (string.IsNullOrEmpty(ticket))
        {
            throw new ArgumentException("Ticket must not be empty.", nameof(ticket));
        }
        if (string.IsNullOrEmpty(browserTrackerId))
        {
            throw new ArgumentException("Browser tracker id must not be empty.", nameof(browserTrackerId));
        }

        var uri = new StringBuilder();
        uri.Append("roblox-player:1");
        uri.Append("+launchmode:app");                       // home, not a game join
        uri.Append("+gameinfo:").Append(ticket);              // still authenticated
        uri.Append("+launchtime:").Append(launchTime);
        uri.Append("+browsertrackerid:").Append(browserTrackerId);
        uri.Append("+robloxLocale:en_us+gameLocale:en_us");
        return uri.ToString();
    }

}
