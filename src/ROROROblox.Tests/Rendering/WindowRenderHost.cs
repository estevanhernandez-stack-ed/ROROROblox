using System.Windows;
using System.Windows.Threading;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The one <see cref="Application"/> this assembly is allowed to have, and the STA thread that owns
/// it. Exists so a REAL app <see cref="Window"/> can be constructed at all.
/// <para>
/// WHY AN APPLICATION IS UNAVOIDABLE HERE, when <c>ThemedRender</c> is emphatic that none is ever
/// constructed. A window's markup resolves app styles with <c>{StaticResource}</c> —
/// <c>AboutWindow.xaml:155</c> takes <c>SecondaryStrongButtonStyle</c> that way, and 26 App XAML
/// files do the same. <b><c>StaticResource</c> resolves at PARSE time, inside
/// <c>InitializeComponent()</c></b>, before any caller can merge a dictionary into the finished
/// window. Element → window → application is the whole lookup chain, so with no Application and
/// nothing yet on the window, the parse throws
/// <c>Cannot find resource named 'SecondaryStrongButtonStyle'</c> and the window never exists. Found
/// by the spike on its first run, 2026-08-12.
/// </para>
/// <para>
/// THE HAZARD <c>ThemedRender</c> NAMES, AND WHAT ACTUALLY CONTAINS IT. Its warning is that an
/// Application makes <c>Application.Current?.Resources</c> process-global state "altering how every
/// other test in this assembly resolves a theme". <b>That hazard is real here and is NOT avoided by
/// keeping theme brushes off the Application</b> — an earlier draft of this file claimed exactly
/// that and it was wrong, because App.xaml declares the ten theme slots plus the derived edge as
/// pre-startup seed values, and loading App.xaml is the only way to get the converters that window
/// markup resolves at parse time.
/// </para>
/// <para>
/// What contains it is that those seeds are <b>never mutated and never reached</b>.
/// <c>ThemeService.ApplyTo</c> is only ever called on a per-render dictionary — never on
/// <c>Application.Current.Resources</c> — so nothing here changes between renders and there is
/// nothing for a concurrent test to observe shifting. And resolution finds the themed value first:
/// every render puts a dictionary carrying all eleven slots at host or window level, which wins
/// over the Application. The seeds are a fallback that a correctly-built render never reaches.
/// </para>
/// <para>
/// VERIFIED RATHER THAN ARGUED, by a gate that already existed.
/// <c>RenderedStyleGateTests.TheDerivedEdgeIsTunedToNavyAndFallsShortOnACard</c> asserts EXACT
/// per-theme ratios off real bitmaps — 2.28:1 under flatline against 2.82:1 under brand. If the
/// Application's brand-coloured seeds were leaking into those renders, flatline would report brand's
/// number and that test would fail. It passes. That is the containment, measured by something with
/// no stake in this file being right.
/// </para>
/// <para>
/// WHY ONE LONG-LIVED THREAD RATHER THAN <c>Sta</c>'s FRESH-PER-CALL. <see cref="Application"/> is
/// one-per-AppDomain and has thread affinity, so it cannot be rebuilt per render. That trades away
/// the isolation <c>Sta</c> deliberately buys, and its doc is right that reusing a thread across
/// themes is exactly how one theme's resolution leaks into the next — as a PASS, not a failure. So
/// the trade is not taken on trust: <c>AboutMarkRenderTests.RenderingIsNotContaminatedByThePrevious
/// Theme</c> renders brand, then midnight, then brand again, and fails if the two brand renders
/// differ by a byte.
/// </para>
/// </summary>
internal static class WindowRenderHost
{
    private static readonly Lazy<Dispatcher> Host = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// How many times WPF-UI's context-menu decoration was caught and swallowed (F-105). Exposed so
    /// a test can assert this is a real, counted event rather than a silent catch — a swallow
    /// nobody can see is indistinguishable from a bug nobody has found.
    /// </summary>
    internal static int SwallowedContextMenuFailures;

