using ROROROblox.App.ViewModels;
using ROROROblox.Tests.Discord;

namespace ROROROblox.Tests;

/// <summary>
/// The notification half of streamer mode — the half nothing covered until 2026-08-04.
/// <para>
/// Streamer mode can be flipped from three places: the Settings checkbox, the tray menu, and a
/// plugin. They are meant to be three views of one source of truth
/// (<c>IStreamerIdentityProvider</c>), and the mechanism that makes that true is
/// <c>MainViewModel.OnStreamerIdentityChanged</c> raising <c>PropertyChanged</c> when the provider
/// confirms a flip.
/// </para>
/// <para>
/// Wave 1 moved the toggle off the main window and briefly broke this: a two-way binding was
/// replaced by a one-shot read in <c>OnLoaded</c>, so flipping streamer mode from the tray while
/// the Settings window was open left the checkbox reporting the opposite of the truth — on the one
/// control that tells a streamer whether their privacy mask is on. The notification was going out
/// the whole time; nothing on the settings side was listening. This test pins the sending half so
/// a future consumer has something to trust.
/// </para>
/// </summary>
public class StreamerModeNotificationTests
{
    [Fact]
    public async Task ProviderFlip_RaisesPropertyChangedForStreamerModeOn()
    {
        var (vm, _) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // The provider is what the tray and plugins write through — not the view model.
        await DiscordTestHarness.SetStreamerModeAsync(vm, active: false);

        Assert.Contains(nameof(MainViewModel.StreamerModeOn), raised);
    }

    [Fact]
    public async Task ProviderFlip_LeavesStreamerModeOnReadingTheProvider()
    {
        // The property is a read-through, not a cached flag: whoever asks after the flip gets the
        // new answer. A consumer that re-reads on the notification is therefore correct by
        // construction — which is the contract the Settings checkbox now relies on.
        var (vm, _) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");
        Assert.True(vm.StreamerModeOn);

        await DiscordTestHarness.SetStreamerModeAsync(vm, active: false);

        Assert.False(vm.StreamerModeOn);
    }
}
