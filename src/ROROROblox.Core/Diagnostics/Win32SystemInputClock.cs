using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Read-only Win32 probe: the tick count of the last system-wide keyboard/mouse input, via
/// <c>GetLastInputInfo</c>. Yields a timestamp tick only -- never keystroke content, never
/// which key/button. <see cref="ActivityMonitor"/> compares consecutive ticks to detect "input
/// advanced since last sample" without knowing what the input was.
/// </summary>
public sealed class Win32SystemInputClock : ISystemInputClock
{
    public uint LastInputTick
    {
        get
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            return PInvoke.GetLastInputInfo(ref lii) ? lii.dwTime : 0u;
        }
    }
}
