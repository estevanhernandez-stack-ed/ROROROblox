namespace ROROROblox.App.Plugins;

/// <summary>
/// One plugin process exit, as the starter saw it (#242). The host is the only place a plugin's
/// exit code is ever known: a plugin that dies before its own code runs leaves no log of its own.
/// </summary>
/// <param name="ExitCode">Null when the code could not be read.</param>
/// <param name="Uptime">From just before <c>Process.Start</c> to the exit.</param>
public sealed record PluginProcessExit(int Pid, int? ExitCode, TimeSpan Uptime)
{
    /// <summary>
    /// Inside this window an exit counts as "while starting". Generous on purpose: the field case
    /// had a first-run start stall for 14 s before the plugin died, and a plugin that lives past ten
    /// seconds has at least reached its own code.
    /// </summary>
    public static readonly TimeSpan StartupWindow = TimeSpan.FromSeconds(10);

    public PluginExitKind Kind
    {
        get
        {
            if (ExitCode is { } code && IsDotNetHostFailure(code)) return PluginExitKind.CouldNotStart;
            var early = Uptime < StartupWindow;
            if (ExitCode == 0) return early ? PluginExitKind.ClosedAtStartup : PluginExitKind.Stopped;
            return early ? PluginExitKind.CrashedWhileStarting : PluginExitKind.Crashed;
        }
    }

    /// <summary>
    /// The .NET host's own failure range, 0x80008080 to 0x800080A2 (dotnet/runtime
    /// <c>docs/design/features/host-error-codes.md</c>): the plugin never reached managed code, so
    /// relaunching the same files the same way cannot help. <c>0x80008085</c> is the field case.
    /// </summary>
    internal static bool IsDotNetHostFailure(int code)
    {
        var u = unchecked((uint)code);
        return u >= 0x80008080 && u <= 0x800080A2;
    }

    /// <summary>"0x80008085": the form the log uses, so a user can read it off the page and match it.</summary>
    public string CodeHex => ExitCode is { } c ? "0x" + c.ToString("X8") : "?";
}

public enum PluginExitKind
{
    /// <summary>The .NET host could not start the plugin. Restarting cannot help.</summary>
    CouldNotStart,
    /// <summary>Non-zero exit inside the startup window.</summary>
    CrashedWhileStarting,
    /// <summary>Exit 0 inside the startup window: the plugin declined to run (often a second copy).</summary>
    ClosedAtStartup,
    /// <summary>Non-zero exit after the startup window.</summary>
    Crashed,
    /// <summary>Exit 0 after the startup window.</summary>
    Stopped,
}
