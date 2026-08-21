using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Reads a Roblox client's window geometry and fullscreen state from outside the process (F-109).
/// <para>
/// Needed because Roblox saves these only on a clean exit, and RoRoRo's force-close is not one. If
/// we are about to destroy the record, we take our own copy first.
/// </para>
/// </summary>
public readonly record struct ClientWindowState(bool Fullscreen, int X, int Y, int Width, int Height)
{
    /// <summary>A rect we could not read. Never written — see <c>WriteWindowStateAsync</c>.</summary>
    public bool IsUsable => Width > 0 && Height > 0;

    /// <summary>
    /// Reads the window belonging to <paramref name="pid"/>, or null when there is nothing to read.
    /// </summary>
    /// <remarks>
    /// Fullscreen is decided by the presence of a title bar, not by comparing the rect to the
    /// screen. A windowed Roblox restored to a saved size equal to the display is a real window at
    /// a non-zero offset and must not be recorded as fullscreen — measured on a 3440x1440 display,
    /// where windowed came back as (23,27) 3440x1440 WITH a caption and fullscreen as (0,0) without
    /// one. A size comparison would have called both fullscreen and written the wrong answer.
    /// </remarks>
    public static ClientWindowState? Read(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return null;

            process.Refresh();
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero) return null;

            var hwnd = new HWND(handle);
            if (!PInvoke.GetWindowRect(hwnd, out var rect)) return null;

            var style = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
            var hasCaption = (style & (int)WINDOW_STYLE.WS_CAPTION) != 0;

            return new ClientWindowState(
                Fullscreen: !hasCaption,
                X: rect.left,
                Y: rect.top,
                Width: rect.right - rect.left,
                Height: rect.bottom - rect.top);
        }
        catch
        {
            // Exited mid-read, access denied, or no window. All mean "nothing worth saving", and
            // none of them justify failing the stop the caller actually asked for.
            return null;
        }
    }
}
