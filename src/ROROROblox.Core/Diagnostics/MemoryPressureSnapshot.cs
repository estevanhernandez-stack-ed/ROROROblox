using System;
using System.Collections.Generic;

namespace ROROROblox.Core.Diagnostics;

/// <summary>Machine-level view. <c>MinutesToCeiling == 0</c> means "no valid projection"
/// OR "already exhausted" — both are cases where the caller should not display a countdown
/// derived from arithmetic it cannot trust. Use <see cref="HasProjection"/> to distinguish.</summary>
public readonly record struct MemoryPressureSnapshot(
    long AvailableBytes,
    double AggregateGrowthBytesPerHour,
    int MinutesToCeiling,
    bool HasProjection,
    Guid? TargetAccountId,
    IReadOnlyList<AccountMemory> Accounts);
