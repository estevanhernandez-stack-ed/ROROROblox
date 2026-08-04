using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// The last-loaded Discord settings, readable synchronously from any thread.
/// <para>
/// <see cref="DiscordConfigStore"/> is async and DPAPI-backed, but <see cref="AlertDispatcher"/>
/// needs the current settings synchronously — its callers are the memory-watchdog crossing and the
/// presence path, and neither may be blocked on a decrypt just to answer "is this trigger routed
/// anywhere at all?". So the settings are cached here and refreshed at the two moments they can
/// change: startup, and the Preferences dialog closing.
/// </para>
/// <para>
/// A reference-typed record behind a <c>volatile</c> field: assignment and read are both atomic,
/// so a reader always sees one coherent config rather than a half-updated one.
/// </para>
/// </summary>
public sealed class DiscordConfigCache
{
    private volatile DiscordConfig _current = new();

    public DiscordConfig Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }
}
