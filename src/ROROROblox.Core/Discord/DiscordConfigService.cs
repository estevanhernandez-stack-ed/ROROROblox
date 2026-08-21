namespace ROROROblox.Core.Discord;

/// <summary>
/// The single owner of the Discord settings record (the F-013 prerequisite).
/// <para>
/// <see cref="DiscordConfig"/> is compound — presence, join, two webhook credentials, two alert
/// routings, and the mute list live in one blob, saved whole — so any two writers doing their own
/// load-modify-save can silently revert each other's fields. Until 2026-08-21 the app had exactly
/// two such writers (the Preferences dialog's in-memory snapshot and the account row's context-menu
/// mute), serialized only by Preferences being modal. That guarantee dies the moment Preferences
/// goes modeless, which is exactly what F-013's shell does.
/// </para>
/// <para>
/// This service replaces both write paths and the old <c>DiscordConfigCache</c>: <see cref="MutateAsync"/>
/// is the only way the record changes, serialized by a gate so concurrent mutations compose instead
/// of racing; <see cref="Current"/> is the synchronous, torn-free read the alert dispatcher needs on
/// every dispatch; <see cref="Changed"/> is how views stay views instead of snapshots — the same
/// owner-plus-event contract <c>StreamerIdentityProvider</c> established, with <c>AppSettings</c>'
/// serialized read-modify-write inside.
/// </para>
/// </summary>
public sealed class DiscordConfigService
{
    private readonly IDiscordConfigStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // A reference-typed record behind a volatile field: assignment and read are both atomic, so a
    // reader always sees one coherent config rather than a half-updated one. (Inherited verbatim
    // from DiscordConfigCache, whose one job this field absorbs.)
    private volatile DiscordConfig _current = new();

    // Guarded by _gate. False until the first successful load — see MutateAsync's lazy load.
    private bool _loaded;

    public DiscordConfigService(IDiscordConfigStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>
    /// The current settings, readable synchronously from any thread. Callers that gate per-event
    /// decisions (alert dispatch) read this per use and never cache it — that half of the old
    /// design was already modeless-safe and is kept.
    /// </summary>
    public DiscordConfig Current => _current;

    /// <summary>
    /// Raised after a mutation has persisted AND published, carrying the record that is now both on
    /// disk and in <see cref="Current"/>. Raised inside the write gate so events arrive in write
    /// order — a subscriber must never call <see cref="MutateAsync"/> from its handler (deadlock),
    /// and may be called on whatever thread the mutation completed on, so UI subscribers marshal
    /// themselves (same contract as <c>DiscordPresenceService.StatusChanged</c>).
    /// </summary>
    public event EventHandler<DiscordConfig>? Changed;

    /// <summary>
    /// Load the persisted record into <see cref="Current"/>. Called once at startup wiring; safe to
    /// call again (a plain reload). Raises no event — <see cref="Changed"/> means "a mutation
    /// landed", and startup order decides who reads the initial state directly.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _current = await _store.LoadAsync().ConfigureAwait(false);
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The only write path. Reads the current record inside the gate, applies <paramref name="mutate"/>,
    /// persists, then publishes and raises <see cref="Changed"/> — in that order, so a throw from
    /// the store leaves <see cref="Current"/> untouched and no event raised: disk and memory cannot
    /// disagree. Callers keep their existing catch-and-tell-the-user shapes; the exception carries
    /// the same message it always did.
    /// </summary>
    public async Task MutateAsync(Func<DiscordConfig, DiscordConfig> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A mutation against a record that was never loaded would compose against defaults and
            // silently wipe every persisted field — the exact failure this owner exists to kill,
            // reachable by nothing more than a startup-ordering mistake. Load first, once.
            if (!_loaded)
            {
                _current = await _store.LoadAsync().ConfigureAwait(false);
                _loaded = true;
            }

            var updated = mutate(_current)
                ?? throw new InvalidOperationException("A Discord config mutation returned null.");
            await _store.SaveAsync(updated).ConfigureAwait(false);
            _current = updated;
            Changed?.Invoke(this, updated);
        }
        finally
        {
            _gate.Release();
        }
    }
}
