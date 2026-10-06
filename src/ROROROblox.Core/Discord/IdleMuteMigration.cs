namespace ROROROblox.Core.Discord;

/// <summary>
/// Carries a v1.8 <c>MuteIdleAlerts</c> answer forward, once, as a routing decision (v1.33 item 3).
/// <para>
/// <c>MuteIdleAlerts</c> was the only thing <c>IdleAlertPresenter</c> honoured: one bool, the whole
/// roster, no destinations, no cadence, no per-account mute. Idle crossings now raise an
/// <see cref="AlertKind.AccountIdle"/> trigger and route through <c>AlertDispatcher</c> like every
/// other kind, where "muted" has a proper expression — an empty <see cref="DiscordConfig.IdleDestinations"/>.
/// This turns the old answer into the new one so an upgrade does not hand a user back the alerts
/// they switched off.
/// </para>
/// <para>
/// EXACTLY ONCE, and that is the whole reason this is a named unit rather than four lines inside
/// <c>OnStartup</c>. Clearing the flag is what makes it once: without that, a user who upgrades,
/// finds the new Alerts section and ticks Desktop for idle is silenced again by the very next
/// restart, forever, with nothing on screen to explain it.
/// </para>
/// <para>
/// THE ORDER OF THE TWO WRITES IS LOAD-BEARING. The destinations are written first and the flag is
/// cleared second, so a crash (or a locked <c>discord.dat</c>) between them leaves the migration
/// PENDING rather than lost: the flag is the only record that the user ever asked for silence, and
/// clearing it before the empty list landed would un-mute them permanently with nothing left to
/// replay.
/// </para>
/// <para>
/// Writes through <c>DiscordConfigService.MutateAsync</c> rather than the store, because that is
/// the only write path — a load-modify-save of its own would be the second writer that service
/// exists to prevent, and it would also leave <c>Current</c> stale for the dispatcher that reads it
/// on every alert.
/// </para>
/// <para>
/// The key itself STAYS on <c>AppSettings.SettingsBlob</c>. Removing a property changes what an
/// older blob deserializes into, for a saving of one bool; it goes on
/// <c>SettingsReachabilityTests</c>' allow-list instead, because v1.33 item 6 takes its checkbox
/// off the Settings page.
/// </para>
/// </summary>
public static class IdleMuteMigration
{
    /// <summary>
    /// Returns true when the migration actually ran — i.e. this user had idle alerts muted and the
    /// preference has now been carried over. False means there was nothing to carry, which is the
    /// case on every launch after the first and for everyone who never muted.
    /// </summary>
    public static async Task<bool> RunAsync(IAppSettings settings, DiscordConfigService config)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(config);

        if (!await settings.GetMuteIdleAlertsAsync().ConfigureAwait(false)) return false;

        // An EXPLICITLY empty list, not an absent key. An absent IdleDestinations defaults to
        // [Local] on purpose (item 1 — an upgrade must not silence the toast idle alerts have shown
        // since v1.8), so the whole correctness of this migration rests on SaveAsync writing the
        // empty list present. DiscordConfigStoreTests.SaveThenLoad_ExplicitEmptyIdleDestinations_
        // RoundTripsAsEmpty is the guard that says it does.
        await config.MutateAsync(c => c with { IdleDestinations = [] }).ConfigureAwait(false);

        await settings.SetMuteIdleAlertsAsync(false).ConfigureAwait(false);
        return true;
    }
}
