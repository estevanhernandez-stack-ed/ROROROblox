using System;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Pins the <see cref="MemoryWatchdog.GetSnapshot"/> pre-first-sample contract: callers must
/// never see a null <see cref="MemoryPressureSnapshot.Accounts"/>. This is the regression guard
/// for a bug Task 7 discovered live — <c>MainViewModel</c> is the first real caller of
/// <c>GetSnapshot()</c> that can run before any <see cref="MemoryWatchdog.Sample"/> completes
/// (its own 30s UI ticker starts independently of the watchdog's 30s sample timer), and a bare
/// <c>default(MemoryPressureSnapshot)</c> would have handed it a null list. Fixed at the type's
/// field default rather than patched at each call site, so every future consumer (System Health
/// reporting, etc.) inherits the guarantee for free.
/// </summary>
public class MemoryWatchdogSnapshotTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeProcessMemory : IProcessMemoryProbe
    {
        public bool TryReadPrivateBytes(int pid, out long privateBytes)
        {
            privateBytes = 0;
            return false;
        }
    }

    private sealed class FakeSystemMemory : ISystemMemoryProbe
    {
        public bool TryRead(out long total, out long available)
        {
            total = 0;
            available = 0;
            return false;
        }
    }

    [Fact]
    public void GetSnapshot_BeforeAnySample_ReturnsNonNullEmptyAccounts()
    {
        // Fails (NullReferenceException on Assert.Empty, or an outright null) if the `_last`
        // field initializer in MemoryWatchdog is ever simplified back to
        // `private MemoryPressureSnapshot _last;` — the exact regression this test exists to
        // catch. Deliberately does NOT call Start() or Sample() first.
        var wd = new MemoryWatchdog(new FakeProcessMemory(), new FakeSystemMemory(), new FakeClock());

        var snapshot = wd.GetSnapshot();

        Assert.NotNull(snapshot.Accounts);
        Assert.Empty(snapshot.Accounts);
        Assert.False(snapshot.HasProjection);
    }
}
