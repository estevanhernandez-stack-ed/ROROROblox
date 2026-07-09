using ROROROblox.Core;

namespace ROROROblox.App.SquadLaunch;

/// <summary>
/// Pure ordering for the Squad Launch saved-servers list: the default server (if any) first,
/// then the pre-existing recency order (most-recently-launched, falling back to AddedAt).
/// Extracted static so the ordering is unit-testable without WPF.
/// </summary>
internal static class SquadLaunchOrdering
{
    public static IReadOnlyList<SavedPrivateServer> Order(IReadOnlyList<SavedPrivateServer> servers) =>
        servers
            .OrderByDescending(s => s.IsDefault)
            .ThenByDescending(s => s.LastLaunchedAt ?? s.AddedAt)
            .ToList();
}
