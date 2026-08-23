using System.Windows.Threading;

namespace ROROROblox.App;

/// <summary>
/// Constructs a UI-affine object on the dispatcher's thread, wherever the resolve came from.
///
/// <para>F-122. <c>TrayService</c>'s constructor builds <c>TaskbarIcon</c>, <c>ContextMenu</c>, and
/// <c>MenuItem</c> — WPF objects with thread affinity — and it is a DI singleton, so whichever
/// caller resolves it first constructs it on whatever thread that caller is on. On 2026-08-20 the
/// first caller was <c>StartPluginHostListener</c>'s threadpool continuation resolving
/// <c>AlertDispatcher</c> (whose factory takes <c>ITrayService</c>), and startup died on "Cannot
/// access Freezable 'SolidColorBrush' across threads". Reordering the resolves would fix that one
/// race and leave the next background caller to rediscover it; binding construction to the
/// dispatcher in the factory removes the class of bug.</para>
///
/// <para>A null dispatcher constructs inline — headless tests have no Application, and refusing to
/// construct there would trade a threading crash for a test-only one.</para>
/// </summary>
internal static class UiBoundFactory
{
    internal static T Create<T>(Dispatcher? dispatcher, Func<T> construct)
    {
        ArgumentNullException.ThrowIfNull(construct);

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return construct();
        }

        // Invoke, not InvokeAsync: DI resolution is synchronous, and construction is cheap — the
        // cost of blocking a background resolver briefly is the price of the object being usable.
        return dispatcher.Invoke(construct);
    }
}
