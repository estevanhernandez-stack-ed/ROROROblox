namespace ROROROblox.Core;

/// <summary>
/// Marshals an action onto the UI thread.
/// <para>
/// WHY THIS EXISTS (F-100). The view model reached straight for
/// <c>Application.Current?.Dispatcher.Invoke(...)</c>, and <c>Application.Current</c> is null across
/// the whole ordinary test suite — <c>ThemedRender</c>'s header states that as a deliberate
/// invariant. So every one of those calls silently no-opped and the delegate inside never ran. The
/// bodies were presence application, session expiry, process attach/exit and memory-pressure
/// alerts: the marshalled UI updates most likely to be wrong, and the ones no test had executed.
/// A null-conditional on process-global state is an off switch the tests are always holding down.
/// </para>
/// <para>
/// Lives in Core, with no UI dependency, because that is all a marshal is: run this later, over
/// there. The WPF half is <c>WpfUiDispatcher</c> in the app.
/// </para>
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, blocking until it completes.
    /// <para>
    /// Implementations that have no dispatcher must still decide what to do rather than swallow the
    /// call — see <c>WpfUiDispatcher</c> for the shipped answer and why it is the one it is.
    /// </para>
    /// </summary>
    void Invoke(Action action);
}
