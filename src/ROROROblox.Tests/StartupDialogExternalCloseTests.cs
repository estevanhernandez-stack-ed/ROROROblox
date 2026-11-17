using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ROROROblox.App.About;
using ROROROblox.App.AppLifecycle;
using ROROROblox.App.Modals;
using ROROROblox.Tests.Rendering;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// A polite close from another process (<c>taskkill</c> without <c>/F</c>) while a startup dialog is in front
/// must end the app, not read as the dialog's own answer. Measured 2026-11-17 with a throwaway probe:
/// <c>taskkill</c> posts <c>WM_CLOSE</c> to ONE unowned top-level window, preferring a visible one, and never to
/// an owned window. So the two <c>App.OnStartup</c> dialogs (shown with no owner) are addressed directly, and
/// the owned first-run Welcome window is not: there the posted close reaches the main window and takes the
/// v1.39 path (<c>ExternalCloseWatcherTests</c>).
/// <para>
/// Real dialogs on their own STA thread, no <c>Application</c> object (it is process-global in this test host).
/// The message is posted from another thread, as another process would. Every wait is bounded.
/// </para>
/// </summary>
public class StartupDialogExternalCloseTests
{
    private static readonly TimeSpan Bound = TestWaits.Liveness;
    private const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private enum CloseKind { PostedFromOutside, SentFromInside }

    /// <summary>
    /// Shows the window modally on the shared render host's thread (the one Application this assembly may
    /// have), closes it the given way, and returns it afterwards.
    /// </summary>
    private static T Run<T>(Func<T> build, CloseKind how) where T : Window =>
        WindowRenderHost.Run(() =>
        {
            var w = build();
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -10000;
            w.Top = -10000;
            w.ShowInTaskbar = false;
            w.ShowActivated = false;
            w.ContentRendered += (_, _) =>
            {
                var handle = new WindowInteropHelper(w).Handle;
                if (how == CloseKind.PostedFromOutside)
                {
                    // From another thread, as another process would.
                    _ = Task.Run(() =>
                    {
                        Thread.Sleep(100);
                        PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    });
                }
                else
                {
                    // What a button or the X does in-app: a Window.Close() on the UI thread.
                    _ = w.Dispatcher.BeginInvoke(new Action(() => w.Close()));
                }
            };
            // A hung dialog must fail the test, not the run.
            var guard = new DispatcherTimer { Interval = Bound };
            guard.Tick += (_, _) => { guard.Stop(); w.Close(); };
            guard.Start();
            try { w.ShowDialog(); }
            finally { guard.Stop(); }
            return w;
        }, "startup dialog close");

    private static LeftoverProcessesWindow Leftover() => new(windowless: 1, windowed: 1);

    private static RobloxAlreadyRunningWindow AlreadyRunning() =>
        new(onCloseForMe: () => Task.FromResult(false), onRetry: () => Task.FromResult(false));

    // ---- leftover-clients dialog ------------------------------------------------------------------------

    [WindowRenderFact]
    public void Leftover_APostedClose_IsReportedAsFromOutside()
    {
        var w = Run(Leftover, CloseKind.PostedFromOutside);
        Assert.True(w.ClosedFromOutside);
    }

    [WindowRenderFact]
    public void Leftover_ASentClose_IsNotFromOutside_AndKeepsTodaysOutcome()
    {
        var w = Run(Leftover, CloseKind.SentFromInside);
        Assert.False(w.ClosedFromOutside);
        Assert.Equal(LeftoverCleanupAction.None, w.Action);
    }

    // ---- already-running dialog -------------------------------------------------------------------------

    [WindowRenderFact]
    public void AlreadyRunning_APostedClose_IsReportedAsFromOutside()
    {
        var w = Run(AlreadyRunning, CloseKind.PostedFromOutside);
        Assert.True(w.ClosedFromOutside);
    }

    [WindowRenderFact]
    public void AlreadyRunning_ASentClose_IsNotFromOutside_AndKeepsTheFailClosedDefault()
    {
        var w = Run(AlreadyRunning, CloseKind.SentFromInside);
        Assert.False(w.ClosedFromOutside);
        Assert.Equal(BlockedModalOutcome.Quit, w.Outcome);
    }

    // ---- Welcome window (reachable directly only when it has no owner: the tray tour) ---------------------

    [WindowRenderFact]
    public void Welcome_APostedClose_IsReportedAsFromOutside()
    {
        var w = Run(() => new WelcomeWindow(), CloseKind.PostedFromOutside);
        Assert.True(w.ClosedFromOutside);
    }

    [WindowRenderFact]
    public void Welcome_ASentClose_IsNotFromOutside()
    {
        var w = Run(() => new WelcomeWindow(), CloseKind.SentFromInside);
        Assert.False(w.ClosedFromOutside);
    }

    // ---- the callers act on it ----------------------------------------------------------------------------

    private static string Source(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!, "src", "ROROROblox.App" }.Concat(parts).ToArray()));
    }

    [Fact]
    public void OnStartup_AbandonsStartup_WhenEitherGateDialogWasClosedFromOutside()
    {
        // OnStartup cannot run in a unit test, so pin its shape: right after each dialog's ShowDialog, the
        // outside-close check comes BEFORE the dialog's answer is read, and it shuts down and returns.
        var app = Source("App.xaml.cs");
        foreach (var (show, flag, answer) in new[]
        {
            ("modal.ShowDialog();", "modal.ClosedFromOutside", "modal.Outcome"),
            ("info.ShowDialog();", "info.ClosedFromOutside", "info.Action"),
        })
        {
            var at = app.IndexOf(show, StringComparison.Ordinal);
            Assert.True(at >= 0, $"{show} is gone from App.xaml.cs.");
            var tail = app[at..];
            var flagAt = tail.IndexOf(flag, StringComparison.Ordinal);
            var answerAt = tail.IndexOf(answer, StringComparison.Ordinal);
            Assert.True(flagAt >= 0 && flagAt < answerAt, $"{flag} must be checked before {answer} is read.");
            var branch = tail[flagAt..Math.Min(tail.Length, flagAt + 700)];
            Assert.Contains("Shutdown(0);", branch);
            Assert.Contains("return;", branch);
        }
    }

    [Fact]
    public void BothWelcomeEntryPoints_QuitTheApp_WhenTheTourWasClosedFromOutside()
    {
        var main = Source("MainWindow.xaml.cs");
        var firstRun = main.IndexOf("welcome.ShowDialog();", StringComparison.Ordinal);
        Assert.True(firstRun >= 0);
        var after = main[firstRun..Math.Min(main.Length, firstRun + 500)];
        Assert.Contains("welcome.ClosedFromOutside", after);
        Assert.Contains("ExternalCloseQuit", after);

        var tour = Source("About", "WelcomeWindow.xaml.cs");
        var onDemand = tour.IndexOf("dialog.ShowDialog();", StringComparison.Ordinal);
        Assert.True(onDemand >= 0);
        var after2 = tour[onDemand..];
        Assert.Contains("dialog.ClosedFromOutside", after2);
        Assert.Contains("ExternalCloseQuit", after2);
    }
}
