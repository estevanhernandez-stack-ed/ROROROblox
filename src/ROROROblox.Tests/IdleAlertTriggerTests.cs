using System.IO;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
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

    /// <summary>
    /// Drives a row in-game through the production seam. Required before any idle crossing, because
    /// idle means IN-GAME idle (Este's ruling 2026-10-07) and a row added straight to
    /// <c>vm.Accounts</c> starts at <see cref="UserPresenceType.Offline"/>. Two tests here used to
    /// pass without it, which is the only reason the wrong behaviour shipped green.
    /// </summary>
    private static void InGame(MainViewModel vm, Guid accountId) =>
        vm.ApplyPresence(new AccountPresenceEventArgs(
            accountId, UserPresenceType.InGame, placeId: 8737899170,
            gameName: "Pet Simulator 99!", occurredAtUtc: DateTimeOffset.UtcNow, server: null));

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
            InGame(vm, added.Id);

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
            foreach (var account in new[] { a, b, c })
            {
                vm.Accounts.Add(new AccountSummary(account));
                InGame(vm, account.Id);
            }

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

    // ---- Idle means IN-GAME idle -------------------------------------------------------------
    //
    // Este's ruling, 2026-10-07, on a question item 1 shipped the wrong way: "an account that isn't
    // in a game should not be getting an idle alert. It needs to be in game idle."
    //
    // The alert's whole value is that Roblox is about to kick the account out of a game it is
    // earning in. An account sitting on the website, or in Studio, or whose presence we cannot read
    // at all, has nothing to be kicked out of — the alert would be noise at best, and at worst it
    // says "went idle" about an account the user deliberately parked.
    //
    // ActivityMonitor is the wrong place to gate it: WarnLatched answers "has this account NEWLY
    // gone quiet", which is true and useful regardless of presence, and the latch feeds more than
    // alerts. The gate belongs at the trigger, where "may we speak about this" is already decided.

    [Theory]
    [InlineData(UserPresenceType.OnlineWebsite)] // logged in, not playing
    [InlineData(UserPresenceType.InStudio)]      // building, not playing
    [InlineData(UserPresenceType.Offline)]       // not online anywhere
    [InlineData(UserPresenceType.Invisible)]     // privacy filter: we cannot tell, so we do not claim
    public void AnIdleCrossingForAnAccountThatIsNotInAGameRaisesNothing(UserPresenceType presence)
    {
        var (vm, row) = Discord.DiscordTestHarness.VmWithOneInGameAccount(
            realName: "este_real", maskedName: "CaptainNoodle");

        // Starts in-game from the harness, then leaves. Driven through ApplyPresence, the same seam
        // production uses, so this exercises the real transition rather than a poked field.
        vm.ApplyPresence(new AccountPresenceEventArgs(
            row.Id, presence, placeId: null, gameName: null,
            occurredAtUtc: DateTimeOffset.UtcNow, server: null));

        var raised = new List<AlertTrigger>();
        vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

        vm.ApplyActivityWarnCrossed([row.Id]);

        Assert.Empty(raised);
    }

    [Fact]
    public void AnIdleCrossingForAnInGameAccountStillRaises()
    {
        // The anchor for the gate above: it must not be satisfiable by raising nothing ever.
        var (vm, row) = Discord.DiscordTestHarness.VmWithOneInGameAccount(
            realName: "este_real", maskedName: "CaptainNoodle");
        var raised = new List<AlertTrigger>();
        vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

        vm.ApplyActivityWarnCrossed([row.Id]);

        Assert.Single(raised);
        Assert.True(row.InGame);
    }

    [Fact]
    public void AMixedCrossingRaisesOnlyForTheInGameAccounts()
    {
        // The coalescing case. ActivityMonitor hands over every account that crossed in one event,
        // and a real roster is mixed — some playing, some parked on the website. The gate is
        // per-account inside the event, not a veto on the whole event.
        var (vm, inGame) = Discord.DiscordTestHarness.VmWithOneInGameAccount(
            realName: "playing_real", maskedName: "StillPlaying");

        var parked = new AccountSummary(new Account(
            Guid.NewGuid(), "parked_real", "", DateTimeOffset.UtcNow, LastLaunchedAt: null));
        vm.Accounts.Add(parked);
        vm.ApplyPresence(new AccountPresenceEventArgs(
            parked.Id, UserPresenceType.OnlineWebsite, placeId: null, gameName: null,
            occurredAtUtc: DateTimeOffset.UtcNow, server: null));

        var raised = new List<AlertTrigger>();
        vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);

        vm.ApplyActivityWarnCrossed([inGame.Id, parked.Id]);

        var trigger = Assert.Single(raised);
        Assert.Equal(inGame.Id, trigger.AccountId);
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
