namespace ROROROblox.Core.KnownIssues;

/// <summary>
/// The Roblox version a launch will actually run: the <c>roblox-player</c> handler's binary, falling
/// back to the newest installed folder only when the handler cannot be read (absent, strap-owned, or
/// unreadable). Reuses <see cref="RobloxCompatChecker"/>'s two readers rather than adding a third copy.
/// </summary>
public static class RunningRobloxVersion
{
    public static Version? Read() =>
        Read(RobloxCompatChecker.GetHandlerRobloxVersion, RobloxCompatChecker.GetInstalledRobloxVersion);

    public static Version? Read(Func<string?> handlerVersion, Func<string?> installedVersion)
    {
        ArgumentNullException.ThrowIfNull(handlerVersion);
        ArgumentNullException.ThrowIfNull(installedVersion);

        if (RobloxVersion.TryParseInstalled(SafeRead(handlerVersion), out var handler))
        {
            return handler;
        }

        return RobloxVersion.TryParseInstalled(SafeRead(installedVersion), out var installed) ? installed : null;
    }

    private static string? SafeRead(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            // "We don't know" is the fallback signal, not an error: the notice still shows.
            return null;
        }
    }
}
