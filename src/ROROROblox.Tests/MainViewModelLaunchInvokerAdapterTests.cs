using ROROROblox.App.Plugins.Adapters;

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
}
