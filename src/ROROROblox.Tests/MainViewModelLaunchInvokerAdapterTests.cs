using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

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
        finally { if (File.Exists(path)) File.Delete(path); }
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
        finally { if (File.Exists(path)) File.Delete(path); }
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
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
