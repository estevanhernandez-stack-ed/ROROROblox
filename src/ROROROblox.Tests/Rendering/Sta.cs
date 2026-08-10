using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Runs a delegate on a fresh STA thread and marshals its result — or its exception — back.
/// <para>
/// WPF visuals require STA, and xUnit runs facts on MTA thread-pool threads. <c>Xunit.StaFact</c>
/// would supply this, and is deliberately not used: this repo ships to the Microsoft Store with an
/// auth-cookie threat model and stays dependency-lean, so twenty lines beats a supply-chain entry
/// (rendered-contrast-gate design, "The harness").
/// </para>
/// <para>
/// A FRESH thread per call, not a shared one. WPF caches per-thread state — the Dispatcher, and any
/// <c>ResourceDictionary</c> a visual has been attached to — so reusing a thread across themes would
/// let one theme's resolution leak into the next, and the leak would look like a passing test rather
/// than a failing one. Thread startup is microseconds against a render; correctness is worth more
/// than the reuse.
/// </para>
/// </summary>
internal static class Sta
{
    /// <summary>
    /// Guards against a render that wedges. A hung STA thread with no timeout takes the whole test
    /// run down with no output naming which case did it; the design flagged flake risk as real, and
    /// "which one hung" is the first question when it fires.
    /// </summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    public static T Run<T>(Func<T> work, string what)
    {
        T result = default!;
        ExceptionDispatchInfo? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                // Capture rather than rethrow: an exception escaping a background thread's delegate
                // tears down the process instead of failing the fact.
                captured = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                // The dispatcher owns this thread once any DispatcherObject is touched. Without an
                // explicit shutdown the thread stays alive holding its visual tree, and enough of
                // those across a theory matrix is a real leak.
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        })
        {
            IsBackground = true,
            Name = $"sta-render:{what}",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(Budget))
        {
            throw new TimeoutException(
                $"STA render for '{what}' did not finish within {Budget.TotalSeconds:F0}s. "
                + "The thread is abandoned rather than aborted (Thread.Abort throws on .NET Core), "
                + "so the run continues but this case measured nothing.");
        }

        captured?.Throw();
        return result;
    }

    /// <summary>
    /// Drains the dispatcher queue down to <see cref="DispatcherPriority.Loaded"/>, synchronously.
    /// <para>
    /// Needed because WPF defers work this gate depends on: <c>DynamicResource</c> invalidation and
    /// template application are queued, not immediate. Measure/Arrange alone can render a control
    /// whose triggers have not yet applied — which would sample the default setter and report a
    /// confident wrong colour, the exact failure mode a pixel gate exists to rule out.
    /// </para>
    /// <para>
    /// <c>PushFrame</c> rather than a message loop: there is no window and nothing to pump beyond
    /// the queue itself, and a frame terminates deterministically instead of on a timer.
    /// </para>
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
