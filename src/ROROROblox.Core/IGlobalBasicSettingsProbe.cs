namespace ROROROblox.Core;

/// <summary>
/// Read side of Roblox's shared user-settings file — the counterpart to
/// <see cref="IGlobalBasicSettingsWriter"/>.
/// <para>
/// Exists because a starting Roblox client re-persists its own FramerateCap to this file
/// repeatedly for ~9 seconds after launch (measured 2026-08-02). To set a per-account cap that
/// survives, we have to observe when the file stops changing and confirm our write held.
/// </para>
/// </summary>
public interface IGlobalBasicSettingsProbe
{
    /// <summary>
    /// The cap currently on disk, or <c>null</c> if the file is missing, locked, malformed, or has
    /// no FramerateCap node. Null means "unknown" — never treat it as a value.
    /// </summary>
    int? ReadFramerateCap();

    /// <summary>
    /// When the file was last written, or <c>null</c> if it is missing or unreadable. Callers use
    /// changes in this value to detect that a client is still writing.
    /// </summary>
    DateTimeOffset? GetLastWriteTimeUtc();
}
