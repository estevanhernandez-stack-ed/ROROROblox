using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <remarks>
/// Every <c>finally</c> block's temp-file cleanup below is best-effort
/// (<c>try</c>/<c>catch (IOException)</c>), matching the established convention already used
/// throughout this test project for scratch temp-file/dir cleanup (e.g. <c>BannerRecipeTests</c>,
/// <c>ContrastPairGateTests</c>, <c>ExpiredRowRedundancyTests</c>, <c>AppLoggingVersionTests</c>).
/// A scratch file's cleanup failing should never mask what the test above it already proved, and
/// the OS reclaims %TEMP% regardless.
/// <para>
/// <b>Fix round 2 correction.</b> Review round 1 attributed the full-suite-only flake on
/// <c>RequestLaunch_FlaggedAltWithMainInGame_FollowsTheMain</c> to real-time AV/EDR scanning
/// transiently holding the account-store file open. <b>That theory was wrong.</b> The actual cause:
/// <c>RequestLaunchAsync</c> marshaled its Follow/Direct dispatch through
/// <c>Application.Current?.Dispatcher.InvokeAsync(...)</c> — fire-and-forget, returning before the
/// queued delegate had even started — so under full-suite load, when another test's real
/// <c>Application</c> made <c>Application.Current</c> non-null and owned by a different thread,
/// this method could hand back <c>ok: true</c> before <c>LaunchAccountAsync</c> had set
/// <c>IsLaunching = true</c>. <c>FlaggedLaunchTests.UntilSettledAsync</c> then observed
/// <c>IsLaunching == false</c> immediately and returned, believing the launch had already settled;
/// the test's own <c>finally</c> deleted the temp <c>.dat</c> file; and the actually-still-queued
/// launch then ran and hit <c>KeyNotFoundException</c> in <c>AccountStore.RetrieveCookieAsync</c>
/// against the now-deleted file — the exact symptom the open finding described. Fixed by marshaling
/// through <c>MainViewModel.UiDispatcher</c> (a blocking <c>Invoke</c>) instead; see
/// <c>MainViewModelLaunchInvokerAdapter.RequestLaunchAsync</c>. The AV/EDR-driven
/// <c>IOException</c> retry theory and its two escalating <c>AccountStore.LoadAsync</c> retry
/// attempts were reverted along with it — this cleanup catch is kept solely because it already
/// matches this project's general best-effort-cleanup convention, not because of that theory.
/// </para>
/// </remarks>
public class MainViewModelLaunchInvokerAdapterTests
{
    [Fact]
    public void ValidateLaunchTargetArgs_RejectsMissingAccountId()
    {
        var (ok, reason) = MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs("", "url", null);
        Assert.False(ok);
        Assert.Contains("account", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLaunchTargetArgs_RejectsNonGuidAccountId()
    {
        var (ok, reason) = MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs("not-a-guid", "url", null);
        Assert.False(ok);
        Assert.Contains("GUID", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLaunchTargetArgs_RejectsNoTarget()
    {
        var (ok, reason) = MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs(Guid.NewGuid().ToString(), null, null);
        Assert.False(ok);
        Assert.Contains("target", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLaunchTargetArgs_AcceptsShareUrl()
    {
        var (ok, _) = MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs(Guid.NewGuid().ToString(), "https://x", null);
        Assert.True(ok);
    }

    [Fact]
    public void ValidateLaunchTargetArgs_AcceptsFollowUserId()
    {
        var (ok, _) = MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs(Guid.NewGuid().ToString(), null, 12345L);
        Assert.True(ok);
    }

    [Fact]
    public async Task RequestLaunch_FlaggedAltWithMainOffline_RefusesWithReasonCode()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: false);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("plugins never ask");
            var adapter = new MainViewModelLaunchInvokerAdapter(vm);
            var (ok, _, _, code) = await adapter.RequestLaunchAsync(alt.Id.ToString());
            Assert.False(ok);
            Assert.Equal(PluginLaunchReasonCodes.FollowTargetNotJoinable, code);
            Assert.Empty(launcher.Launches);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task RequestLaunch_FlaggedAltWithMainInGame_FollowsTheMain()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: true);
            var (ok, _, _, _) = await new MainViewModelLaunchInvokerAdapter(vm).RequestLaunchAsync(alt.Id.ToString());
            await FlaggedLaunchTests.UntilSettledAsync(alt);
            Assert.True(ok);
            Assert.Equal(new LaunchTarget.FollowFriend(111), Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    /// <summary>
    /// Fix round 2. The full-suite flake on <see cref="RequestLaunch_FlaggedAltWithMainInGame_FollowsTheMain"/>
    /// traced to <c>RequestLaunchAsync</c> marshaling through <c>Application.Current?.Dispatcher</c>
    /// directly: when another test's real <c>App</c> owns a non-null <c>Application.Current</c> on a
    /// different thread, <c>CheckAccess()</c> is false and the old code fell into
    /// <c>dispatcher.InvokeAsync(...)</c> — fire-and-forget, returning before the queued delegate even
    /// started. That let <c>RequestLaunchAsync</c> hand back <c>ok: true</c> before
    /// <c>LaunchAccountAsync</c>'s first line (<c>summary.IsLaunching = true</c>) had run, so the
    /// caller's next read of <c>IsLaunching</c> raced the launch's own start.
    /// <para>
    /// Reproducing the exact trigger (a real, differently-threaded, non-null <c>Application.Current</c>)
    /// deterministically in an isolated unit test would mean standing up a second STA <c>App</c> here —
    /// heavy, and it would reintroduce the same cross-test global-state hazard that caused the flake in
    /// the first place. Instead this pins the actual code-level contract the fix establishes: dispatch
    /// goes through <see cref="MainViewModel.UiDispatcher"/> (recorded via a fake here), never through
    /// <c>Application.Current</c> directly. The old adapter code never touched the injected dispatcher
    /// at all for this path, so this fake is never invoked pre-fix — a genuine, deterministic RED — and
    /// because the fake's <c>Invoke</c> runs the action inline before returning, once the adapter routes
    /// through it, <c>IsLaunching</c> is provably true by the time <c>RequestLaunchAsync</c> returns, in
    /// every environment, with no dependence on Application.Current at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RequestLaunch_FlaggedAltWithMainInGame_DispatchesThroughTheInjectedUiDispatcherBeforeReturning()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var dispatcher = new RecordingInlineUiDispatcher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher, uiDispatcher: dispatcher);
        try
        {
            var (_, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: true);
            var (ok, _, _, _) = await new MainViewModelLaunchInvokerAdapter(vm).RequestLaunchAsync(alt.Id.ToString());
            Assert.True(ok);
            Assert.True(dispatcher.Invoked, "RequestLaunchAsync must marshal the launch through MainViewModel.UiDispatcher, not Application.Current directly.");
            Assert.True(alt.IsLaunching, "the launch must have started, synchronously, before RequestLaunchAsync returns.");
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    /// <summary>Runs the marshaled action inline, synchronously, like <c>WpfUiDispatcher</c> does
    /// when there is no real dispatcher — and records that it was actually called, which
    /// <c>Application.Current?.Dispatcher</c> is not, from this adapter, after the fix.</summary>
    private sealed class RecordingInlineUiDispatcher : Core.IUiDispatcher
    {
        public bool Invoked { get; private set; }

        public void Invoke(Action action)
        {
            Invoked = true;
            action();
        }
    }

    [Fact]
    public async Task RequestLaunchTarget_FollowOfASavedAccountNotInGame_Refuses()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: false);
            alt.JoinViaFriend = false;
            var (ok, _, _, code) = await new MainViewModelLaunchInvokerAdapter(vm)
                .RequestLaunchTargetAsync(alt.Id.ToString(), null, followUserId: 111);
            Assert.False(ok);
            Assert.Equal(PluginLaunchReasonCodes.FollowTargetNotJoinable, code);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    /// <summary>
    /// Review round 1, finding 1: before the fix, a Direct decision routed through
    /// <c>LaunchAccountCommand</c> -&gt; <c>LaunchAccountAsync</c> in Ask mode, which re-decides
    /// the flagged-launch outcome from scratch when it actually runs. Since the adapter's own
    /// decision and that re-decision both read the SAME live <see cref="AccountSummary"/>
    /// synchronously with nothing awaited in between, there is no deterministic seam available to
    /// flip <c>JoinViaFriend</c> or the main's presence between the two decisions without adding a
    /// test-only hook to production code — so this test takes the reviewer's offered fallback:
    /// assert that a Direct-outcome launch, with <see cref="MainViewModel.FlaggedLaunchPrompt"/>
    /// wired to throw, still completes and reaches the real launcher. Since Refuse mode never
    /// calls <c>FlaggedLaunchPrompt</c> for ANY re-decided outcome (Direct, Follow, or a refusal —
    /// see <c>LaunchAccountAsync</c>'s <c>default:</c> switch arm), this pins the routing fix in
    /// place structurally: if a future change ever routed Direct back through the Ask-mode
    /// command, this test would hang or throw instead of completing.
    /// </summary>
    [Fact]
    public async Task RequestLaunch_DirectOutcome_RoutesThroughRefuseMode_NeverAsks()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: false);
            alt.JoinViaFriend = false; // Direct outcome regardless of the main's presence
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("plugins never ask");
            var (ok, _, _, _) = await new MainViewModelLaunchInvokerAdapter(vm).RequestLaunchAsync(alt.Id.ToString());
            await FlaggedLaunchTests.UntilSettledAsync(alt);
            Assert.True(ok);
            Assert.IsType<LaunchTarget.DefaultGame>(Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    /// <summary>Runs every marshalled action inline and records how deeply Invoke calls nest.</summary>
    private sealed class DepthRecordingUi : IUiDispatcher
    {
        private int _depth;
        public int MaxDepth { get; private set; }
        public void Invoke(Action action)
        {
            _depth++;
            MaxDepth = Math.Max(MaxDepth, _depth);
            try { action(); } finally { _depth--; }
        }
    }

    // M6: RequestLaunchTargetAsync marshals through the view model's own IUiDispatcher, like
    // RequestLaunchAsync, not Application.Current's Dispatcher. The launch's own marshal (it
    // settles relaunch-pending through the same dispatcher) then runs nested inside the outer one,
    // and the call still awaits the launch.
    [Fact]
    public async Task RequestLaunchTarget_MarshalsThroughTheViewModelsDispatcher_AndAwaitsTheLaunch()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var ui = new DepthRecordingUi();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher, uiDispatcher: ui);
        try
        {
            var (_, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: false);
            alt.JoinViaFriend = false;

            var (ok, _, _, _) = await new MainViewModelLaunchInvokerAdapter(vm)
                .RequestLaunchTargetAsync(alt.Id.ToString(), null, followUserId: 999)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(ok);
            Assert.Equal(new LaunchTarget.FollowFriend(999), Assert.Single(launcher.Launches)); // awaited, not queued
            Assert.True(ui.MaxDepth >= 2, $"the launch did not start inside the view model's dispatcher (max depth {ui.MaxDepth})");
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }
}
