namespace ROROROblox.Core;

/// <summary>
/// Writes the per-account FPS cap into <c>ClientAppSettings.json</c> at every
/// candidate Roblox version folder (standalone + Microsoft Store / UWP).
/// Spec §5.1.
/// </summary>
public interface IClientAppSettingsWriter
{
    /// <summary>
    /// Set or clear the FPS cap. <paramref name="fps"/> = null removes the
    /// <c>DFIntTaskSchedulerTargetFps</c> key (and the cap-removal flag if we
    /// previously wrote it). Other FFlags in the file are preserved.
    /// </summary>
    /// <exception cref="ClientAppSettingsWriteException">
    /// Roblox version folder not found, or all candidate writes failed.
    /// </exception>
    Task WriteFpsAsync(int? fps, CancellationToken ct = default);
}
