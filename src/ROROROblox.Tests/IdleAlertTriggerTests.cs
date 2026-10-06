using System.IO;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests;

/// <summary>
/// This file was <c>IdleAlertPresenterTests</c> until v1.33 item 3 (renamed, not deleted, so the
/// history of what it used to assert travels with it). <c>IdleAlertPresenter</c> was the third of
/// three code paths that could put something on a user's screen, and the narrowest: it took a
/// coalesced crossing count, checked ONE mute flag, and called <c>ITrayService.ShowToast</c>. No
/// destinations, no per-account mute, no quiet period, no Discord, no phone.
/// <para>
/// Its three tests mapped one-to-one onto questions that now have different owners, and each is
/// re-asked below against its new owner rather than dropped:
/// </para>
/// <list type="bullet">
/// <item><c>Notify_Unmuted_MultipleAccounts_ShowsOneCoalescedToast</c> → the view model raises one
/// trigger PER account in a single event, and <c>AlertRouter</c> groups them into one delivery per
/// destination. Coalescing moved; it did not disappear.</item>
/// <item><c>Notify_Muted_ShowsNothing</c> → two tests, because the old flag conflated two
/// questions: an empty <c>IdleDestinations</c> (what the one-time <c>MuteIdleAlerts</c> migration
/// writes) and a per-account mute, which the presenter could never express at all.</item>
/// <item><c>Notify_ZeroCount_ShowsNothing</c> → the view model raises nothing for an empty
/// crossing.</item>
/// </list>
/// <para>
/// The latch is deliberately not tested here, because it deliberately did not move:
/// <c>ActivityMonitor.WarnLatched</c> still answers "has this account NEWLY gone idle", which is a
/// different question from "may we speak about it". <c>ActivityMonitorTests</c> keeps it.
/// </para>
/// </summary>
public class IdleAlertTriggerTests
{
    private static readonly IReadOnlyDictionary<AlertCooldownKey, DateTimeOffset> NothingSentYet =
        new Dictionary<AlertCooldownKey, DateTimeOffset>();

    private static AlertTrigger Idle(Guid accountId, string name = "BaronBloxwell") =>
        new(AlertKind.AccountIdle, accountId, name, $"real_{name}", "Pet Simulator 99!",
            PrivateBytes: null, DateTimeOffset.UtcNow);

    // ---- The view model is the trigger source ------------------------------------------------

