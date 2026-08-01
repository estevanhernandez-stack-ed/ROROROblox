using System;
using System.Collections.Generic;

namespace ROROROblox.Core.Diagnostics;

/// <summary>Machine-level view. <c>MinutesToCeiling == 0</c> means "no valid projection"
/// OR "already exhausted" — both are cases where the caller should not display a countdown
/// derived from arithmetic it cannot trust. Use <see cref="HasProjection"/> to distinguish.
/// <para>
/// A <c>sealed record</c> (reference type), NOT a <c>readonly record struct</c> — deliberate
/// (final-branch review IMPORTANT 4). This type carries a <see cref="long"/>, a
/// <see cref="double"/>, an <see cref="int"/>, a <see cref="bool"/>, a <see cref="Guid"/>? and a
/// reference — far over 8 bytes, so struct assignment is NOT atomic on this runtime.
/// <see cref="MemoryWatchdog"/> writes <c>_last</c> on its own <see cref="System.Threading.Timer"/>
/// callback thread while the UI thread and other background readers call
/// <see cref="MemoryWatchdog.GetSnapshot"/> concurrently; a torn struct read could pair one
/// tick's <c>Accounts</c> with a different tick's <c>MinutesToCeiling</c> — a wrong number stated
/// confidently, which is the exact failure this feature exists to prevent. Reference
/// assignment/read is atomic, so the class form closes that gap for free.
/// </para>
/// </summary>
public sealed record MemoryPressureSnapshot(
    long AvailableBytes,
    double AggregateGrowthBytesPerHour,
    int MinutesToCeiling,
    bool HasProjection,
    Guid? TargetAccountId,
    IReadOnlyList<AccountMemory> Accounts);
