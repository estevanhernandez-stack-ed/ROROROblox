using Windows.Win32;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Read-only Win32 probe: which process owns the current foreground window. Backs
/// <see cref="ActivityMonitor"/>'s per-tick "is a tracked account actually the one the user is
/// looking at" check. Never sets focus, never injects input — <c>GetForegroundWindow</c> and
/// <c>GetWindowThreadProcessId</c> are both pure reads.
/// </summary>
public sealed class Win32ForegroundWindowProbe : IForegroundWindowProbe
{
    public bool TryGetForegroundPid(out int pid)
    {
        pid = 0;
        var hwnd = PInvoke.GetForegroundWindow();
        if (hwnd.IsNull) return false;
        PInvoke.GetWindowThreadProcessId(hwnd, out var processId);
        pid = (int)processId;
        return pid > 0;
    }
}
