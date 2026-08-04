using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// The per-account half of the two-control design: routing is per-trigger, muting is per-account.
/// Mute lives in the Discord config rather than accounts.dat — it is Discord state, not account
/// state, and an exported account should not carry someone else's notification preferences.
/// </summary>
public class AccountMuteTests
{
    [Fact]
    public async Task MutingARow_PersistsIntoTheDiscordConfig()
    {
        var (vm, row, configStore) = DiscordTestHarness.VmWithConfigStore();

        await vm.SetAlertsMutedAsync(row, muted: true);

        Assert.Contains(row.Id, (await configStore.LoadAsync()).MutedAccountIds);
        Assert.True(row.AlertsMuted);
    }

    [Fact]
    public async Task UnmutingARow_RemovesItFromTheConfig()
    {
        var (vm, row, configStore) = DiscordTestHarness.VmWithConfigStore();
        await vm.SetAlertsMutedAsync(row, muted: true);

        await vm.SetAlertsMutedAsync(row, muted: false);

        Assert.Empty((await configStore.LoadAsync()).MutedAccountIds);
        Assert.False(row.AlertsMuted);
    }

    [Fact]
    public async Task MutingIsIdempotent_NoDuplicateIds()
    {
        var (vm, row, configStore) = DiscordTestHarness.VmWithConfigStore();

        await vm.SetAlertsMutedAsync(row, muted: true);
        await vm.SetAlertsMutedAsync(row, muted: true);

        Assert.Single((await configStore.LoadAsync()).MutedAccountIds);
    }

    [Fact]
    public async Task MutingOneRow_LeavesEveryOtherSettingAlone()
    {
        // The mute write is a read-modify-write of the whole config record. Getting it wrong
        // silently wipes the user's webhook URL or presence toggle — a setting they would then
        // have to re-enter without ever being told why.
        var (vm, row, configStore) = DiscordTestHarness.VmWithConfigStore();
        await configStore.SaveAsync(new DiscordConfig
        {
            PresenceEnabled = true,
            JoinEnabled = true,
            MineWebhookUrl = "https://discord.com/api/webhooks/1/tok",
            DroppedOutDestination = AlertDestination.Mine,
        });

        await vm.SetAlertsMutedAsync(row, muted: true);

        var saved = await configStore.LoadAsync();
        Assert.True(saved.PresenceEnabled);
        Assert.True(saved.JoinEnabled);
        Assert.Equal("https://discord.com/api/webhooks/1/tok", saved.MineWebhookUrl);
        Assert.Equal(AlertDestination.Mine, saved.DroppedOutDestination);
        Assert.Contains(row.Id, saved.MutedAccountIds);
    }

    [Fact]
    public async Task AMutedAccount_ProducesNoAlertEvenWhenItDropsOut()
    {
        // End-to-end for the mute: the row-level toggle has to actually reach AlertRouter, not
        // merely persist. Muting a row and then having it drop out is the whole scenario.
        var (vm, row, configStore) = DiscordTestHarness.VmWithConfigStore();
        await vm.SetAlertsMutedAsync(row, muted: true);
        var config = await configStore.LoadAsync();

        var trigger = new AlertTrigger(
            AlertKind.AccountDroppedOut, row.Id, row.RenderName, row.DisplayName, "Pet Simulator 99!", null,
            DateTimeOffset.UtcNow);

        var routed = AlertRouter.Route(
            [trigger],
            config with { DroppedOutDestination = AlertDestination.Local },
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(),
            DateTimeOffset.UtcNow);

        Assert.Empty(routed);
    }
}
