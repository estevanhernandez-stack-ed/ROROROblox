using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <remarks>
/// Review round 1, finding 2: every <c>finally</c> block's temp-file cleanup below is best-effort
/// (<c>try</c>/<c>catch (IOException)</c>), not because the delete itself is under test, but
/// because real-time AV/EDR scanning was confirmed (on the machine that reproduced this) to
/// transiently hold this exact file open under full-suite load — a cleanup delete racing that
/// hold must not mask what the test above it already proved. The OS reclaims %TEMP% regardless.
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
}
