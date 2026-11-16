using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ROROROblox.App.AppLifecycle;
using ROROROblox.Tests.Rendering;
using Wpf.Ui.Controls;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// End task / <c>taskkill</c> without <c>/F</c> post <c>WM_CLOSE</c> to the main window, and
/// <c>MainWindow.OnClosing</c> hides on every close, so a polite outside close could never end RoRoRo.
/// The fix identifies the outside close POSITIVELY and fails towards hiding: an outside tool delivers
/// <c>WM_CLOSE</c> with <c>PostMessage</c>, so it comes off the thread's message queue, and WPF raises
/// <c>ComponentDispatcher.ThreadFilterMessage</c> for queued messages only. Every in-app close reaches the
/// window as a SENT <c>WM_CLOSE</c> and never touches that event.
/// <para>
/// These tests drive REAL windows on their own STA threads with a pumping dispatcher, and deliver the real
/// Win32 messages from a different thread (the same shape as another process posting to us). Every wait is
/// bounded, so a regression fails instead of hanging the run. <c>Application.Current</c> is never relied on.
/// </para>
/// </summary>
public class ExternalCloseWatcherTests
{
    private static readonly TimeSpan Bound = TestWaits.Liveness;

    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ---- the decision table -------------------------------------------------------------------------

    [Theory]
    [InlineData(true, false, "Proceed")]
    [InlineData(true, true, "Proceed")]
    [InlineData(false, true, "QuitApp")]
    [InlineData(false, false, "HideToTray")]
    public void Decide_IsTheThreeRowTable(bool shuttingDown, bool external, string expected)
        => Assert.Equal(Enum.Parse<CloseDisposition>(expected), CloseRequestPolicy.Decide(shuttingDown, external));

    [Fact]
    public void Decide_ShuttingDown_ProceedsEvenWhenTheCloseLooksExternal()
    {
        // The app is already exiting (the tray's Quit, or OnExit closing windows): never cancel that.
        Assert.Equal(CloseDisposition.Proceed, CloseRequestPolicy.Decide(isShuttingDown: true, isExternalClose: true));
    }

    // ---- a real window on a pumping STA thread -----------------------------------------------------

    private sealed class UiThread
    {
        public Dispatcher Dispatcher { get; }
        public Thread Thread { get; }

        private UiThread(Dispatcher dispatcher, Thread thread)
        {
            Dispatcher = dispatcher;
            Thread = thread;
        }

        public static UiThread Start()
        {
            Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            { IsBackground = true, Name = "sta-external-close" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(ready.Wait(Bound), "The UI thread never came up.");
            return new UiThread(dispatcher!, thread);
        }

        public T Invoke<T>(Func<T> work)
        {
            var op = Dispatcher.InvokeAsync(work);
            Assert.True(op.Task.Wait(Bound), "Work on the UI thread did not finish.");
            return op.Task.Result;
        }

        public void Invoke(Action work) => Invoke(() => { work(); return 0; });

        /// <summary>Runs after everything already queued at higher priority, then returns.</summary>
        public void DrainQueue() => Assert.True(
            Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task.Wait(Bound),
            "The UI dispatcher never drained.");

        public void Stop()
        {
            try { if (!Dispatcher.HasShutdownStarted) Dispatcher.InvokeShutdown(); } catch { }
            Thread.Join(Bound);
        }
    }

    /// <summary>
    /// A shown window with a watcher, whose Closing handler records what the watcher said (the same
    /// call <c>MainWindow.OnClosing</c> makes) and cancels, as the main window does.
    /// </summary>
    private sealed class Scenario : IDisposable
    {
        public UiThread Ui { get; } = UiThread.Start();
        public Window Window { get; private set; } = null!;
        public ExternalCloseWatcher Watcher { get; private set; } = null!;
        public IntPtr Handle { get; private set; }

        private readonly object _gate = new();
        private readonly List<bool> _classified = [];
        private readonly SemaphoreSlim _closingSeen = new(0);
        private bool _allowRealClose;

        public Scenario()
        {
            Ui.Invoke(() =>
            {
                Window = NewWindow();
                Watcher = new ExternalCloseWatcher(Window);
                Window.Closing += (_, e) =>
                {
                    var external = Watcher.TryConsume();
                    lock (_gate) { _classified.Add(external); }
                    e.Cancel = !_allowRealClose;
                    _closingSeen.Release();
                };
                Window.Show();
                Handle = new WindowInteropHelper(Window).Handle;
            });
        }

