namespace ROROROblox.Core.Diagnostics;

/// <summary>A running RobloxPlayerBeta.exe process: its PID and whether it currently has a
/// top-level window (windowless = tray-resident client or orphan; windowed = a real game the
/// user may still be playing). <c>CommandLine</c> is null when it could not be read (#241).
/// <c>StartedAt</c> is null when the start time could not be read (access denied, exited mid-scan);
/// the age floor in <see cref="TrayClient"/> treats that as unknown and never sweeps (#222).</summary>
public readonly record struct RobloxProcessInfo(int Pid, bool HasWindow, string? CommandLine = null, DateTimeOffset? StartedAt = null);
