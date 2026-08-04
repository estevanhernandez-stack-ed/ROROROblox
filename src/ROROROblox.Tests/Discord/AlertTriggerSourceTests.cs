using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// The two signals that become alerts, taken from where they already exist rather than detected
/// afresh: the presence-confirmed close in <c>ApplyPresence</c>, and the memory watchdog's
/// edge-triggered crossing. Nothing new watches anything.
/// </summary>
public class AlertTriggerSourceTests
{
    private static AccountPresenceEventArgs OutOfGame(Guid accountId) =>
        new(accountId, UserPresenceType.OnlineWebsite, placeId: null, gameName: null,
            occurredAtUtc: DateTimeOffset.UtcNow, server: null);

    [Fact]
    public void ApplyPresence_PresenceConfirmedClose_RaisesADroppedOutAlert()
    {
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");
        var raised = new List<AlertTrigger>();
        vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

        vm.ApplyPresence(OutOfGame(row.Id));

        var trigger = Assert.Single(raised);
        Assert.Equal(AlertKind.AccountDroppedOut, trigger.Kind);
        Assert.Equal(row.Id, trigger.AccountId);
    }

    [Fact]
    public void ApplyPresence_DroppedOutAlert_CarriesTheMaskedNameNotTheRealOne()
    {
        // Streamer mode has to hold on the way OUT of the app, exactly as it does for presence.
        // An alert posted to a clan channel is a wider audience than the Discord card, not a
        // narrower one, so this is the surface where leaking the real name costs the most.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");
        var raised = new List<AlertTrigger>();
        vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

        vm.ApplyPresence(OutOfGame(row.Id));

        Assert.Equal("CaptainNoodle", Assert.Single(raised).DisplayName);
        Assert.DoesNotContain("este_real", Assert.Single(raised).DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyPresence_AnAccountThatWasAlreadyIdle_RaisesNothing()
    {
        // "Dropped out" means a transition. A poll confirming an account is still not playing is
        // not news, and firing on it would page the user on every poll cycle forever.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");
        vm.ApplyPresence(OutOfGame(row.Id));
        var raised = new List<AlertTrigger>();
        vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

        vm.ApplyPresence(OutOfGame(row.Id));

        Assert.Empty(raised);
    }

    [Fact]
    public void BuildMemoryAlerts_AnOverCapAccount_BecomesAMemoryWarningCarryingItsBytes()
    {
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");
        var snapshot = new MemoryPressureSnapshot(
            AvailableBytes: 1_000_000_000,
            AggregateGrowthBytesPerHour: 0,
            MinutesToCeiling: 0,
            HasProjection: false,
            TargetAccountId: row.Id,
            Accounts: [new AccountMemory(row.Id, PrivateBytes: 5_000_000_000, GrowthBytesPerHour: 0,
                MinutesToCeiling: 0, OverCap: true, IsTarget: true, ReadOk: true)]);

        var triggers = vm.BuildMemoryAlerts(snapshot, DateTimeOffset.UtcNow);

        var trigger = Assert.Single(triggers);
        Assert.Equal(AlertKind.MemoryWarning, trigger.Kind);
        Assert.Equal(5_000_000_000, trigger.PrivateBytes);
        Assert.Equal("CaptainNoodle", trigger.DisplayName);
    }

    [Fact]
    public void BuildMemoryAlerts_AnUnreadableAccount_IsNeverReportedAsOverCap()
    {
        // ReadOk false means UNKNOWN, not zero. Alerting on a reading we could not take would be
        // a wrong number stated confidently — the exact failure the watchdog exists to avoid.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");
        var snapshot = new MemoryPressureSnapshot(
            AvailableBytes: 1_000_000_000,
            AggregateGrowthBytesPerHour: 0,
            MinutesToCeiling: 0,
            HasProjection: false,
            TargetAccountId: null,
            Accounts: [new AccountMemory(row.Id, PrivateBytes: 0, GrowthBytesPerHour: 0,
                MinutesToCeiling: 0, OverCap: true, IsTarget: false, ReadOk: false)]);

        Assert.Empty(vm.BuildMemoryAlerts(snapshot, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BuildMemoryAlerts_AProjectionOnlyCrossing_ReportsTheTargetAccount()
    {
        // The machine is projected to run out but no single client is over its own cap. The
        // watchdog still names the account worth recycling; the alert should say which one rather
        // than going silent on a crossing that genuinely fired.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");
        var snapshot = new MemoryPressureSnapshot(
            AvailableBytes: 500_000_000,
            AggregateGrowthBytesPerHour: 2_000_000_000,
            MinutesToCeiling: 4,
            HasProjection: true,
            TargetAccountId: row.Id,
            Accounts: [new AccountMemory(row.Id, PrivateBytes: 3_000_000_000, GrowthBytesPerHour: 0,
                MinutesToCeiling: 4, OverCap: false, IsTarget: true, ReadOk: true)]);

        var trigger = Assert.Single(vm.BuildMemoryAlerts(snapshot, DateTimeOffset.UtcNow));
        Assert.Equal(row.Id, trigger.AccountId);
        Assert.Equal(3_000_000_000, trigger.PrivateBytes);
    }

    [Fact]
    public void BuildMemoryAlerts_NothingOverCapAndNoTarget_RaisesNothing()
    {
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");
        var snapshot = new MemoryPressureSnapshot(
            AvailableBytes: 8_000_000_000,
            AggregateGrowthBytesPerHour: 0,
            MinutesToCeiling: 0,
            HasProjection: false,
            TargetAccountId: null,
            Accounts: [new AccountMemory(row.Id, PrivateBytes: 1_000_000_000, GrowthBytesPerHour: 0,
                MinutesToCeiling: 0, OverCap: false, IsTarget: false, ReadOk: true)]);

        Assert.Empty(vm.BuildMemoryAlerts(snapshot, DateTimeOffset.UtcNow));
    }
}
