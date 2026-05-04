using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ROROROblox.App.Theming;

/// <summary>
/// Forces every Win11 system title bar in the process to render with the dark immersive
/// theme, so secondary windows (Diagnostics, Settings, About, modals, etc.) match the
/// app's deep-navy chrome instead of the OS-default white.
///
/// MainWindow uses WPF-UI's FluentWindow with ExtendsContentIntoTitleBar, so its system
/// title bar is hidden anyway -- the attribute is harmless there. For every plain Window,
/// this is what swaps the chrome from light to dark.
/// </summary>
internal static class WindowTheming
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE -- documented value 20 on Win10 20H1+ / Win11.
    // (Earlier Win10 builds used 19; we don't target those -- spec §3 declares min Win11.)
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Apply dark title bar to a single window. Safe to call before or after the window's
    /// HWND exists; defers to SourceInitialized when the HWND isn't available yet.
    /// </summary>
    public static void ApplyDarkTitleBar(Window window)
    {
        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            int dark = 1;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Apply();
        }
        else
        {
            window.SourceInitialized += (_, _) => Apply();
        }
    }

    /// <summary>
    /// Auto-apply dark title bar to every Window created in the process. Call once during
    /// App.OnStartup. Uses a class-level routed-event handler so secondary windows opened
    /// later (modals, About, Diagnostics, etc.) get themed without per-window changes.
    /// </summary>
    public static void RegisterGlobalDarkTitleBar()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window w)
                {
                    ApplyDarkTitleBar(w);
                }
            }));
    }
}
