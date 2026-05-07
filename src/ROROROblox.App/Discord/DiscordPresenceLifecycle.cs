using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// The keystone wiring for v1.2 Discord clan-coordination.
///
/// Composes the Layer-1 presence service, the Layer-2 inbound join handler, and the Layer-3
/// webhook poster against the AccountLifecycle event stream. Implements <see cref="IHostedService"/>
/// for shape parity with future Generic Host migration; bootstrapped manually from
/// <c>App.OnStartup</c> in the meantime.
///
/// Hard rule (spec §7): no Discord-related failure may break account launching. Every
/// subscriber wraps in try/catch + log; nothing propagates back through the lifecycle event.
/// </summary>
public sealed class DiscordPresenceLifecycle : IHostedService
{
    private readonly IDiscordPresence _presence;
    private readonly IDiscordWebhook _webhook;
    private readonly IAccountLifecycle _lifecycle;
    private readonly IRobloxLauncher _launcher;
    private readonly IAccountStore _accountStore;
    private readonly ILogger<DiscordPresenceLifecycle> _log;

    private bool _started;

    public DiscordPresenceLifecycle(
        IDiscordPresence presence,
        IDiscordWebhook webhook,
        IAccountLifecycle lifecycle,
        IRobloxLauncher launcher,
        IAccountStore accountStore,
        ILogger<DiscordPresenceLifecycle> log)
    {
        _presence = presence;
        _webhook = webhook;
        _lifecycle = lifecycle;
        _launcher = launcher;
        _accountStore = accountStore;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _log.LogInformation("DiscordPresenceLifecycle.StartAsync — wiring presence + webhook + lifecycle.");
        _presence.JoinRequested += OnJoinRequested;
        _lifecycle.AccountStarted += OnAccountStarted;
        _lifecycle.AccountStopped += OnAccountStopped;
        _started = true;

        try
        {
            await _presence.StartAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Presence init failure is non-fatal — the rest of the wiring still drives webhooks
            // (which don't require Discord IPC).
            _log.LogWarning(ex, "IDiscordPresence.StartAsync threw; presence will reconnect on its own backoff.");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!_started) return;
        _started = false;
        _log.LogInformation("DiscordPresenceLifecycle.StopAsync — disposing.");
        _presence.JoinRequested -= OnJoinRequested;
        _lifecycle.AccountStarted -= OnAccountStarted;
        _lifecycle.AccountStopped -= OnAccountStopped;

        try
        {
            await _presence.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Presence dispose threw; ignoring.");
        }
    }

    // ---- Lifecycle event handlers ----

