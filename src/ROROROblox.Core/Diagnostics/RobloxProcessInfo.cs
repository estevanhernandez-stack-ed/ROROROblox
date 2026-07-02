namespace ROROROblox.Core.Diagnostics;

/// <summary>A running RobloxPlayerBeta.exe process: its PID and whether it currently has a
/// top-level window (windowless = tray-resident client or orphan; windowed = a real game the
/// user may still be playing).</summary>
public readonly record struct RobloxProcessInfo(int Pid, bool HasWindow);
