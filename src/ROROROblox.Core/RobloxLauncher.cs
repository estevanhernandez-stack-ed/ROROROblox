using System.ComponentModel;
using System.Text;

namespace ROROROblox.Core;

/// <summary>
/// Implements <see cref="IRobloxLauncher"/>. Coordinates ticket fetch + URI build + process spawn.
/// Pure URI construction is exposed as <see cref="BuildLaunchUri"/> for snapshot testing without
/// invoking the full async flow.
/// </summary>
public sealed class RobloxLauncher : IRobloxLauncher
{
    private const string RobloxNotInstalledMessage = "Roblox does not appear to be installed.";

    private readonly IRobloxApi _api;
    private readonly IAppSettings _settings;
    private readonly IProcessStarter _processStarter;
    private readonly TimeProvider _timeProvider;
    private readonly Func<long> _browserTrackerIdFactory;

    public RobloxLauncher(IRobloxApi api, IAppSettings settings, IProcessStarter processStarter)
        : this(api, settings, processStarter, TimeProvider.System,
              () => Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999))
    {
    }

    // Visible for tests.
    internal RobloxLauncher(
        IRobloxApi api,
        IAppSettings settings,
        IProcessStarter processStarter,
        TimeProvider timeProvider,
        Func<long> browserTrackerIdFactory)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _browserTrackerIdFactory = browserTrackerIdFactory ?? throw new ArgumentNullException(nameof(browserTrackerIdFactory));
    }

    public async Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null)
    {
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }

        var resolvedPlaceUrl = placeUrl ?? await _settings.GetDefaultPlaceUrlAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(resolvedPlaceUrl))
        {
            return new LaunchResult.Failed(
                "No default Roblox game URL is configured. Set one in Settings or pass a place URL.");
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

        var launchTime = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var browserTrackerId = _browserTrackerIdFactory().ToString();
        var uri = BuildLaunchUri(ticket.Ticket, launchTime, browserTrackerId, resolvedPlaceUrl);

        try
        {
            var pid = _processStarter.StartViaShell(uri);
            return new LaunchResult.Started(pid);
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

    /// <summary>
    /// Pure URI construction — public for snapshot testing. Shape per spec §5.6 + the
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
