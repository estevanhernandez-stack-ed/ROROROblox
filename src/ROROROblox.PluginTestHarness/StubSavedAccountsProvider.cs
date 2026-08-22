using ROROROblox.App.Plugins;

namespace ROROROblox.PluginTestHarness;

internal sealed class StubSavedAccountsProvider : ISavedAccountsProvider
{
    private readonly IReadOnlyList<SavedAccountSnapshot> _snapshots;
    public StubSavedAccountsProvider(params SavedAccountSnapshot[] snapshots) => _snapshots = snapshots;
    public IReadOnlyList<SavedAccountSnapshot> Snapshot() => _snapshots;
}