    private static Dispatcher Start()
    {
        // Before the thread, not inside it: an exception raised on the STA thread below is unhandled
        // and kills the test host, which is the loud half of the failure this prevents. Raised here
        // it reaches the calling test as an ordinary, readable failure.
        RenderEnvironment.RequireClean();

        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            // THE REAL App, loading THE REAL App.xaml — for its resources, and for nothing else.
            //
            // THE COMMENT THAT USED TO BE HERE WAS WRONG, AND IT WAS F-105's ROOT CAUSE. It said
            // "`new App()` does not start anything — only Run() raises OnStartup", and on that
            // basis nobody suspected startup. But with a dispatcher pumping on this thread, startup
            // DOES run: the app's own log file, written by this test host during a render run,
            // reads "ROROROblox starting", "Another instance is running; signaling and exiting",
            // "ROROROblox exiting (code 0)".
            //
            // So with a RoRoRo instance up, the single-instance guard found the mutex held and
            // called Shutdown(0) — disposing the Application underneath renders in flight. The
            // NotSupportedException everyone chased is what pack-URI resolution does against a
            // disposed Application. Four isolation probes hunted a symptom.
            //
            // It also meant every render run executed real startup: writing to the user's real log,
            // scanning the plugin registry, STARTING A REAL PLUGIN PROCESS, and running an update
            // check. A test suite with production side effects, invisible because the failure it
            // caused looked like something else entirely.
            ROROROblox.App.App.SuppressStartupForRenderHarness = true;
            //
            // WHY NOT A HAND-BUILT DICTIONARY. The first version merged WPF-UI + ControlStyles.xaml
            // by hand, on the theory that only theme-INDEPENDENT vocabulary was needed. That failed
            // on MainWindow: App.xaml also declares the seven value converters, and
            // `{StaticResource StringToVisibilityConverter}` resolves at parse time like every other
            // StaticResource, so the window would not construct. Reproducing App.xaml's contents
            // here would be a second copy of the app's resource composition, and this repo has been
            // bitten twice by a definition existing in two places and drifting.
            var app = new ROROROblox.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            // F-105. WPF-UI's ContextMenuLoader posts OnResourceDictionaryLoaded to this dispatcher
            // when ControlsDictionary loads, and that callback resolves a pack:// URI for an editor
            // context-menu style NOTHING here renders. With a RoRoRo instance running it throws
            // NotSupportedException from WebRequest.Create — and because it arrives on the
            // dispatcher rather than on a render's call stack, it took the whole TEST HOST down,
            // aborting the run wherever it happened to be.
            //
            // THE MECHANISM IS STILL UNKNOWN and this does not claim to fix it. What it fixes is the
            // BLAST RADIUS: a decoration this harness never asks for must not be able to kill the
            // process. Scoped as narrowly as the evidence allows — this exact exception type, from
            // this exact WPF-UI type — so a genuine pack:// failure in anything we DO render still
            // fails the test that caused it, which is the whole point of the gates.
            app.DispatcherUnhandledException += (_, args) =>
            {
                if (args.Exception is NotSupportedException
                    && args.Exception.StackTrace?.Contains("ContextMenuLoader", StringComparison.Ordinal) == true)
                {
                    SwallowedContextMenuFailures++;
                    args.Handled = true;
                }
            };

            app.InitializeComponent();

            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "sta-window-render-host",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the host thread and marshals its result back. Renders are
    /// serialized by the dispatcher queue, which is what keeps the shared thread safe.
    /// <para>
    /// QUEUE TIME AND RENDER TIME ARE TIMED SEPARATELY, and the timeout reports both. A shared
    /// serialized host has two quite different ways to be slow — this render is genuinely wedged, or
    /// it sat behind someone else's — and they want opposite fixes. A single elapsed number cannot
    /// tell them apart, which matters because <b>one unexplained 2-failure run was observed on
    /// 2026-08-12 during the spike and could not be reproduced in five subsequent runs</b>,
    /// including a forced rebuild. The names were not captured. Contention on this host is the
    /// leading hypothesis and this instrumentation exists to confirm or kill it if it recurs, rather
    /// than leaving the next person the same non-reproducible report.
    /// </para>
    /// </summary>
    public static T Run<T>(Func<T> work, string what)
    {
        var budget = TimeSpan.FromSeconds(60);
        var queued = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan? startedAfter = null;

        var op = Host.Value.InvokeAsync(() =>
        {
            startedAfter = queued.Elapsed;
            return work();
        });

        if (!op.Task.Wait(budget))
        {
            var waited = startedAfter is { } s
                ? $"it began rendering after {s.TotalSeconds:F1}s of queue wait and then did not "
                  + "finish, so THIS render is the slow one"
                : "it never started, so it was still queued behind an earlier render — treat the "
                  + "FIRST timeout in the run as the cause, not this one";

            throw new TimeoutException(
                $"Window render for '{what}' did not finish within {budget.TotalSeconds:F0}s on the "
                + $"shared host thread: {waited}.");
        }

        return op.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Drains the host dispatcher down to <see cref="DispatcherPriority.Loaded"/>. Same argument as
    /// <c>Sta.DrainQueue</c>: <c>DynamicResource</c> invalidation and template application are
    /// queued, so sampling before this measures the default setter rather than the applied value.
    /// Must be called from the host thread.
    /// </summary>
    public static void DrainQueue()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
