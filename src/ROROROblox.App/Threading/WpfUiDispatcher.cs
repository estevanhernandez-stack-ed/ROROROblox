using System;
using System.Windows;
using System.Windows.Threading;
using ROROROblox.Core;

namespace ROROROblox.App.Threading;

/// <summary>
/// The shipped <see cref="IUiDispatcher"/>: WPF's own dispatcher, reached through
/// <see cref="Application.Current"/>.
/// </summary>
/// <remarks>
/// <para>
/// NO DISPATCHER MEANS RUN IT HERE, not drop it. That is the whole correction F-100 asked for. The
/// old inline calls were <c>Application.Current?.Dispatcher.Invoke(...)</c>, and the
/// <c>?.</c> turned "there is no UI thread" into "do nothing at all" — which is why five delegate
/// bodies in <c>MainViewModel</c> had never once executed under test.
/// </para>
/// <para>
/// This is not a new convention. <c>MainViewModel.OnGamesChanged</c> already did exactly this,
/// explicitly: <c>if (dispatcher is null || dispatcher.CheckAccess()) call directly</c>. One site
/// got it right and the rest never adopted it; this class makes that site's rule the default
/// everywhere.
/// </para>
/// <para>
/// <see cref="System.Windows.Threading.Dispatcher.CheckAccess"/> matters for more than speed: WPF's
/// <c>Invoke</c> from the UI thread re-enters the dispatcher, and these handlers can fire from
/// inside a render. Calling straight through when we are already on the thread avoids that.
/// </para>
/// </remarks>
internal sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Func<Dispatcher?> _resolve;

    /// <summary>The shipped behaviour: resolve <see cref="Application.Current"/>'s dispatcher at call time.</summary>
    public WpfUiDispatcher() : this(static () => Application.Current?.Dispatcher) { }

    /// <summary>Test seam: drive a specific dispatcher instead of <see cref="Application.Current"/>'s.</summary>
    internal WpfUiDispatcher(Dispatcher dispatcher) : this(FixedTo(dispatcher)) { }

    /// <summary>
    /// Test seam for the "no dispatcher" case. <see cref="Application.Current"/> is process-global and
    /// other tests create one, so that case cannot be staged through the parameterless constructor.
    /// </summary>
    internal WpfUiDispatcher(Func<Dispatcher?> resolve)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    private static Func<Dispatcher?> FixedTo(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return () => dispatcher;
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = _resolve();
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // SHUTDOWN IS NOT AN ERROR. A client that exits while RoRoRo is exiting reaches here from a
        // thread-pool Process.Exited callback. Once the dispatcher is going down there is no UI left
        // to update, so drop the call; and a call already queued when shutdown begins is aborted by
        // WPF, which surfaces on THIS thread as a TaskCanceledException that nothing above catches
        // (it killed the process and made a clean quit read as a crash).
        if (IsShuttingDown(dispatcher))
        {
            return;
        }

        try
        {
            dispatcher.Invoke(action);
        }
        catch (OperationCanceledException) when (IsShuttingDown(dispatcher))
        {
            // The abort above. The FILTER is what tells it from the action's own cancellation:
            // WPF reports both to this caller as a TaskCanceledException (measured), so the type
            // cannot separate them; dispatcher state can. A healthy dispatcher means the
            // cancellation is not shutdown's, and it propagates. The one overlap, an action's own
            // cancellation racing shutdown, loses nothing: shutdown has already discarded the
            // work it would have reported on. (The inline path above never enters this try.)
        }
    }

    private static bool IsShuttingDown(Dispatcher dispatcher) =>
        dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished;
}
