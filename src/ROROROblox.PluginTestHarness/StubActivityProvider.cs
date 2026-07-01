using ROROROblox.App.Plugins;

namespace ROROROblox.PluginTestHarness;

/// <summary>
/// Reusable <see cref="IActivitySnapshotProvider"/> test double for the end-to-end
/// harness. Defaults to an empty snapshot (mirrors <c>EmptyAccounts</c> for
/// IRunningAccountsProvider) but accepts a configurable set of snapshots via the
/// params ctor so consented/denied GetAccountActivity tests can seed activity data
/// without a bespoke fake per test.
/// </summary>
public sealed class StubActivityProvider : IActivitySnapshotProvider
{
    private readonly IReadOnlyList<AccountActivitySnapshot> _snapshots;

    public StubActivityProvider(params AccountActivitySnapshot[] snapshots)
    {
        _snapshots = snapshots;
    }

    public IReadOnlyList<AccountActivitySnapshot> Snapshot() => _snapshots;
}
