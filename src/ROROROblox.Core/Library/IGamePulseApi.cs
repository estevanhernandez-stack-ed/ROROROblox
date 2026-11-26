using ROROROblox.Core.Feeds;

namespace ROROROblox.Core.Library;

/// <summary>
/// One batched read of how many people are in a set of games and when each was last updated. Its own
/// interface so no <c>IRobloxApi</c> fake has to move.
/// </summary>
public interface IGamePulseApi
{
    /// <summary>Measured 2026-11-26: 50 ids answer 200, 51 answer 400 (code 9).</summary>
    const int MaxIdsPerRequest = 50;

    /// <summary>Never throws on the network. Throws <see cref="ArgumentException"/> for more than <see cref="MaxIdsPerRequest"/> ids.</summary>
    Task<GamePulseRead> ReadAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default);
}

/// <summary><paramref name="Answer"/> is set only when <paramref name="Outcome"/> is <see cref="FeedOutcome.Ok"/>.</summary>
public sealed record GamePulseRead(FeedOutcome Outcome, GamePulseAnswer? Answer);

/// <summary>An asked id that is not in <paramref name="Found"/> is gone (or had a reading outside its range).</summary>
public sealed record GamePulseAnswer(IReadOnlyDictionary<long, GamePulseReading> Found);

public readonly record struct GamePulseReading(int Playing, DateTimeOffset? UpdatedUtc);
