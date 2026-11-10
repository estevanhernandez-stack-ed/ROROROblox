using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The launch pre-flight (2026-11-10). A launch shell-executes a <c>roblox-player:</c> link. With no
/// handler registered, <c>Process.Start</c> does NOT throw: it returns no process and Windows opens
/// its own "You'll need a new app to open this link" dialog. Measured on Windows 11 with an
/// unregistered scheme. So the <c>Win32Exception</c> catch that maps to
/// <see cref="LaunchFailureKind.RobloxNotInstalled"/> never ran for the case it was written for, the
/// launch reported Started, and the row ended on "Launch never connected". The Microsoft Store
/// edition of Roblox is the common way to get there: its manifest declares <c>roblox</c> only.
/// The launcher half of this lives in <see cref="RobloxLauncherTests"/> (the "Launch pre-flight"
/// region), where the launcher's existing test doubles are.
/// </summary>
public class RobloxLaunchHandlerPreflightTests
{
    // === The probe says "missing" only when every lookup agrees ===

    [Fact]
    public void Probe_EveryLookupSaysNo_IsNotRegistered()
    {
        var probe = new RobloxLaunchHandlerProbe(new Func<string, bool>[] { _ => false, _ => false });

        Assert.False(probe.IsRegistered());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Probe_AnyLookupSaysYes_IsRegistered(bool first, bool second)
    {
        var probe = new RobloxLaunchHandlerProbe(new Func<string, bool>[] { _ => first, _ => second });

        Assert.True(probe.IsRegistered());
    }

    [Fact]
    public void Probe_ALookupThatThrows_CountsAsRegistered()
    {
        // A locked-down PC that refuses a registry read must not be told Roblox is missing.
        var probe = new RobloxLaunchHandlerProbe(new Func<string, bool>[]
        {
            _ => false,
            _ => throw new UnauthorizedAccessException("registry read refused"),
        });

        Assert.True(probe.IsRegistered());
    }

    [Fact]
    public void Probe_NoLookupsAtAll_CountsAsRegistered()
    {
        // Nothing checked means nothing known. Vacuous agreement must not read as "missing".
        var probe = new RobloxLaunchHandlerProbe(Array.Empty<Func<string, bool>>());

        Assert.True(probe.IsRegistered());
    }

    [Fact]
    public void Probe_AsksAboutTheSchemeTheLauncherUses()
    {
        var asked = new List<string>();
        var probe = new RobloxLaunchHandlerProbe(new Func<string, bool>[] { s => { asked.Add(s); return true; } });

        probe.IsRegistered();

        Assert.Equal(new[] { "roblox-player" }, asked);
        // The launcher's own link must start with that scheme, or the probe checks the wrong thing.
        Assert.StartsWith(RobloxLaunchHandlerProbe.Scheme + ":",
            RobloxLauncher.BuildLaunchUri("T", 1, "2", "https://example.invalid/"));
    }

    // === The real lookups, against this machine ===

    [Fact]
    public void RealLookups_FindTheHttpHandlerEveryWindowsHas()
    {
        Assert.True(RobloxLaunchHandlerProbe.IsSchemeRegistered("http"));
    }

    [Fact]
    public void RealLookups_DoNotFindASchemeNothingRegisters()
    {
        Assert.False(RobloxLaunchHandlerProbe.IsSchemeRegistered("rororo-test-scheme-" + Guid.NewGuid().ToString("N")));
    }
}
