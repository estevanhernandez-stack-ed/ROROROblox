using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class MutexHolderIsHeldElsewhereTests
{
    private static string UniqueName() => $@"Local\rororo-test-{System.Guid.NewGuid():N}";

    [Fact]
    public void IsHeldElsewhere_NobodyHolds_ReturnsFalse()
    {
        using var holder = new MutexHolder(UniqueName());
        Assert.False(holder.IsHeldElsewhere());
    }

    [Fact]
    public void IsHeldElsewhere_WeHoldIt_ReturnsFalse()
    {
        using var holder = new MutexHolder(UniqueName());
        Assert.True(holder.Acquire());
        Assert.False(holder.IsHeldElsewhere()); // held by us is NOT "elsewhere"
    }

    [Fact]
    public void IsHeldElsewhere_AnotherHolderHasIt_ReturnsTrue()
    {
        var name = UniqueName();
        using var owner = new MutexHolder(name);
        Assert.True(owner.Acquire());

        using var observer = new MutexHolder(name);
        Assert.True(observer.IsHeldElsewhere()); // owner holds it, observer sees it
    }

    [Fact]
    public void IsHeldElsewhere_AfterOwnerReleases_ReturnsFalse()
    {
        var name = UniqueName();
        using var owner = new MutexHolder(name);
        using var observer = new MutexHolder(name);
        Assert.True(owner.Acquire());
        Assert.True(observer.IsHeldElsewhere());

        owner.Release();
        Assert.False(observer.IsHeldElsewhere());
    }
}
