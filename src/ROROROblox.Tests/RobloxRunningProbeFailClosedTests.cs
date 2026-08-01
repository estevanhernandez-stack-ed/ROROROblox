using System;
using System.ComponentModel;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// <see cref="RobloxRunningProbe.ReadHasWindow"/> must fail CLOSED.
///
/// <para>The windowed/windowless flag is not cosmetic — it is the whole safety gate for
/// <see cref="SeamlessTakeover.WindowlessOnly"/>, which closes every Roblox client it is handed
/// <b>with no confirming modal</b>. So a process we could not classify must be treated as windowed
/// (possibly mid-game, possibly about to lose progress), never as a harmless tray orphan.</para>
///
/// <para>The original code did the opposite: <c>catch { hasWindow = false; }</c>, with a comment
/// naming "access denied" as an expected case. That converts any transient read failure into
/// silently killing the user's live game windows.</para>
/// </summary>
public class RobloxRunningProbeFailClosedTests
{
    [Fact]
    public void ReadHasWindow_HandleReadThrows_TreatsProcessAsWindowed()
    {
        var hasWindow = RobloxRunningProbe.ReadHasWindow(
            () => throw new InvalidOperationException("process has exited"));

        Assert.True(hasWindow);
    }

    // The three exception types Process.MainWindowHandle actually raises in the wild. Access
    // denied (Win32Exception) is the one the original comment named — and the one that made the
    // inverted default dangerous rather than merely wrong.
    [Fact]
    public void ReadHasWindow_AccessDenied_TreatsProcessAsWindowed()
    {
        var hasWindow = RobloxRunningProbe.ReadHasWindow(
            () => throw new Win32Exception(5)); // ERROR_ACCESS_DENIED

        Assert.True(hasWindow);
    }

    [Fact]
    public void ReadHasWindow_NotSupported_TreatsProcessAsWindowed()
    {
        var hasWindow = RobloxRunningProbe.ReadHasWindow(
            () => throw new NotSupportedException("remote process"));

        Assert.True(hasWindow);
    }

    // Guard against over-correcting. If ReadHasWindow simply returned true always, every test
    // above would still pass while seamless takeover silently stopped working forever — RoRoRo
    // would show the BLOCKED modal on every launch alongside a tray-resident Roblox.
    [Fact]
    public void ReadHasWindow_ZeroHandle_ReportsWindowless()
        => Assert.False(RobloxRunningProbe.ReadHasWindow(() => IntPtr.Zero));

    [Fact]
    public void ReadHasWindow_NonZeroHandle_ReportsWindowed()
        => Assert.True(RobloxRunningProbe.ReadHasWindow(() => new IntPtr(0x1234)));

    // End-to-end safety property, stated as a test: the thing we actually care about is not the
    // boolean, it is that an unclassifiable process cannot be silently closed.
    [Fact]
    public void UnreadableProcess_BlocksSilentTakeover()
    {
        var players = new[]
        {
            new RobloxProcessInfo(1, RobloxRunningProbe.ReadHasWindow(() => IntPtr.Zero)),
            new RobloxProcessInfo(2, RobloxRunningProbe.ReadHasWindow(() => throw new Win32Exception(5))),
        };

        Assert.False(SeamlessTakeover.WindowlessOnly(players));
    }

    // Positive control for the same path — genuine tray clients must still be silently reclaimable,
    // which is the entire point of seamless takeover.
    [Fact]
    public void AllGenuinelyWindowless_StillAllowsSilentTakeover()
    {
        var players = new[]
        {
            new RobloxProcessInfo(1, RobloxRunningProbe.ReadHasWindow(() => IntPtr.Zero)),
            new RobloxProcessInfo(2, RobloxRunningProbe.ReadHasWindow(() => IntPtr.Zero)),
        };

        Assert.True(SeamlessTakeover.WindowlessOnly(players));
    }
}
