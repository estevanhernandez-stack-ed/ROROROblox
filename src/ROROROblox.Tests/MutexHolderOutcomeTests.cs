using System.Threading;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The singleton "mutex" is a name race between two different kernel object types, and the two
/// ways of losing it mean opposite things.
///
/// <para>Roblox creates an <b>Event</b> named <c>ROBLOX_singletonEvent</c>. RoRoRo creates a
/// <b>Mutex</b> of the same name. Whoever wins, the loser's create call fails — but with different
/// error codes: <c>ERROR_INVALID_HANDLE</c> when the name is taken by the other type (Roblox), and
/// <c>ERROR_ALREADY_EXISTS</c> when it's taken by a Mutex (another RoRoRo or a compatible tool).</para>
///
/// <para>Every pre-existing test in this suite held the name with another <see cref="MutexHolder"/>,
/// i.e. the compatible-tool case. Nothing ever exercised the case that actually happens in
/// production, which is why <c>IsHeldElsewhere()</c> could be blind to Roblox for so long.</para>
/// </summary>
public class MutexHolderOutcomeTests
{
    private static string UniqueName() => $@"Local\rororo-test-{Guid.NewGuid():N}";

    [Fact]
    public void TryAcquire_NobodyHoldsTheName_Acquired()
    {
        using var holder = new MutexHolder(UniqueName());
        Assert.Equal(MutexAcquireOutcome.Acquired, holder.TryAcquire());
    }

    [Fact]
    public void TryAcquire_NameHeldAsEvent_ReportsHeldByRoblox()
    {
        // Exactly what Roblox does: an Event under the singleton name.
        var name = UniqueName();
        using var robloxsEvent = new EventWaitHandle(false, EventResetMode.ManualReset, name);

        using var holder = new MutexHolder(name);

        Assert.Equal(MutexAcquireOutcome.HeldByRoblox, holder.TryAcquire());
        Assert.False(holder.IsHeld);
    }

    [Fact]
    public void TryAcquire_NameHeldAsMutex_ReportsHeldByCompatibleTool()
    {
        // What another RoRoRo (or a compatible multi-instance tool) does: a Mutex of the same name.
        var name = UniqueName();
        using var peer = new MutexHolder(name);
        Assert.Equal(MutexAcquireOutcome.Acquired, peer.TryAcquire());

        using var holder = new MutexHolder(name);

        Assert.Equal(MutexAcquireOutcome.HeldByCompatibleTool, holder.TryAcquire());
        Assert.False(holder.IsHeld);
    }

    [Fact]
    public void Acquire_KeepsItsBooleanContract()
    {
        var name = UniqueName();
        using var robloxsEvent = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        using var holder = new MutexHolder(name);

        Assert.False(holder.Acquire()); // HeldByRoblox is still "didn't get it"
    }

    // =====================================================================
    // IsHeldElsewhere had to open the name as a Mutex, so Roblox's Event was
    // invisible to it and MutexContestedWatcher never raised the banner in the
    // one case it exists for.
    // =====================================================================

    [Fact]
    public void IsHeldElsewhere_NameHeldAsEvent_ReturnsTrue()
    {
        var name = UniqueName();
        using var robloxsEvent = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        using var holder = new MutexHolder(name);

        Assert.True(holder.IsHeldElsewhere(), "an Event holding the name is Roblox — that IS contention");
    }

    [Fact]
    public void IsHeldElsewhere_NameHeldAsMutex_ReturnsTrue()
    {
        var name = UniqueName();
        using var peer = new MutexHolder(name);
        peer.Acquire();
        using var holder = new MutexHolder(name);

        Assert.True(holder.IsHeldElsewhere());
    }

    [Fact]
    public void IsHeldElsewhere_NobodyHolds_ReturnsFalse()
    {
        using var holder = new MutexHolder(UniqueName());
        Assert.False(holder.IsHeldElsewhere());
    }

    // =====================================================================
    // Retry polls. A single instantaneous attempt is why "quit Roblox, then hit
    // Retry" needed a second press: the kernel object outlives the process by the
    // moment it takes the last handle to close.
    // =====================================================================

    [Fact]
    public async Task TryAcquireWithRetryAsync_WinsOnceTheBlockerReleases()
    {
        var name = UniqueName();
        var robloxsEvent = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        using var concrete = new MutexHolder(name);
        IMutexHolder holder = concrete; // TryAcquireWithRetryAsync is a default interface method

        Assert.Equal(MutexAcquireOutcome.HeldByRoblox, holder.TryAcquire()); // blocked right now

        // Release the name shortly after the poll begins, the way a dying Roblox process does.
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            robloxsEvent.Dispose();
        });

        var outcome = await holder.TryAcquireWithRetryAsync(
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50));

        Assert.Equal(MutexAcquireOutcome.Acquired, outcome);
        Assert.True(holder.IsHeld);
    }

    [Fact]
    public async Task TryAcquireWithRetryAsync_GivesUpWhenTheBlockerStays()
    {
        var name = UniqueName();
        using var robloxsEvent = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        using var concrete = new MutexHolder(name);
        IMutexHolder holder = concrete;

        var outcome = await holder.TryAcquireWithRetryAsync(
            TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50));

        Assert.Equal(MutexAcquireOutcome.HeldByRoblox, outcome);
        Assert.False(holder.IsHeld);
    }

    [Fact]
    public async Task TryAcquireWithRetryAsync_DoesNotWaitOutACompatibleTool()
    {
        // A peer tool holding the name is a stable state, not a race — returning immediately keeps
        // the modal from stalling for the full window on a state that is already fine.
        var name = UniqueName();
        using var peer = new MutexHolder(name);
        peer.Acquire();
        using var concrete = new MutexHolder(name);
        IMutexHolder holder = concrete;

        var started = DateTime.UtcNow;
        var outcome = await holder.TryAcquireWithRetryAsync(
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100));
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(MutexAcquireOutcome.HeldByCompatibleTool, outcome);
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"returned in {elapsed.TotalMilliseconds:N0}ms; should not poll");
    }
}
