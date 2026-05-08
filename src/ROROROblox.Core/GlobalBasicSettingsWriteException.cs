namespace ROROROblox.Core;

/// <summary>
/// Thrown when <see cref="IGlobalBasicSettingsWriter"/> can't read/modify/write
/// <c>GlobalBasicSettings_&lt;N&gt;.xml</c>. Caller (RobloxLauncher) treats this as
/// non-blocking — the launch still proceeds with whatever FramerateCap Roblox
/// currently has on disk. Spec banner-correction (2026-05-07).
/// </summary>
public sealed class GlobalBasicSettingsWriteException : Exception
{
    public GlobalBasicSettingsWriteException(string message) : base(message) { }
    public GlobalBasicSettingsWriteException(string message, Exception inner) : base(message, inner) { }
}
