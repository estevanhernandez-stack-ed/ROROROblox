using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Real-Win32-mutex coverage of <see cref="MutexHolder"/>. Each test uses a unique mutex name
/// so concurrent test runs don't collide. The MutexLost watchdog isn't directly tested here —
/// reliably triggering an invalid-handle case requires admin or external handle manipulation;
/// the watchdog code is structurally exercised by Acquire/Release lifetimes.
/// </summary>
public class MutexHolderTests
{
    private static string UniqueName() => $@"Local\ROROROblox-test-{Guid.NewGuid():N}";

    [Fact]
    public void Acquire_ReturnsTrue_AndIsHeld()
    {
        using var holder = new MutexHolder(UniqueName());

        Assert.True(holder.Acquire());
        Assert.True(holder.IsHeld);
    }

    [Fact]
    public void Acquire_IsIdempotent_WhenAlreadyHeld()
    {
        using var holder = new MutexHolder(UniqueName());

        Assert.True(holder.Acquire());
        Assert.True(holder.Acquire());
        Assert.True(holder.IsHeld);
    }

    [Fact]
    public void Acquire_ReturnsFalse_WhenAnotherInstanceHoldsTheMutex()
    {
        var name = UniqueName();
        using var first = new MutexHolder(name);
        using var second = new MutexHolder(name);

        Assert.True(first.Acquire());
        Assert.False(second.Acquire());
        Assert.True(first.IsHeld);
        Assert.False(second.IsHeld);
    }

    [Fact]
    public void Release_ReturnsToUnheld()
    {
        using var holder = new MutexHolder(UniqueName());
        holder.Acquire();

        holder.Release();

        Assert.False(holder.IsHeld);
    }

    [Fact]
    public void Release_IsNoOp_WhenNotHeld()
    {
        using var holder = new MutexHolder(UniqueName());

        holder.Release();

        Assert.False(holder.IsHeld);
    }

    [Fact]
    public void Acquire_AfterRelease_Succeeds()
    {
        using var holder = new MutexHolder(UniqueName());

        Assert.True(holder.Acquire());
        holder.Release();
        Assert.True(holder.Acquire());
    }

    [Fact]
    public void TwoHolders_HandOffOwnershipCorrectly()
    {
        var name = UniqueName();
        var first = new MutexHolder(name);
        first.Acquire();

        using var second = new MutexHolder(name);
        Assert.False(second.Acquire());

        first.Dispose();
        Assert.True(second.Acquire());
    }

    [Fact]
    public void Dispose_ReleasesAndPreventsFurtherUse()
    {
        var holder = new MutexHolder(UniqueName());
        holder.Acquire();

        holder.Dispose();

        Assert.False(holder.IsHeld);
        Assert.Throws<ObjectDisposedException>(() => holder.Acquire());
    }

    [Fact]
    public void Constructor_RejectsEmptyOrWhitespaceName()
    {
        Assert.Throws<ArgumentException>(() => new MutexHolder(""));
        Assert.Throws<ArgumentException>(() => new MutexHolder("   "));
    }

    // ---- IsValidName: the pre-ctor validator (config-driven mutex name, spec item #1) -------
    // Strict SUPERSET of the ctor's throw conditions: any name IsValidName approves must
    // construct without throwing, so a garbage roblox-compat.json value can never reach the
    // throwing ctor — it is rejected and the resolver falls back to DefaultMutexName.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("has\0embedded-null")]
    public void IsValidName_False_ForUnsafeName(string? candidate)
    {
        Assert.False(MutexHolder.IsValidName(candidate));
    }

    [Fact]
    public void IsValidName_False_ForOverlongName()
    {
        var tooLong = @"Local\" + new string('a', 300);

        Assert.False(MutexHolder.IsValidName(tooLong));
    }

    [Fact]
    public void IsValidName_True_ForDefaultName()
    {
        Assert.True(MutexHolder.IsValidName(MutexHolder.DefaultMutexName));
    }

    [Fact]
    public void IsValidName_True_ForPlausibleRenamedName()
    {
        Assert.True(MutexHolder.IsValidName(@"Local\ROBLOX_singletonEvent_v2"));
    }

    // ---- MutexName: the resolved-name forward-hook for the Handle64 plugin host -------------

    [Fact]
    public void MutexName_ExposesConfiguredName_OnTheInterface()
    {
        // The future add-to-already-running plugin reads the resolved name off IMutexHolder so it
        // closes the SAME singleton handle the app holds.
        var name = UniqueName();
        using var holder = new MutexHolder(name);
        IMutexHolder asInterface = holder;

        Assert.Equal(name, asInterface.MutexName);
    }

    [Fact]
    public void MutexName_DefaultsToDefaultMutexName_WithParameterlessCtor()
    {
        using var holder = new MutexHolder();

        Assert.Equal(MutexHolder.DefaultMutexName, holder.MutexName);
    }
}