    [Fact]
    public async Task AnIdleCrossingRaisesAnAccountIdleTriggerForTheRowThatCrossed()
    {
        // Raised through the real IActivityMonitor event, not through the internal seam the shape
        // tests below use, so this also proves MainViewModel is still SUBSCRIBED. A shape test on
        // the seam alone stays green after a dropped subscription, which is the regression that
        // would silence idle alerts completely.
        var monitor = new MainViewModelTests.FakeActivityMonitor();
        var (vm, store, _, path) = MainViewModelTests.Build(activityMonitor: monitor);
        try
        {
            var added = await store.AddAsync("IdleOne", "", "cookie");
            vm.Accounts.Add(new AccountSummary(added));

            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

            monitor.RaiseWarnCrossed([added.Id]);

            var trigger = Assert.Single(raised);
            Assert.Equal(AlertKind.AccountIdle, trigger.Kind);
            Assert.Equal(added.Id, trigger.AccountId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task AnIdleCrossingForThreeAccountsRaisesThreeTriggersInOneEvent()
    {
        // The old presenter was handed a COUNT and wrote "3 accounts idle > 15m" itself. Per-account
        // triggers in one event is what buys everything that count could not: the per-account mute
        // drops one name and keeps the others, WebhookPayload names each row, and the cooldown has a
        // slot per account. One EVENT still matters — AlertRouter groups by kind, so three triggers
        // raised together are one delivery per destination rather than three.
        var monitor = new MainViewModelTests.FakeActivityMonitor();
        var (vm, store, _, path) = MainViewModelTests.Build(activityMonitor: monitor);
        try
        {
            var a = await store.AddAsync("One", "", "cookie");
            var b = await store.AddAsync("Two", "", "cookie");
            var c = await store.AddAsync("Three", "", "cookie");
            foreach (var account in new[] { a, b, c }) vm.Accounts.Add(new AccountSummary(account));

            var events = new List<IReadOnlyList<AlertTrigger>>();
            vm.AlertsRaised += (_, triggers) => events.Add(triggers);

            monitor.RaiseWarnCrossed([a.Id, b.Id, c.Id]);

            var batch = Assert.Single(events);
            Assert.Equal(3, batch.Count);
            Assert.All(batch, t => Assert.Equal(AlertKind.AccountIdle, t.Kind));
            Assert.Equal([a.Id, b.Id, c.Id], batch.Select(t => t.AccountId));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task AnEmptyCrossingRaisesNothing()
    {
        var monitor = new MainViewModelTests.FakeActivityMonitor();
        var (vm, store, _, path) = MainViewModelTests.Build(activityMonitor: monitor);
        try
        {
            vm.Accounts.Add(new AccountSummary(await store.AddAsync("IdleOne", "", "cookie")));

            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

            monitor.RaiseWarnCrossed([]);

            Assert.Empty(raised);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AnIdleTriggerCarriesTheGameTheRowIsSittingIn()
    {
        // Item 1's handoff note, and the reason this test exists at all: WarnThresholdCrossed hands
        // over IReadOnlyList<Guid> and nothing else, so the game name has to be LOOKED UP against
        // the row. Without the lookup every idle alert takes WebhookPayload's no-game arm — "idle ·
        // Roblox kicks an idle account" — and the one fact that makes the alert actionable, which
        // game the account is about to be kicked out of, never reaches the screen.
        var (vm, row) = Discord.DiscordTestHarness.VmWithOneInGameAccount(
            realName: "este_real", maskedName: "CaptainNoodle");
        var raised = new List<AlertTrigger>();
        vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

        vm.ApplyActivityWarnCrossed([row.Id]);

        var trigger = Assert.Single(raised);
        Assert.Equal("Pet Simulator 99!", trigger.GameName);

        var payload = WebhookPayload.ForAlert(AlertKind.AccountIdle, [trigger]);
        Assert.Contains("Pet Simulator 99!", payload.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdleTriggerCarriesTheMaskedNameAsWellAsTheRealOne()
    {
        // Same rule every other kind follows (see AlertTrigger): DisplayName is the streamer-rendered
        // name every destination gets, RealName is the actual one and only the clan channel may use
        // it. The old presenter composed its own string from a count and so could not leak a name;
        // the moment idle alerts carry names they join this contract.
        var (vm, row) = Discord.DiscordTestHarness.VmWithOneInGameAccount(
            realName: "este_real", maskedName: "CaptainNoodle");
        var raised = new List<AlertTrigger>();
        vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

        vm.ApplyActivityWarnCrossed([row.Id]);

        var trigger = Assert.Single(raised);
        Assert.Equal("CaptainNoodle", trigger.DisplayName);
        Assert.Equal("este_real", trigger.RealName);
    }

    [Fact]
    public async Task ACrossingForAnAccountWithNoRowRaisesNothing()
    {
        // BuildMemoryAlerts' precedent: a trigger whose row cannot be found has no name to say and
        // no game to name, so it is dropped rather than announced as "An account". Reachable only
        // if the monitor outlives a deleted row.
        var monitor = new MainViewModelTests.FakeActivityMonitor();
        var (vm, store, _, path) = MainViewModelTests.Build(activityMonitor: monitor);
        try
        {
            vm.Accounts.Add(new AccountSummary(await store.AddAsync("IdleOne", "", "cookie")));

            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

            monitor.RaiseWarnCrossed([Guid.NewGuid()]);

            Assert.Empty(raised);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---- The router is the only thing that decides whether it reaches a screen ----------------

    [Fact]
    public void WithTheDesktopTicked_AnIdleCrossingIsExactlyOneDesktopDelivery()
    {
        // Replaces Notify_Unmuted_MultipleAccounts_ShowsOneCoalescedToast. Three triggers, one
        // RoutedAlert, three names inside it — "exactly one balloon per event, not two".
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var config = new DiscordConfig { IdleDestinations = [AlertDestination.Local] };

        var routed = AlertRouter.Route(
            [Idle(ids[0], "One"), Idle(ids[1], "Two"), Idle(ids[2], "Three")],
            config, NothingSentYet, DateTimeOffset.UtcNow);

        var alert = Assert.Single(routed);
        Assert.Equal(AlertDestination.Local, alert.Destination);
        Assert.Equal(AlertKind.AccountIdle, alert.Kind);
        Assert.Equal(3, alert.Triggers.Count);
    }

    [Fact]
    public void WithTheDesktopUnticked_AnIdleCrossingReachesNoScreen()
    {
        // Replaces Notify_Muted_ShowsNothing, half one. An empty IdleDestinations is what the
        // one-time MuteIdleAlerts migration writes for a user who had the old checkbox ticked, and
        // it has to mean silence — including no fallback to the desktop, which is what Resolve does
        // for a destination that is merely unconfigured.
        var routed = AlertRouter.Route(
            [Idle(Guid.NewGuid())],
            new DiscordConfig { IdleDestinations = [] },
            NothingSentYet, DateTimeOffset.UtcNow);

        Assert.Empty(routed);
    }

    [Fact]
    public void APerAccountMuteSilencesThatAccountsIdleAlertAndNobodyElses()
    {
        // Replaces Notify_Muted_ShowsNothing, half two — and this half the presenter could not
        // express at all. Its single flag was all-or-nothing across the whole roster, so the user
        // who wanted one noisy alt to stop had to silence every account to get it.
        var noisy = Guid.NewGuid();
        var quiet = Guid.NewGuid();

        var routed = AlertRouter.Route(
            [Idle(noisy, "Noisy"), Idle(quiet, "Quiet")],
            new DiscordConfig { IdleDestinations = [AlertDestination.Local], MutedAccountIds = [noisy] },
            NothingSentYet, DateTimeOffset.UtcNow);

        var alert = Assert.Single(routed);
        Assert.Equal(quiet, Assert.Single(alert.Triggers).AccountId);
    }

    [Fact]
    public void ARepeatIdleCrossingInsideTheQuietPeriodIsSuppressed()
    {
        // The capability the presenter never had: the latch stops a SECOND alert while an account
        // stays idle, but an account that flickers in and out of idle re-latches each time, and
        // before this the presenter spoke on every one of them.
        var id = Guid.NewGuid();
        var trigger = Idle(id);
        var sentAt = DateTimeOffset.UtcNow;
        var lastSent = new Dictionary<AlertCooldownKey, DateTimeOffset>
        {
            [AlertCooldownKey.For(trigger)] = sentAt,
        };
        var config = new DiscordConfig { IdleDestinations = [AlertDestination.Local] };

        Assert.Empty(AlertRouter.Route([trigger], config, lastSent,
            sentAt + AlertCadence.DefaultQuietPeriod - TimeSpan.FromSeconds(1)));

        Assert.Single(AlertRouter.Route([trigger], config, lastSent,
            sentAt + AlertCadence.DefaultQuietPeriod));
    }
}
