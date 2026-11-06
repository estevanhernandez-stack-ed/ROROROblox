namespace ROROROblox.Tests.Rendering;

/// <summary>
/// One process-wide lock that every test-side XAML parse runs under: <see cref="Sta.Run{T}"/>'s
/// fresh threads and <see cref="WindowRenderHost"/>'s long-lived one, including the host's
/// <c>App.InitializeComponent()</c>.
/// <para>
/// WHY. WPF's BAML reader shares one static schema context across every thread in the process
/// (<c>WpfSharedBamlSchemaContext</c>), and its known-type lookup enumerates a <c>List</c> that a
/// first-time lookup on another thread can grow. Two STA threads parsing at once can therefore throw
/// <c>InvalidOperationException: Collection was modified</c>, wrapped in a
/// <c>XamlParseException</c>. Measured, not theorised: the v1.36.0.0 tag's <c>release.yml</c> run
/// (2026-11-05) crashed the TEST HOST 131 tests in, with that exception thrown from
/// <c>App.InitializeComponent</c> on <c>sta-window-render-host</c> while a <c>Sta</c> render parsed
/// on another thread. <c>ci.yml</c> had passed the same commit twice on both architectures, which is
/// the signature of a race and not of a defect in any one test.
/// </para>
/// <para>
/// WHY THE HOST DIES RATHER THAN THE TEST FAILS. On the host thread the throw happens before the
/// dispatcher loop, outside any test's call stack, so nothing catches it and the process goes down.
/// The whole run aborts, wherever it happened to be.
/// </para>
/// <para>
/// THE COST is that renders no longer overlap across xUnit collections. Renders were already
/// serialized on the host thread, and a <c>Sta</c> render is milliseconds, so the suite's wall clock
/// barely moves. Re-entrancy is safe: a host render that pumps its own dispatcher
/// (<c>DrainQueue</c>) may run a queued render on the same thread, and <c>lock</c> is re-entrant
/// per thread. No render starts another render on a different thread, which is the one shape that
/// would deadlock here; the 60 s budgets in both helpers would name it if one ever did.
/// </para>
/// </summary>
internal static class XamlParseGate
{
    public static readonly object Sync = new();
}
