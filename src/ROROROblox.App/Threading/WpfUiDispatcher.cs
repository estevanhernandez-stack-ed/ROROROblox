using System;
using System.Windows;
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
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
