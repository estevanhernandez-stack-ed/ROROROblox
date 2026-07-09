using ROROROblox.App.Plugins;

namespace ROROROblox.PluginTestHarness;

/// <summary>
/// Reusable <see cref="IAccountActivityMarker"/> test double for the end-to-end harness.
/// Mirrors <see cref="StubActivityProvider"/>'s shape: records every Mark() call so a
/// test can assert what the plugin credited, without a bespoke fake per test.
/// </summary>
public sealed class StubActivityMarker : IAccountActivityMarker
{
    public List<string> MarkedAccountIds { get; } = new();

    public void Mark(string accountId) => MarkedAccountIds.Add(accountId);
}
