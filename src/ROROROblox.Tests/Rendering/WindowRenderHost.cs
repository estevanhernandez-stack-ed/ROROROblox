using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

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
/// HOW THE HAZARD <c>ThemedRender</c> NAMES IS AVOIDED. Its warning is that an Application makes
/// <c>Application.Current?.Resources</c> process-global state "altering how every other test in this
/// assembly resolves a theme". That is about THEME BRUSHES, and this host holds none:
/// <see cref="Resources"/> carries only the theme-INDEPENDENT vocabulary — WPF-UI's dictionaries and
/// <c>ControlStyles.xaml</c>, whose own setters reach colours through <c>DynamicResource</c>. It is
/// populated once and <b>never mutated</b>, so there is no per-theme state here to race over and
/// nothing for a concurrent render to observe changing. Theme brushes stay on the window, which is
/// per-render and per-thread.
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

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            // Application's constructor installs the pack:// WebRequest factory and sets
            // Application.Current. OnExplicitShutdown so closing a rendered window never tries to
            // tear the process down mid-run.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            // Theme-INDEPENDENT only, in App.xaml's own merge order (App.xaml:10-15), which is
            // load-bearing there: ControlStyles.xaml has BasedOn="{StaticResource {x:Type Button}}"
            // that resolves at parse time against WPF-UI's dictionary.
            app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
            app.Resources.MergedDictionaries.Add(new ControlsDictionary());
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/ROROROblox.App;component/Controls/ControlStyles.xaml",
                    UriKind.Absolute),
            });

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
