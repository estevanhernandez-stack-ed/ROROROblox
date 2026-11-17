using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ROROROblox.App.AppLifecycle;

namespace ROROROblox.Tests;

/// <summary>
/// <c>taskkill</c> without <c>/F</c> posts <c>WM_CLOSE</c> to ONE of the app's windows, and not
/// reliably the main one: with the first-run tour up it went to a toast popup, the toast closed, and
/// the app carried on (seen live 2026-11-17). <see cref="ThreadCloseWatcher"/> applies v1.39's rule
/// (a queued <c>WM_CLOSE</c> came from outside) to the whole UI thread.
/// <para>
/// Real windows on their own STA thread with a pumping dispatcher, and real Win32 messages delivered
/// from another thread. Every wait is bounded. <c>Application.Current</c> is never relied on.
/// </para>
/// </summary>
public class ThreadCloseWatcherTests
{
    private static readonly TimeSpan Bound = TestWaits.Liveness;

    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>A pumping STA thread with a watcher and any number of plain windows on it.</summary>
    private sealed class Stage : IDisposable
    {
        private readonly Thread _thread;
        private readonly Dispatcher _dispatcher;
        private readonly List<Window> _windows = [];
        private int _callbacks;

        public ThreadCloseWatcher Watcher { get; private set; } = null!;
        public int Callbacks => Volatile.Read(ref _callbacks);

        public Stage()
        {
            Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            { IsBackground = true, Name = "sta-thread-close" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            Assert.True(ready.Wait(Bound), "The UI thread never came up.");
            _dispatcher = dispatcher!;
            Invoke(() => Watcher = new ThreadCloseWatcher(() => Interlocked.Increment(ref _callbacks)));
        }

        public T Invoke<T>(Func<T> work)
        {
            var op = _dispatcher.InvokeAsync(work);
            Assert.True(op.Task.Wait(Bound), "Work on the UI thread did not finish.");
            return op.Task.Result;
        }

        public void Invoke(Action work) => Invoke(() => { work(); return 0; });

        public void Drain() => Assert.True(
            _dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task.Wait(Bound),
            "The UI dispatcher never drained.");

        /// <summary>A shown window that is nobody's "main window". Returns its handle.</summary>
        public IntPtr AddWindow(Action<Window>? configure = null) => Invoke(() =>
        {
            var w = new Window
            {
                Width = 120, Height = 80, Left = -10000, Top = -10000,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
                // See ExternalCloseWatcherTests: opt out of the render host's implicit Window style.
                Style = null,
            };
            configure?.Invoke(w);
            w.Show();
            _windows.Add(w);
            return new WindowInteropHelper(w).Handle;
        });

        public void Dispose()
        {
            try
            {
                Invoke(() =>
                {
                    Watcher.Dispose();
                    foreach (var w in _windows) { try { w.Close(); } catch { } }
                });
            }
            catch
            {
                // Cleanup must never fail or hang a test.
            }
            try { if (!_dispatcher.HasShutdownStarted) _dispatcher.InvokeShutdown(); } catch { }
            _thread.Join(Bound);
        }
    }

