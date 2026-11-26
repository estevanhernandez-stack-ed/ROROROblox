namespace ROROROblox.Core.Feeds;

public enum RobloxStatusLevel
{
    Operational,
    Maintenance,
    Degraded,
    PartialOutage,
    Outage,
    SecurityEvent,
    Unrecognised,
}

/// <summary>The worst watched row, its level, and how many other watched rows are not operational. No text.</summary>
public sealed record RobloxStatusReading(RobloxStatusRow? Worst, RobloxStatusLevel Level, int OtherRows)
{
    public static RobloxStatusReading None { get; } = new(null, RobloxStatusLevel.Operational, 0);
}

public static class RobloxStatusEvaluator
{
    /// <summary>
    /// A shown row's level is the worst of its pairs. A watched pair absent from the snapshot is unknown:
    /// it contributes nothing and <paramref name="onMissing"/> is called with its row.
    /// </summary>
    public static RobloxStatusReading Evaluate(
        RobloxStatusSnapshot snapshot,
        IReadOnlyList<WatchedCell> watch,
        Action<RobloxStatusRow>? onMissing = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(watch);

        var worstByRow = new Dictionary<RobloxStatusRow, RobloxStatusLevel>();
        foreach (var pair in watch)
        {
            var found = false;
            var level = RobloxStatusLevel.Operational;
            foreach (var cell in snapshot.Cells)
            {
                if (cell.ComponentId != pair.ComponentId || cell.ContainerId != pair.ContainerId)
                {
                    continue;
                }

                found = true;
                var read = LevelOf(cell.Code);
                if (Rank(read) > Rank(level))
                {
                    level = read;
                }
            }

            if (!found)
            {
                onMissing?.Invoke(pair.Row);
                continue;
            }

            if (!worstByRow.TryGetValue(pair.Row, out var existing) || Rank(level) > Rank(existing))
            {
                worstByRow[pair.Row] = level;
            }
        }

        RobloxStatusRow? worst = null;
        var worstLevel = RobloxStatusLevel.Operational;
        var affected = 0;
        foreach (var (row, level) in worstByRow.OrderBy(kv => kv.Key))
        {
            if (Rank(level) == 0)
            {
                continue;
            }

            affected++;
            if (Rank(level) > Rank(worstLevel))
            {
                worst = row;
                worstLevel = level;
            }
        }

        return worst is null ? RobloxStatusReading.None : new RobloxStatusReading(worst, worstLevel, affected - 1);
    }

    /// <summary>Status.io's own table (kb.status.io/developers/status-codes); any other code is Unrecognised.</summary>
    internal static RobloxStatusLevel LevelOf(int code) => code switch
    {
        100 => RobloxStatusLevel.Operational,
        200 => RobloxStatusLevel.Maintenance,
        300 => RobloxStatusLevel.Degraded,
        400 => RobloxStatusLevel.PartialOutage,
        500 => RobloxStatusLevel.Outage,
        600 => RobloxStatusLevel.SecurityEvent,
        _ => RobloxStatusLevel.Unrecognised,
    };

    internal static int Rank(RobloxStatusLevel level) => level switch
    {
        RobloxStatusLevel.Operational => 0,
        RobloxStatusLevel.Maintenance => 1,
        RobloxStatusLevel.Unrecognised => 2,
        RobloxStatusLevel.Degraded => 3,
        RobloxStatusLevel.PartialOutage => 4,
        RobloxStatusLevel.SecurityEvent => 5,
        RobloxStatusLevel.Outage => 6,
        _ => 0,
    };
}
