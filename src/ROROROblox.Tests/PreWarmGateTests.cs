using ROROROblox.App.ViewModels;

namespace ROROROblox.Tests;

/// <summary>
/// Tests for the pure <see cref="PreWarmGate"/> install-deferral gate extracted out of
/// <see cref="MainViewModel"/> so the batch-launch decision + the wait-complete predicate can be
/// exercised without the heavy view model or a live process / network. Spec
/// §"Components > 2. Pre-warm batch launch" + "3. Version pre-check" + "Data flow":
/// strap-handling OR no-update → launch the whole batch as today; update-pending (and no strap) →
/// pre-warm the first client, then release the rest once the installer is gone AND #1 attached.
/// </summary>
public class PreWarmGateTests
{
    // === Decide: strap-handling short-circuits regardless of the other signals ===

    [Fact]
    public void Decide_StrapHandling_NoUpdate_LaunchAllNow()
    {
        Assert.Equal(
            PreWarmDecision.LaunchAllNow,
            PreWarmGate.Decide(strapHandling: true, updatePending: false, updateChurn: false));
    }

    [Fact]
    public void Decide_StrapHandling_UpdatePending_StillLaunchAllNow()
    {
        // A strap updates Roblox proactively itself — pre-warming would double-update. The strap
        // path wins even when an update is pending (spec Riders §7).
        Assert.Equal(
            PreWarmDecision.LaunchAllNow,
            PreWarmGate.Decide(strapHandling: true, updatePending: true, updateChurn: false));
    }

    [Fact]
    public void Decide_StrapHandling_Churning_StillLaunchAllNow()
    {
        // Churn under a strap is the strap's own update loop. The short-circuit stays ahead of the
        // F-104 signal deliberately — widening it here would double-update exactly the users who
        // already have a bootstrapper serialising for them.
        Assert.Equal(
            PreWarmDecision.LaunchAllNow,
            PreWarmGate.Decide(strapHandling: true, updatePending: false, updateChurn: true));
    }

    // === Decide: no strap — either update signal drives the decision ===

    [Fact]
    public void Decide_NoStrap_Quiet_LaunchAllNow()
    {
        // The common path: full multilaunch speed, no pre-warm wait.
        Assert.Equal(
            PreWarmDecision.LaunchAllNow,
            PreWarmGate.Decide(strapHandling: false, updatePending: false, updateChurn: false));
    }

    [Fact]
    public void Decide_NoStrap_UpdatePending_PreWarmThenRelease()
    {
        Assert.Equal(
            PreWarmDecision.PreWarmThenRelease,
            PreWarmGate.Decide(strapHandling: false, updatePending: true, updateChurn: false));
    }

    [Fact]
    public void Decide_NoStrap_ChurnAloneIsEnoughToHold()
    {
        // F-104's second signal, and the one that needs no network. When versions are landing on
        // top of each other, no single version comparison is stable enough to trust — so a clean
        // "no update pending" must NOT release the batch on its own.
        Assert.Equal(
            PreWarmDecision.PreWarmThenRelease,
            PreWarmGate.Decide(strapHandling: false, updatePending: false, updateChurn: true));
    }

    [Fact]
    public void Decide_NoStrap_BothSignals_PreWarmThenRelease()
    {
        Assert.Equal(
            PreWarmDecision.PreWarmThenRelease,
            PreWarmGate.Decide(strapHandling: false, updatePending: true, updateChurn: true));
    }

    // === PreWarmWaitComplete: done only when installer gone AND first attached ===

    [Fact]
    public void WaitComplete_InstallerGone_FirstAttached_True()
    {
        Assert.True(PreWarmGate.PreWarmWaitComplete(installerRunning: false, firstAttached: true));
    }

    [Fact]
    public void WaitComplete_InstallerRunning_FirstAttached_False()
    {
        // Update still installing — keep waiting even though #1 attached.
        Assert.False(PreWarmGate.PreWarmWaitComplete(installerRunning: true, firstAttached: true));
    }

    [Fact]
    public void WaitComplete_InstallerGone_NotAttached_False()
    {
        // Installer cleared but #1 hasn't attached yet — keep waiting.
        Assert.False(PreWarmGate.PreWarmWaitComplete(installerRunning: false, firstAttached: false));
    }

    [Fact]
    public void WaitComplete_InstallerRunning_NotAttached_False()
    {
        Assert.False(PreWarmGate.PreWarmWaitComplete(installerRunning: true, firstAttached: false));
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void WaitComplete_TruthTable(bool installerRunning, bool firstAttached, bool expected)
    {
        Assert.Equal(expected, PreWarmGate.PreWarmWaitComplete(installerRunning, firstAttached));
    }

    // === AttachFailedMessage: install-aware ProcessAttachFailed copy (spec Riders §5) ===

    [Fact]
    public void AttachFailedMessage_InstallerRunning_UpdatingCopy()
    {
        // Installer running → the client hasn't attached because Roblox is mid-update, not a failure.
        Assert.Equal("Roblox is updating — hold on.", PreWarmGate.AttachFailedMessage(installerRunning: true));
    }

    [Fact]
    public void AttachFailedMessage_NoInstaller_FailureCopy()
    {
        // No installer → a real never-connected failure; keep the existing antivirus/version hint.
        Assert.Equal(
            "Launch never connected. Check Roblox is current + antivirus isn't blocking.",
            PreWarmGate.AttachFailedMessage(installerRunning: false));
    }
}