    [Fact]
    public void APostedClose_ToAnyWindowOnTheThread_IsSeen_AndTheCallbackRuns()
    {
        using var stage = new Stage();
        stage.AddWindow();                 // stands for the main window
        var popup = stage.AddWindow();     // stands for the toast taskkill actually picked

        Assert.True(PostMessage(popup, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        stage.Drain();

        Assert.Equal(1, stage.Watcher.Seen);
        Assert.Equal(1, stage.Callbacks);
    }

    [Fact]
    public void Seen_IsAlreadySet_WhenTheWindowTheCloseWasForIsClosing()
    {
        // App.OnStartup reads Seen straight after the ShowDialog that the close ended. The callback is
        // queued and has not run by then, so the count has to move before the message is dispatched.
        using var stage = new Stage();
        var seenDuringClosing = -1;
        var callbacksDuringClosing = -1;
        var handle = stage.AddWindow(w => w.Closing += (_, _) =>
        {
            seenDuringClosing = stage.Watcher.Seen;
            callbacksDuringClosing = stage.Callbacks;
        });

        Assert.True(PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        stage.Drain();

        Assert.Equal(1, seenDuringClosing);
        Assert.Equal(0, callbacksDuringClosing);
        Assert.Equal(1, stage.Callbacks);
    }

    [Fact]
    public void TheAppsOwnClose_IsNotAnOutsideClose()
    {
        using var stage = new Stage();
        var closed = false;
        Window? own = null;
        stage.AddWindow(w => { own = w; w.Closed += (_, _) => closed = true; });

        stage.Invoke(() => own!.Close()); // Window.Close(): a sent WM_CLOSE, never on the queue
        stage.Drain();

        Assert.True(closed);
        Assert.Equal(0, stage.Watcher.Seen);
        Assert.Equal(0, stage.Callbacks);
    }

    [Fact]
    public void APostedSysCommandClose_IsNotAnOutsideClose()
    {
        // The taskbar's "Close window" and Alt+F4 arrive as SC_CLOSE; the WM_CLOSE that follows is sent
        // by the default window procedure. Those are the user's own closes and must keep hiding to the tray.
        using var stage = new Stage();
        var closing = new SemaphoreSlim(0);
        var handle = stage.AddWindow(w => w.Closing += (_, e) => { e.Cancel = true; closing.Release(); });

        Assert.True(PostMessage(handle, WM_SYSCOMMAND, (IntPtr)SC_CLOSE, IntPtr.Zero));
        Assert.True(closing.Wait(Bound), "The window never saw the close SC_CLOSE turns into.");
        stage.Drain();

        Assert.Equal(0, stage.Watcher.Seen);
        Assert.Equal(0, stage.Callbacks);
    }

    [Fact]
    public void AfterDispose_NothingIsSeen()
    {
        using var stage = new Stage();
        var closing = new SemaphoreSlim(0);
        var handle = stage.AddWindow(w => w.Closing += (_, e) => { e.Cancel = true; closing.Release(); });
        stage.Invoke(() => stage.Watcher.Dispose());

        Assert.True(PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        Assert.True(closing.Wait(Bound), "The window never saw its close.");
        stage.Drain();

        Assert.Equal(0, stage.Watcher.Seen);
        Assert.Equal(0, stage.Callbacks);
    }

    [Fact]
    public void Startup_WatchesBeforeTheFirstDialog_AndChecksAfterEachOne()
    {
        // OnStartup cannot run in a test, so this pins its shape: the watcher starts before the gate's
        // first dialog, each dialog's ShowDialog is followed by a check that reads OutsideCloseSeen and
        // returns, and one more check sits before the main window is built.
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate the repo root from the test bin directory.");
        const string file = "src/ROROROblox.App/App.xaml.cs";
        var code = string.Join("\n", SettingsReachabilityTests.WithoutComments(
            file, File.ReadAllLines(Path.Combine(root!, file.Replace('/', Path.DirectorySeparatorChar)))));

        var watch = code.IndexOf("_outsideClose = new AppLifecycle.ThreadCloseWatcher(OnOutsideClose);", StringComparison.Ordinal);
        var firstDialog = code.IndexOf("modal.ShowDialog();", StringComparison.Ordinal);
        var secondDialog = code.IndexOf("info.ShowDialog();", StringComparison.Ordinal);
        var mainWindow = code.IndexOf("var mainWindow = _services.GetRequiredService<MainWindow>();", StringComparison.Ordinal);
        Assert.True(watch >= 0 && firstDialog > watch && secondDialog > firstDialog && mainWindow > secondDialog,
            "The watcher must start before the gate's dialogs, and the main window must come after them.");

        foreach (var (at, name) in new[] { (firstDialog, "already-running"), (secondDialog, "leftover") })
        {
            var after = code.Substring(at, 400);
            Assert.True(after.Contains("OutsideCloseSeen", StringComparison.Ordinal)
                && after.Contains("Shutdown(0);", StringComparison.Ordinal)
                && after.Contains("return;", StringComparison.Ordinal),
                $"The {name} dialog must be followed by an outside-close check that shuts down and returns.");
        }

        var between = code.Substring(secondDialog, mainWindow - secondDialog);
        var lastCheck = between.LastIndexOf("if (OutsideCloseSeen)", StringComparison.Ordinal);
        Assert.True(lastCheck >= 0, "An outside-close check must sit just before the main window is built.");
        Assert.Contains("return;", between[lastCheck..]);
    }
}
