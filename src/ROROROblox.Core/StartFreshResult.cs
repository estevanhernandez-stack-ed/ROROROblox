namespace ROROROblox.Core;

/// <summary>
/// What <see cref="IAccountStore.StartFreshAsync"/> did. File names only, never paths: the files stay in
/// the data folder beside <c>accounts.dat</c>, and a name is all a message or a log line needs.
/// </summary>
/// <param name="SetAside">The names the vault files were renamed to (never deleted).</param>
/// <param name="Failed">The names of files that could not be set aside; empty on success.</param>
public sealed record StartFreshResult(IReadOnlyList<string> SetAside, IReadOnlyList<string> Failed)
{
    /// <summary>True when nothing failed: either every vault file that existed was set aside and the store
    /// now reads as empty, or <see cref="StoreWasReadable"/> and nothing needed setting aside.</summary>
    public bool Succeeded => Failed.Count == 0;

    /// <summary>
    /// True when the store turned out to read correctly at the moment Start fresh was asked for (the
    /// failure that raised the dialog was a lock that has since gone). Nothing was set aside, and the
    /// store still holds every saved account: the caller should show them, not an empty list.
    /// </summary>
    public bool StoreWasReadable { get; init; }
}