    private async void OnAccountStarted(object? sender, AccountStartedEventArgs e)
    {
        try
        {
            var state = new RichPresenceState(
                Mode: PresenceMode.AccountsActive,
                ActiveAccountCount: e.CurrentActiveCount,
                CurrentActivity: "Multi-clienting");
            await SafeUpdateStateAsync(state).ConfigureAwait(false);
            await SafePostLaunchAsync(e.CurrentActiveCount).ConfigureAwait(false);
            await SafePostThresholdAsync(e.CurrentActiveCount).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "OnAccountStarted handler threw; ignoring.");
        }
    }

    private async void OnAccountStopped(object? sender, AccountStoppedEventArgs e)
    {
        try
        {
            var state = e.CurrentActiveCount <= 0
                ? new RichPresenceState(PresenceMode.Idle, 0, null)
                : new RichPresenceState(PresenceMode.AccountsActive, e.CurrentActiveCount, "Multi-clienting");

            await SafeUpdateStateAsync(state).ConfigureAwait(false);

            // No party left when nobody is playing.
            if (e.CurrentActiveCount <= 0)
            {
                try { await _presence.ClearPartyAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception px) { _log.LogDebug(px, "ClearPartyAsync threw on stopped→0; ignoring."); }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "OnAccountStopped handler threw; ignoring.");
        }
    }

    // ---- JoinRequested (clanmate clicked Join on this user's presence card) ----

    private async void OnJoinRequested(object? sender, JoinRequestedEventArgs e)
    {
        try
        {
            var accounts = await _accountStore.ListAsync().ConfigureAwait(false);
            var target = accounts
                .Where(a => a.LastLaunchedAt is not null)
                .OrderByDescending(a => a.LastLaunchedAt)
                .FirstOrDefault()
                ?? accounts.FirstOrDefault();

            if (target is null)
            {
                // No saved accounts means we can't launch Roblox via the auth-ticket path.
                // Open the public web URL instead — clanmate can sign into Roblox in their
                // browser and click Play, landing on the same private server (link-code path).
                var webUrl = ConvertToPublicWebUrl(e.ServerShareUrl);
                _log.LogInformation("Discord Join: no saved accounts in RORORO; opening web URL in browser → {WebUrl}", webUrl);
                OpenInDefaultBrowser(webUrl);
                return;
            }

            string cookie;
            try
            {
                cookie = await _accountStore.RetrieveCookieAsync(target.Id).ConfigureAwait(false);
            }
            catch (Exception ckx)
            {
                _log.LogWarning(ckx, "Couldn't retrieve cookie for account {AccountId} during Discord Join; skipping.", target.Id);
                return;
            }

            // The share URL is already in PlaceLauncher.ashx form; the legacy placeUrl overload's
            // NormalizeToPlaceLauncherUrl passes such URLs through unchanged.
            var result = await _launcher.LaunchAsync(cookie, e.ServerShareUrl).ConfigureAwait(false);
            switch (result)
            {
                case LaunchResult.Started started:
                    _log.LogInformation("Discord Join → launched account {AccountId} into shared server (pid {Pid}).", target.Id, started.Pid);
                    break;
                case LaunchResult.CookieExpired:
                    _log.LogWarning("Discord Join couldn't launch — cookie expired for account {AccountId}.", target.Id);
                    break;
                case LaunchResult.Failed failed when failed.Message.Contains("Roblox does not appear to be installed", StringComparison.OrdinalIgnoreCase):
                    // Fallback: open the public Roblox web URL in the user's default browser.
                    // Roblox.com handles the "install Roblox" prompt + post-install join. We're
                    // not the installer; we're just opening the right URL — same flow Roblox
                    // expects for first-time players. Surfaced from CHECKPOINT 2 cross-machine
                    // smoke 2026-05-07: clanmate clicks Join with no Roblox installed, Discord
                    // pulses forever. Now: their browser opens, Roblox handles the rest.
                    var webUrl = ConvertToPublicWebUrl(e.ServerShareUrl);
                    _log.LogInformation("Discord Join: Roblox not installed; opening web URL in browser → {WebUrl}", webUrl);
                    OpenInDefaultBrowser(webUrl);
                    break;
                case LaunchResult.Failed failed:
                    _log.LogWarning("Discord Join launch failed for account {AccountId}: {Message}", target.Id, failed.Message);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "OnJoinRequested handler threw; ignoring.");
        }
    }

    /// <summary>
    /// Convert a PlaceLauncher.ashx URL into the public roblox.com web URL the joiner can
    /// open in a browser. For LinkCode private servers, preserves the share via the
    /// privateServerLinkCode query param so post-install they can still land on the same
    /// server. AccessCode private servers can't be represented in a public URL (owner-shared
    /// only) — falls back to the public game page.
    /// </summary>
    internal static string ConvertToPublicWebUrl(string placeLauncherUrl)
    {
        // Extract placeId + linkCode (linkCode is share-friendly via the public URL;
        // accessCode isn't, so we drop it).
        var placeId = System.Text.RegularExpressions.Regex.Match(
            placeLauncherUrl, @"placeId=(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
        var linkCode = System.Text.RegularExpressions.Regex.Match(
            placeLauncherUrl, @"linkCode=([^&]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;

        if (string.IsNullOrEmpty(placeId))
        {
            // Truly unparseable — fall back to the Roblox home page.
            return "https://www.roblox.com/";
        }

        if (!string.IsNullOrEmpty(linkCode))
        {
            return $"https://www.roblox.com/games/{placeId}?privateServerLinkCode={Uri.EscapeDataString(linkCode)}";
        }
        return $"https://www.roblox.com/games/{placeId}";
    }

    private void OpenInDefaultBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't open browser for Discord Join fallback.");
        }
    }

    // ---- Safe wrappers (every Discord call is best-effort) ----

    private async Task SafeUpdateStateAsync(RichPresenceState state)
    {
        try { await _presence.UpdateStateAsync(state, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _log.LogDebug(ex, "UpdateStateAsync threw; presence stale."); }
    }

    private async Task SafePostLaunchAsync(int count)
    {
        try { await _webhook.PostLaunchAsync(count, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _log.LogDebug(ex, "PostLaunchAsync threw; ignoring."); }
    }

    private async Task SafePostThresholdAsync(int count)
    {
        try { await _webhook.PostAccountThresholdAsync(count, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _log.LogDebug(ex, "PostAccountThresholdAsync threw; ignoring."); }
    }

    private static string Redact(string url)
    {
        // URL goes to logs — keep scheme + host, drop the rest. Accidentally-shared private-server
        // URLs in logs would be a privacy footgun.
        try
        {
            var uri = new Uri(url, UriKind.Absolute);
            return $"{uri.Scheme}://{uri.Host}/…";
        }
        catch
        {
            return "(unparseable)";
        }
    }
}
