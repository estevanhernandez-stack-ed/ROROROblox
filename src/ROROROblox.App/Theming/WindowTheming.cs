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

    // DWMWA_CLOAK — hides the window from DWM composition entirely while the app keeps
    // rendering into it. The primitive Windows itself uses for suspended apps.
    private const int DWMWA_CLOAK = 13;

    /// <summary>
    /// Keeps a window invisible — chrome and all — until its content has actually rendered, then
    /// reveals it in one step. Kills the white first-composite flash on heavy windows.
    /// <para>
    /// The journey that earned this (2026-09-05, the shell's Settings flash, measured by a
    /// burst-capture probe): a dark <c>Background</c> doesn't help — the flash is the HWND's
    /// surface showing before WPF presents anything. Dark chrome at SourceInitialized fixed only
    /// the title bar. WPF's <c>Window.Opacity</c> is silently ignored on a normal window (it
    /// only works with <c>AllowsTransparency</c>, which costs the system title bar). And
    /// <c>WS_EX_LAYERED</c> + alpha 0 gets clobbered — WPF rewrites the extended styles during
    /// Show, and the probe caught the white frame regardless. DWM cloaking is the one lever WPF
    /// never touches: cloak before the first composite, uncloak a beat after ContentRendered,
    /// with a 3-second failsafe so no window can ever be left invisible.
    /// </para>
    /// </summary>
    public static void RevealAfterFirstRender(Window window)
    {
        var revealed = false;

        void Reveal()
        {
            if (revealed) return;
            revealed = true;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int cloak = 0;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref cloak, sizeof(int));
        }

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int cloak = 1;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref cloak, sizeof(int));

            // Failsafe: whatever happens to ContentRendered, the window may not stay invisible.
            _ = window.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await System.Threading.Tasks.Task.Delay(3000);
                Reveal();
            }));
        };

        window.ContentRendered += (_, _) =>
        {
            // One beat later than ContentRendered, which can fire ahead of the actual present.
            _ = window.Dispatcher.BeginInvoke(
                new Action(Reveal), System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

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
