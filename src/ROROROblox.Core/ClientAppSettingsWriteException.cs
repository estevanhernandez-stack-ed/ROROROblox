namespace ROROROblox.Core;

/// <summary>
/// Thrown when <see cref="IClientAppSettingsWriter"/> can't read/merge/write
/// <c>ClientAppSettings.json</c>. Caller (RobloxLauncher) treats this as
/// non-blocking — the launch still proceeds with whatever FPS Roblox decides
/// to use. Spec §7.7.
/// </summary>
public sealed class ClientAppSettingsWriteException : Exception
{
    public ClientAppSettingsWriteException(string message) : base(message) { }
    public ClientAppSettingsWriteException(string message, Exception inner) : base(message, inner) { }
}
