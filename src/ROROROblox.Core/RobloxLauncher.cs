using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

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
    private readonly SemaphoreSlim _launchGate = new(initialCount: 1, maxCount: 1);
    private static readonly TimeSpan FFlagReadHold = TimeSpan.FromMilliseconds(250);

    public RobloxLauncher(
        IRobloxApi api,
        IAppSettings settings,
        IProcessStarter processStarter,
        IFavoriteGameStore? favorites = null,
        IClientAppSettingsWriter? clientAppSettings = null,
        IGlobalBasicSettingsWriter? globalBasicSettings = null)
        : this(api, settings, processStarter, TimeProvider.System,
              () => Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999),
              favorites, clientAppSettings, globalBasicSettings)
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
        IGlobalBasicSettingsWriter? globalBasicSettings = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _browserTrackerIdFactory = browserTrackerIdFactory ?? throw new ArgumentNullException(nameof(browserTrackerIdFactory));
        _favorites = favorites;
        _clientAppSettings = clientAppSettings;
        _globalBasicSettings = globalBasicSettings;
    }

    public async Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null)
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
                if (_globalBasicSettings is not null)
                {
                    // Wins over the FFlag for users who haven't set in-game frame-rate to
                    // Unlimited — i.e. the dominant clan profile (banner-correction 2026-05-07).
                    try
                    {
                        await _globalBasicSettings.WriteFramerateCapAsync(fpsCap.Value).ConfigureAwait(false);
                    }
                    catch (GlobalBasicSettingsWriteException)
                    {
                        // Non-blocking. Roblox falls back to whatever cap is currently in the file.
                    }
                }
            }

            var result = await ExecuteLaunchAsync(cookie, target).ConfigureAwait(false);
            await Task.Delay(FFlagReadHold).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _launchGate.Release();
        }
    }

    private async Task<LaunchResult> ExecuteLaunchAsync(string cookie, LaunchTarget target)
    {
        // FollowFriend doesn't need place resolution — Roblox follows the user wherever they are.
        // Place / PrivateServer are already concrete. DefaultGame resolves through favorites + settings.
        var resolved = target is LaunchTarget.DefaultGame
            ? await ResolveDefaultAsync().ConfigureAwait(false)
            : target;

        if (resolved is null)
        {
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
        catch (Exception ex)
        {
            return new LaunchResult.Failed($"Failed to obtain auth ticket: {ex.Message}");
        }

        var browserTrackerId = _browserTrackerIdFactory().ToString();
        var launchTime = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var placeLauncherUrl = BuildPlaceLauncherUrl(resolved, browserTrackerId);
        var uri = BuildLaunchUri(ticket.Ticket, launchTime, browserTrackerId, placeLauncherUrl);

        try
        {
            var launchedAtUtc = _timeProvider.GetUtcNow();
            var pid = _processStarter.StartViaShell(uri);
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

    public async Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null)
    {
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }

        // Place-URL resolution tiers (legacy path, kept for back-compat):
        //   1. Explicit placeUrl param (caller picked a specific game).
        //   2. The default favorite (IFavoriteGameStore -- new in v1.x: multi-game library).
        //   3. AppSettings.DefaultPlaceUrl (legacy single-URL setting).
        var resolvedPlaceUrl = placeUrl;
        if (string.IsNullOrEmpty(resolvedPlaceUrl) && _favorites is not null)
        {
            var defaultFavorite = await _favorites.GetDefaultAsync().ConfigureAwait(false);
            if (defaultFavorite is not null)
            {
                resolvedPlaceUrl = defaultFavorite.PlaceId.ToString();
            }
        }
        if (string.IsNullOrEmpty(resolvedPlaceUrl))
        {
            resolvedPlaceUrl = await _settings.GetDefaultPlaceUrlAsync().ConfigureAwait(false);
        }
        if (string.IsNullOrEmpty(resolvedPlaceUrl))
        {
            return new LaunchResult.Failed(
                "No default Roblox game configured. Add one in Games (header button), or pass a place URL.");
        }

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
                if (_globalBasicSettings is not null)
                {
                    // Wins over the FFlag for users who haven't set in-game frame-rate to
                    // Unlimited — i.e. the dominant clan profile (banner-correction 2026-05-07).
                    try
                    {
                        await _globalBasicSettings.WriteFramerateCapAsync(fpsCap.Value).ConfigureAwait(false);
                    }
                    catch (GlobalBasicSettingsWriteException)
                    {
                        // Non-blocking. Roblox falls back to whatever cap is currently in the file.
                    }
                }
            }

            var result = await ExecuteLegacyLaunchAsync(cookie, resolvedPlaceUrl).ConfigureAwait(false);
            await Task.Delay(FFlagReadHold).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _launchGate.Release();
        }
    }

    private async Task<LaunchResult> ExecuteLegacyLaunchAsync(string cookie, string resolvedPlaceUrl)
    {
        AuthTicket ticket;
        try
        {
            ticket = await _api.GetAuthTicketAsync(cookie).ConfigureAwait(false);
        }
        catch (CookieExpiredException)
        {
            return new LaunchResult.CookieExpired();
        }
        catch (Exception ex)
        {
            return new LaunchResult.Failed($"Failed to obtain auth ticket: {ex.Message}");
        }

        var launchTime = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var browserTrackerId = _browserTrackerIdFactory().ToString();

        // Normalize: a "public" roblox.com/games/<id>/<slug> URL (what users copy from their browser
        // address bar) is NOT what RobloxPlayerLauncher expects in placelauncherurl. The launcher
        // hits that URL with the auth ticket expecting a JSON game-server connection blob; pointing
        // it at the HTML game page makes Roblox open then exit silently (caught empirically 2026-05-04).
        // Transform to the assetgame.roblox.com/game/PlaceLauncher.ashx form.
        var normalizedPlaceUrl = NormalizeToPlaceLauncherUrl(resolvedPlaceUrl, browserTrackerId);
        var uri = BuildLaunchUri(ticket.Ticket, launchTime, browserTrackerId, normalizedPlaceUrl);

        try
        {
            var launchedAtUtc = _timeProvider.GetUtcNow();
            var pid = _processStarter.StartViaShell(uri);
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

        var defaultUrl = await _settings.GetDefaultPlaceUrlAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(defaultUrl))
        {
            return null;
        }

        // The settings field can hold any of the historical shapes (URL, bare id, PlaceLauncher form).
        // FromUrl handles all of them.
        return LaunchTarget.FromUrl(defaultUrl);
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
}
