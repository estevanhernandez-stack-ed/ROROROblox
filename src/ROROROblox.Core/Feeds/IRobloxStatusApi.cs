namespace ROROROblox.Core.Feeds;

/// <summary>The snapshot is set only when <see cref="Outcome"/> is <see cref="FeedOutcome.Ok"/>.</summary>
public sealed record RobloxStatusRead(FeedOutcome Outcome, RobloxStatusSnapshot? Snapshot);

public interface IRobloxStatusApi
{
    /// <summary>One public GET of Roblox's status answer. Never throws.</summary>
    Task<RobloxStatusRead> ReadAsync(CancellationToken ct = default);
}
