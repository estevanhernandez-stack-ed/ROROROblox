namespace ROROROblox.Core.Notify;

/// <summary>
/// The single owner of the phone-alert settings record — <c>DiscordConfigService</c>'s
/// owner-plus-event contract, applied to the second compound settings blob before a second
/// writer can exist to race the first. <see cref="MutateAsync"/> is the only write path,
/// serialized by a gate; <see cref="Current"/> is the synchronous torn-free read the alert
/// dispatcher takes on every dispatch; <see cref="Changed"/> keeps views views.
/// </summary>
public sealed class PhoneNotifyConfigService
{
    private readonly IPhoneNotifyConfigStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Reference-typed record behind a volatile field: assignment and read are atomic, so a reader
    // always sees one coherent config. Same shape as DiscordConfigService.
    private volatile PhoneNotifyConfig _current = new();

    // Guarded by _gate. False until the first successful load — see MutateAsync's lazy load.
    private bool _loaded;

    public PhoneNotifyConfigService(IPhoneNotifyConfigStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Readable synchronously from any thread; per-dispatch readers never cache it.</summary>
    public PhoneNotifyConfig Current => _current;

    /// <summary>
    /// Raised after a mutation has persisted AND published, inside the write gate — same contract
    /// and same caveats as <c>DiscordConfigService.Changed</c>: never mutate from a handler, and
    /// marshal yourself if you are UI.
    /// </summary>
    public event EventHandler<PhoneNotifyConfig>? Changed;

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

    public async Task MutateAsync(Func<PhoneNotifyConfig, PhoneNotifyConfig> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A mutation composed against a never-loaded record would silently wipe every
            // persisted field — the failure DiscordConfigService documents. Load first, once.
            if (!_loaded)
            {
                _current = await _store.LoadAsync().ConfigureAwait(false);
                _loaded = true;
            }

            var updated = mutate(_current)
                ?? throw new InvalidOperationException("A phone-notify config mutation returned null.");
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