        public static Window NewWindow() => new()
        {
            Width = 120,
            Height = 80,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            // An Application elsewhere in this process (the render host's) puts an implicit Window style
            // from WPF-UI on every window, on every thread, and that style changes AllowsTransparency after
            // the handle exists. A local null Style opts these probe windows out, so the result does not
            // depend on which test ran first.
            Style = null,
        };

        public IReadOnlyList<bool> Classified
        {
            get { lock (_gate) { return _classified.ToArray(); } }
        }

        /// <summary>Waits for one more close attempt on the watched window to have been classified.</summary>
        public void WaitForAClose(string what) =>
            Assert.True(_closingSeen.Wait(Bound), $"{what}: the window never reported a close attempt.");

        public void Dispose()
        {
            try
            {
                Ui.Invoke(() =>
                {
                    _allowRealClose = true;
                    Window.Close();
                    Watcher.Dispose();
                });
            }
            catch
            {
                // Cleanup must never fail or hang a test.
            }

            Ui.Stop();
        }
    }

    [Fact]
    public void AQueuedWmClose_IsClassifiedExternal()
    {
        // What taskkill without /F and Task Manager's End task do: PostMessage, from outside our thread.
        using var s = new Scenario();

        Assert.True(PostMessage(s.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        s.WaitForAClose("posted WM_CLOSE");

        Assert.Equal(new[] { true }, s.Classified);
    }

    [Fact]
    public void WindowClose_IsClassifiedInternal()
    {
        using var s = new Scenario();

        s.Ui.Invoke(() => s.Window.Close());
        s.WaitForAClose("Window.Close()");

        Assert.Equal(new[] { false }, s.Classified);
    }

    [Fact]
    public void ASentSysCommandClose_IsClassifiedInternal()
    {
        // Alt+F4, the taskbar's Close and the system menu all arrive as WM_SYSCOMMAND/SC_CLOSE, which
        // DefWindowProc turns into a SENT WM_CLOSE. Sent from another thread: SendMessage blocks the
        // caller until the UI thread has handled it, so the answer is ready when it returns.
        using var s = new Scenario();

        SendMessage(s.Handle, WM_SYSCOMMAND, (IntPtr)SC_CLOSE, IntPtr.Zero);
        s.WaitForAClose("sent WM_SYSCOMMAND/SC_CLOSE");

        Assert.Equal(new[] { false }, s.Classified);
    }

    [Fact]
    public void APostedSysCommandClose_ProducesAClassifiableCloseToo_AndIsInternal()
    {
        // Documents what Alt+F4 looks like if a tool POSTS the syscommand instead: the WM_SYSCOMMAND is
        // queued, but the WM_CLOSE DefWindowProc then makes from it is sent. Only a queued WM_CLOSE counts.
        using var s = new Scenario();

        Assert.True(PostMessage(s.Handle, WM_SYSCOMMAND, (IntPtr)SC_CLOSE, IntPtr.Zero));
        s.WaitForAClose("posted WM_SYSCOMMAND/SC_CLOSE");

        Assert.Equal(new[] { false }, s.Classified);
    }

    [Fact]
    public void AQueuedWmCloseForADifferentWindowOnTheSameThread_IsNotExternalForTheWatchedOne()
    {
        using var s = new Scenario();
        var otherClosing = new SemaphoreSlim(0);
        IntPtr otherHandle = IntPtr.Zero;
        Window? other = null;
        s.Ui.Invoke(() =>
        {
            other = Scenario.NewWindow();
            other.Closing += (_, e) => { e.Cancel = true; otherClosing.Release(); };
            other.Show();
            otherHandle = new WindowInteropHelper(other).Handle;
        });

        Assert.True(PostMessage(otherHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        Assert.True(otherClosing.Wait(Bound), "The other window never saw its close.");
        s.Ui.DrainQueue();

        Assert.False(s.Watcher.Pending, "A close queued for another window was noted against the watched one.");
        Assert.Empty(s.Classified);

        // And the watched window's own next close is still internal.
        s.Ui.Invoke(() => s.Window.Close());
        s.WaitForAClose("Window.Close() after the other window's close");
        Assert.Equal(new[] { false }, s.Classified);
    }

    [Fact]
    public void TheNoteIsClearedAfterOneClose_SoAnInternalCloseThatFollowsAnExternalOneHides()
    {
        using var s = new Scenario();

        Assert.True(PostMessage(s.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        s.WaitForAClose("posted WM_CLOSE");
        s.Ui.Invoke(() => s.Window.Close());
        s.WaitForAClose("Window.Close() after the posted one");
        SendMessage(s.Handle, WM_SYSCOMMAND, (IntPtr)SC_CLOSE, IntPtr.Zero);
        s.WaitForAClose("SC_CLOSE after the posted one");

        Assert.Equal(new[] { true, false, false }, s.Classified);
        Assert.False(s.Watcher.Pending);
    }

    [Fact]
    public void ANoteForACloseThatNeverReachedTheWindow_DoesNotOutliveItsMessage()
    {
        // A hook that swallows WM_CLOSE means OnClosing never runs, so nothing consumes the note. It must
        // not sit there waiting for the next, unrelated close.
        using var s = new Scenario();
        var swallowed = new SemaphoreSlim(0);
        s.Ui.Invoke(() =>
        {
            var swallowedOne = false;
            HwndSource.FromHwnd(s.Handle)!.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == (int)WM_CLOSE && !swallowedOne)
                {
                    swallowedOne = true;
                    handled = true;
                    swallowed.Release();
                }

                return IntPtr.Zero;
            });
        });

        Assert.True(PostMessage(s.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        Assert.True(swallowed.Wait(Bound), "The hook never saw the posted WM_CLOSE.");
        s.Ui.DrainQueue();
        Assert.False(s.Watcher.Pending, "The note outlived the message it belonged to.");

        s.Ui.Invoke(() => s.Window.Close());
        s.WaitForAClose("Window.Close() after a swallowed WM_CLOSE");
        Assert.Equal(new[] { false }, s.Classified);
    }

    [Fact]
    public void ADisposedWatcher_NotesNothing()
    {
        using var s = new Scenario();
        s.Ui.Invoke(() => s.Watcher.Dispose());
        Assert.False(s.Ui.Invoke(() => s.Watcher.IsAttached));

        Assert.True(PostMessage(s.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
        s.WaitForAClose("posted WM_CLOSE after Dispose");

        Assert.Equal(new[] { false }, s.Classified);
    }

    [Fact]
    public void TheHookIsRemovedWhenTheWindowCloses()
    {
        var ui = UiThread.Start();
        try
        {
            ExternalCloseWatcher? watcher = null;
            ui.Invoke(() =>
            {
                var w = Scenario.NewWindow();
                watcher = new ExternalCloseWatcher(w);
                w.Show();
                Assert.True(watcher.IsAttached);
                w.Close();
            });

            Assert.False(ui.Invoke(() => watcher!.IsAttached), "The watcher is still subscribed after its window closed.");
        }
        finally
        {
            ui.Stop();
        }
    }

    [Fact]
    public void ThreadFilterMessage_IsStillRaised_WhileAModalDialogIsUp()
    {
        // RoRoRo shows modal dialogs over the main window. ShowDialog runs WPF's own nested pump, which
        // is the same message loop as the outer one, so a queued WM_CLOSE for the owner must still be
        // seen as external. Measured here rather than argued.
        using var s = new Scenario();
        var dialogWasOpenAtTheClose = false;
        using var done = new ManualResetEventSlim(false);

        s.Ui.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Scenario.NewWindow();
            dialog.Owner = s.Window;
            // Started before ShowDialog: the timer's ticks run inside the dialog's own nested pump.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            var started = Environment.TickCount64;
            var posted = false;
            timer.Tick += (_, _) =>
            {
                if (!posted)
                {
                    posted = true;
                    PostMessage(s.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                if (s.Classified.Count > 0 || Environment.TickCount64 - started > Bound.TotalMilliseconds)
                {
                    timer.Stop();
                    dialogWasOpenAtTheClose = dialog.IsVisible;
                    dialog.Close();
                }
            };
            timer.Start();
            dialog.ShowDialog();
            done.Set();
        });

        Assert.True(done.Wait(Bound + Bound), "The modal dialog never finished.");
        Assert.True(dialogWasOpenAtTheClose, "The owner's close was classified only after the dialog had gone.");
        Assert.Equal(new[] { true }, s.Classified);
    }

    // ---- the title bar's X ------------------------------------------------------------------------

    [WindowRenderFact]
    public void TheTitleBarsCloseButton_IsAnInternalClose()
    {
        // MainWindow.xaml has `<ui:TitleBar ShowClose="True">`. Drive the real control: find its Close
        // button in a shown FluentWindow and invoke it exactly as TitleBarButton.InvokeClick does (a
        // ButtonAutomationPeer's Invoke), then ask the watcher what that close was.
        var result = WindowRenderHost.Run(() =>
        {
            var titleBar = new TitleBar { ShowClose = true, ShowMinimize = true, ShowMaximize = true, Title = "probe" };
            var window = new FluentWindow
            {
                Content = titleBar,
                Width = 300,
                Height = 200,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                ShowActivated = false,
            };

            using var watcher = new ExternalCloseWatcher(window);
            var classified = new List<bool>();
            var allowRealClose = false;
            window.Closing += (_, e) =>
            {
                classified.Add(watcher.TryConsume());
                e.Cancel = !allowRealClose;
            };

            try
            {
                window.Show();
                WindowRenderHost.DrainQueue();

                var close = Descendants(titleBar).OfType<TitleBarButton>()
                    .FirstOrDefault(b => b.ButtonType == TitleBarButtonType.Close);
                Assert.True(close is not null, "The TitleBar's Close button never materialised.");

                var invoke = (IInvokeProvider)new ButtonAutomationPeer(close!).GetPattern(PatternInterface.Invoke)!;
                invoke.Invoke();
                DrainToIdle();   // the peer's Invoke posts the click at Input priority, below the host's Loaded drain

                return (titleBar.ForceShutdown, Closes: classified.ToArray());
            }
            finally
            {
                allowRealClose = true;
                window.Close();
            }
        }, "TitleBar close button");

        Assert.False(result.ForceShutdown, "TitleBar.ForceShutdown defaults to true: its X would shut the app down.");
        Assert.Equal(new[] { false }, result.Closes);
    }

    /// <summary>Pumps the current thread until everything queued above ApplicationIdle has run.</summary>
    private static void DrainToIdle()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    [Fact]
    public void TheMainWindowsTitleBar_KeepsTheDefaultCloseBehaviour()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        var xaml = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "MainWindow.xaml"));

        Assert.Contains("<ui:TitleBar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ForceShutdown", xaml, StringComparison.Ordinal);
    }

    // ---- the wiring --------------------------------------------------------------------------------

    [Fact]
    public void MainWindowOnClosing_AsksTheWatcher_AfterSavingPlacement_AndDecidesThroughThePolicy()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        var source = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "MainWindow.xaml.cs"));

        var start = source.IndexOf("protected override void OnClosing(", StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.OnClosing is gone.");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        var body = source[start..end];

        var save = body.IndexOf("SaveWindowPlacement()", StringComparison.Ordinal);
        var consume = body.IndexOf(".TryConsume()", StringComparison.Ordinal);
        var decide = body.IndexOf("CloseRequestPolicy.Decide(", StringComparison.Ordinal);
        Assert.True(save >= 0, "OnClosing no longer saves the window placement.");
        Assert.True(consume > save, "OnClosing must ask the watcher (TryConsume) after saving the placement.");
        Assert.True(decide > consume, "OnClosing must decide through CloseRequestPolicy, using what the watcher said.");
        Assert.Contains("CloseDisposition.QuitApp", body, StringComparison.Ordinal);
        Assert.Contains("BeginInvoke", body, StringComparison.Ordinal);  // posted, so Close() is not re-entered
        Assert.Contains("new ExternalCloseWatcher(this)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAppWiresTheExternalCloseQuit_ThroughTheTraysQuitPath()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        var source = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "App.xaml.cs"));

        var at = source.IndexOf("ExternalCloseQuit", StringComparison.Ordinal);
        Assert.True(at >= 0, "App.xaml.cs never wires MainWindow.ExternalCloseQuit.");
        var wiring = source.Substring(at, Math.Min(600, source.Length - at));
        Assert.Contains("RequestShutdown", wiring, StringComparison.Ordinal);
    }
}
