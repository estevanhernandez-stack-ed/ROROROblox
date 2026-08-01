using System;

namespace ROROROblox.Core.Diagnostics;

/// <summary>One account's memory reading. <paramref name="ReadOk"/> false means UNKNOWN — the
/// caller must exclude it from aggregates, never treat it as a zero reading.</summary>
public readonly record struct AccountMemory(
    Guid AccountId,
    long PrivateBytes,
    double GrowthBytesPerHour,
    int MinutesToCeiling,
    bool OverCap,
    bool IsTarget,
    bool ReadOk);
