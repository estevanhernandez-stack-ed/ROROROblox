using System.Windows.Threading;
using ROROROblox.App.Threading;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// A Roblox client that exits while RoRoRo is exiting runs <c>Process.Exited</c> on a thread-pool
/// thread, and the first subscriber marshals through <see cref="WpfUiDispatcher.Invoke"/>. While
/// <c>App.OnExit</c> holds the UI thread the call queues; when the dispatcher shuts down it aborts
/// the queued operation and <c>Dispatcher.Invoke</c> throws <see cref="TaskCanceledException"/> on
/// the pool thread, where nothing catches it. These tests drive a REAL dispatcher on its own STA
/// thread (not <c>Application.Current</c>) through the internal ctor seam.
/// <para>Every wait is bounded, so a regression fails instead of hanging the run.</para>
/// </summary>
public class WpfUiDispatcherTests
{
    private static readonly TimeSpan Bound = TestWaits.Liveness;

    private sealed class DispatcherThread
    {
        public Dispatcher Dispatcher { get; }
        public Thread Thread { get; }

        private DispatcherThread(Dispatcher dispatcher, Thread thread)
        {
            Dispatcher = dispatcher;
            Thread = thread;
        }

        public static DispatcherThread Start()
        {
            Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            Assert.True(ready.Wait(Bound), "Dispatcher thread never came up.");
            return new DispatcherThread(dispatcher!, thread);
        }

        /// <summary>Best-effort cleanup that never throws or hangs a test.</summary>
        public void Stop()
        {
            try { if (!Dispatcher.HasShutdownStarted) Dispatcher.InvokeShutdown(); } catch { }
            Thread.Join(Bound);
        }
    }

    /// <summary>Runs <paramref name="call"/> on a fresh thread-pool thread and returns what it threw.</summary>
    private static Exception? CallFromAnotherThread(Action call)
    {
        Exception? thrown = null;
        var worker = new Thread(() =>
        {
            try { call(); }
            catch (Exception ex) { thrown = ex; }
        })
        { IsBackground = true };
        worker.Start();
        Assert.True(worker.Join(Bound), "The calling thread never returned: Invoke hung.");
        return thrown;
    }

    [Fact]
    public void Invoke_AfterShutdownStarted_DoesNotThrowAndDoesNotRunTheAction()
    {
        var ui = DispatcherThread.Start();
        ui.Dispatcher.InvokeShutdown();
        Assert.True(ui.Thread.Join(Bound), "Dispatcher thread never finished shutting down.");
        Assert.True(ui.Dispatcher.HasShutdownStarted);

        var sut = new WpfUiDispatcher(ui.Dispatcher);
        var ran = false;

        var thrown = CallFromAnotherThread(() => sut.Invoke(() => ran = true));

        Assert.Null(thrown);
        Assert.False(ran, "The action must not run once the dispatcher is shutting down.");
    }

    [Fact]
    public void Invoke_QueuedBehindBusyDispatcherThenAbortedByShutdown_DoesNotThrow()
    {
        var ui = DispatcherThread.Start();
        try
        {
            var sut = new WpfUiDispatcher(ui.Dispatcher);
            using var blockerRunning = new ManualResetEventSlim(false);
            using var callQueued = new ManualResetEventSlim(false);
            var ran = false;

            // Block the UI thread. When the caller's operation is queued behind us, shut the
            // dispatcher down FROM the blocking action, so the queued operation is aborted rather
            // than run (a shutdown posted from outside would queue behind the call and let it run).
            ui.Dispatcher.BeginInvoke(() =>
            {
                blockerRunning.Set();
                callQueued.Wait(Bound);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            });
            Assert.True(blockerRunning.Wait(Bound), "The blocking action never started.");

            // OperationPosted fires on the posting thread once the caller's Invoke is queued.
            ui.Dispatcher.Hooks.OperationPosted += (_, _) => callQueued.Set();

            var thrown = CallFromAnotherThread(() => sut.Invoke(() => ran = true));

            Assert.Null(thrown);
            Assert.False(ran, "The queued action must be aborted by shutdown, not run.");
            Assert.True(ui.Dispatcher.HasShutdownStarted);
        }
        finally
        {
            ui.Stop();
        }
    }

    [Fact]
    public void Invoke_ActionThrowsOperationCanceledOnHealthyDispatcher_Propagates()
    {
        var ui = DispatcherThread.Start();
        try
        {
            var sut = new WpfUiDispatcher(ui.Dispatcher);
            // The action's own cancellation is a real signal; only shutdown's abort is swallowed.
            // WPF treats an OperationCanceledException from a delegate as "this operation was
            // cancelled": the waiting cross-thread caller gets a TaskCanceledException, the same
            // type shutdown's abort produces (measured; it never reaches UnhandledException). So the
            // two cannot be told apart by type, only by dispatcher state, and that is the filter.
            var thrown = CallFromAnotherThread(() => sut.Invoke(() => throw new OperationCanceledException("action's own")));

            Assert.IsAssignableFrom<OperationCanceledException>(thrown);
            Assert.False(ui.Dispatcher.HasShutdownStarted);
        }
        finally
        {
            ui.Stop();
        }
    }

    [Fact]
    public void Invoke_OnTheDispatcherThread_ActionThrowsOperationCanceled_PropagatesToTheCaller()
    {
        // The inline path: the caller IS the dispatcher thread, so the action's own exception is
        // the caller's exception, untouched.
        var dispatcher = Dispatcher.CurrentDispatcher;
        var sut = new WpfUiDispatcher(dispatcher);

        var ex = Assert.Throws<OperationCanceledException>(
            () => sut.Invoke(() => throw new OperationCanceledException("action's own")));

        Assert.Equal("action's own", ex.Message);
    }

    [Fact]
    public void Invoke_OnHealthyDispatcher_RunsTheActionOnTheDispatcherThread()
    {
        var ui = DispatcherThread.Start();
        try
        {
            var sut = new WpfUiDispatcher(ui.Dispatcher);
            var ranOn = -1;

            var thrown = CallFromAnotherThread(() => sut.Invoke(() => ranOn = Environment.CurrentManagedThreadId));

            Assert.Null(thrown);
            Assert.Equal(ui.Thread.ManagedThreadId, ranOn);
        }
        finally
        {
            ui.Stop();
        }
    }

    [Fact]
    public void Invoke_OnTheDispatcherThread_RunsInline()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var sut = new WpfUiDispatcher(dispatcher);
        var ran = false;

        sut.Invoke(() => ran = true);

        Assert.True(ran);
    }

    [Fact]
    public void Invoke_WithNoDispatcher_RunsInline()
    {
        // No dispatcher means run it here, not drop it (F-100). Staged through the resolver seam:
        // Application.Current is process-global and other tests in this assembly create one, so the
        // parameterless constructor cannot be relied on to find none. (The first version of this
        // test used it and failed in a full run, passing alone: 2026-11-16.)
        var sut = new WpfUiDispatcher(static () => null);
        var ranOn = -1;

        sut.Invoke(() => ranOn = Environment.CurrentManagedThreadId);

        Assert.Equal(Environment.CurrentManagedThreadId, ranOn);
    }
}
