namespace ROROROblox.Core.Library;

/// <summary>The universe ids whose reading changed. Raised on any thread.</summary>
public sealed class GamePulseChangedEventArgs(IReadOnlyCollection<long> universeIds) : EventArgs
{
    public IReadOnlyCollection<long> UniverseIds { get; } = universeIds;
}

/// <summary>What is known about one game right now. <c>Playing</c> is null once the count is 15 minutes old; the date stays.</summary>
public readonly record struct GamePulseView(int? Playing, DateTimeOffset? UpdatedUtc);

/// <summary>
/// In-memory game readings, asked for only what is on screen. Nothing is asked before
/// <see cref="AllowReadsFrom"/>'s instant, and nothing ever when it was never called.
/// </summary>
public interface IGamePulseService
{
    event EventHandler<GamePulseChangedEventArgs>? Changed;

    /// <summary>Null means no line.</summary>
    GamePulseView? TryGet(long universeId);

    /// <summary>What this owner has on screen now. An empty set removes the owner.</summary>
    void SetInterest(object owner, IReadOnlyCollection<long> universeIds);

    /// <summary>False clears every reading, raises <see cref="Changed"/> and asks nothing. Interest is kept.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Nothing is asked before this instant. Unset means never.</summary>
    void AllowReadsFrom(DateTimeOffset instant);
}
