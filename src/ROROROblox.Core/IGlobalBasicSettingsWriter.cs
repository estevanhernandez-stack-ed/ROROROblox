namespace ROROROblox.Core;

/// <summary>
/// Writes Roblox's user-facing FramerateCap (Settings → Performance → Frame Rate)
/// into <c>%LOCALAPPDATA%\Roblox\GlobalBasicSettings_&lt;N&gt;.xml</c>. Wins over the
/// <c>DFIntTaskSchedulerTargetFps</c> FFlag for users who haven't already set their
/// in-game cap to Unlimited — which is the dominant clan profile, per smoke 2026-05-07.
/// Spec banner-correction (2026-05-07).
/// </summary>
public interface IGlobalBasicSettingsWriter
{
    /// <summary>
    /// Set the FramerateCap to <paramref name="fps"/>. <c>null</c> leaves the file alone
    /// (we don't have a "Roblox default" to write back, and clearing the existing user
    /// preference would surprise them). Non-null overwrites the existing value.
    /// </summary>
    /// <exception cref="GlobalBasicSettingsWriteException">
    /// File not found, malformed XML, missing FramerateCap node we can't insert, or
    /// write failed for any reason.
    /// </exception>
    Task WriteFramerateCapAsync(int? fps, CancellationToken ct = default);
}
