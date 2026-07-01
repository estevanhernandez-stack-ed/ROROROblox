using System.Collections.Generic;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Plain-data record passed across the PluginHostService boundary for GetAccountActivity.
/// Kept separate from Core.Diagnostics.AccountActivity so the App layer controls the
/// exact shape exposed to plugins (string account id, unix-ms timestamp, clamped seconds).
/// </summary>
public sealed record AccountActivitySnapshot(string AccountId, long LastActivityUnixMs, long SecondsSinceActivity);

/// <summary>Plugin-facing projection of the ActivityMonitor snapshot. Mirrors IRunningAccountsProvider.</summary>
public interface IActivitySnapshotProvider
{
    /// <summary>Point-in-time snapshot. Callers should treat the result as immutable.</summary>
    IReadOnlyList<AccountActivitySnapshot> Snapshot();
}
