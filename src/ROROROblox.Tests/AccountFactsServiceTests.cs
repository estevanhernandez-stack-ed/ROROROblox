using System.IO;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 3: <see cref="AccountFactsService"/>, the facts lane. A real <see cref="AccountStore"/>
/// over a temp file (fake cookies only), a scripted <see cref="IAccountFactsApi"/>, and a
/// <see cref="FakeTimeProvider"/> for the clock, the inter-account delay and the slow timer. No
/// network, no real waiting on the pacing: the pump advances the fake clock and then yields real
/// time with <c>Task.Delay</c> (never a spin), the shape <see cref="FpsCapSettlerTests"/> settled on.
/// </summary>
public class AccountFactsServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    // ---- startup rule ----

    [Fact]
    public async Task NothingIsRequested_AfterConstruction_Start_AndTheLoadOfEightAccounts_UntilAnExpandOrATick()
    {
        using var rig = await Rig.CreateAsync(8);

        rig.Service.Start();
        await Task.Delay(150);

        Assert.Equal(0, rig.Service.RequestCount);
        Assert.Empty(rig.Api.Snapshot());
        Assert.Equal(0, rig.Clock.TimersCreated); // no timer, not even an inter-account delay
        Assert.False(rig.Service.TimerArmed);

        // An expand is a request; only then does anything go out, and only for that account.
        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.PumpUntilAsync(() => rig.Service.RequestCount == 3);
        Assert.All(rig.Api.Snapshot(), c => Assert.Equal("A0", c.Label));
    }

    [Fact]
    public void ProductionRegistration_ResolvesTheService_WithNoRequestNoTimerAndNothingEnqueued()
    {
        // The registration half of the startup rule: the REAL composition root builds the service
        // (resolving the typed client, the store and the logger) and construction alone does
        // nothing. The roster delegate is lazy, so MainViewModel is never resolved here.
        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<AccountFactsService>();

        Assert.Same(service, provider.GetRequiredService<AccountFactsService>()); // one lane for the process
        Assert.Equal(0, service.RequestCount);
        Assert.False(service.TimerArmed);
        Assert.False(service.MembershipEnabled); // C1 flips this, on a measured answer
        Assert.NotNull(provider.GetRequiredService<IAccountFactsApi>());
    }

    [Fact]
    public void MainViewModel_TakesBothServices_AndProductionDiRegistersBoth()
    {
        // Both are optional ctor parameters (30 test construction sites pass neither), and MS DI
        // fills an optional parameter ONLY if the service is registered. Unregistered, production
        // would build a view model that never hears a facts update and never learns a resolved id,
        // and every test would stay green.
        var parameters = typeof(MainViewModel).GetConstructors().Single().GetParameters();
        Assert.All(new[] { typeof(AccountFactsService), typeof(AccountUserIdBackfillService) }, type =>
        {
            var parameter = Assert.Single(parameters, p => p.ParameterType == type);
            Assert.True(parameter.IsOptional);
        });

        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        Assert.Contains(services, d => d.ServiceType == typeof(AccountFactsService) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(AccountUserIdBackfillService) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task TheTimer_ArmsOnlyOnStartTimer_AndItsFirstTickIsAFullTickLater()
    {
        using var rig = await Rig.CreateAsync(8);

        rig.Service.StartTimer();
        await Task.Delay(100);
        Assert.True(rig.Service.TimerArmed);
        Assert.Equal(1, rig.Clock.TimersCreated);
        Assert.Equal(0, rig.Service.RequestCount);

        rig.Clock.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(100);
        Assert.Equal(0, rig.Service.RequestCount); // one second short of a full tick: still nothing

        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        await rig.WaitUntilAsync(() => rig.Service.RequestCount == 3);
        await Task.Delay(100);

        // One tick, one account (the first of the equally stale never-fetched ones), three calls.
        Assert.Equal(3, rig.Service.RequestCount);
        Assert.All(rig.Api.Snapshot(), c => Assert.Equal("A0", c.Label));
    }

    [Fact]
    public async Task StartTimer_IsIdempotent()
    {
        using var rig = await Rig.CreateAsync(2);

        rig.Service.StartTimer();
        rig.Service.StartTimer();
        await Task.Delay(50);

        Assert.Equal(1, rig.Clock.TimersCreated);
    }

    // ---- pacing ----

    [Fact]
    public async Task OneAccountsThreeCallsComplete_BeforeTheNextAccountStarts()
    {
        using var rig = await Rig.CreateAsync(3);
        foreach (var id in rig.Ids) { Assert.True(rig.Service.Enqueue(id)); }

        await rig.PumpUntilAsync(() => rig.Api.Snapshot().Count == 9);

        var calls = rig.Api.Snapshot();
        Assert.Equal(
            new[] { "A0", "A0", "A0", "A1", "A1", "A1", "A2", "A2", "A2" },
            calls.Select(c => c.Label).ToArray());
        Assert.Equal(
            new[] { "robux", "friends", "membership", "robux", "friends", "membership", "robux", "friends", "membership" },
            calls.Select(c => c.Kind).ToArray());
    }

    [Fact]
    public async Task ConsecutiveAccounts_AreTwoToThreeSecondsApart_OnAFakeClock()
    {
        using var rig = await Rig.CreateAsync(4);
        foreach (var id in rig.Ids) { Assert.True(rig.Service.Enqueue(id)); }

        await rig.PumpUntilAsync(() => rig.Api.Snapshot().Count == 12, step: TimeSpan.FromMilliseconds(50));

        var starts = rig.Api.Snapshot()
            .GroupBy(c => c.Label)
            .OrderBy(g => g.First().Seq)
            .Select(g => g.First().At)
            .ToList();
        Assert.Equal(4, starts.Count);
        for (var i = 1; i < starts.Count; i++)
        {
            var gap = starts[i] - starts[i - 1];
            // The floor is the guarantee (never requested in the same second, never early). The
            // ceiling is 3.0 s of jitter plus one pump step of observation slack.
            Assert.True(gap >= TimeSpan.FromSeconds(2.0), $"account {i} started {gap.TotalSeconds:F3} s after the previous one");
            Assert.True(gap <= TimeSpan.FromSeconds(3.05) + TimeSpan.FromMilliseconds(50), $"account {i} started {gap.TotalSeconds:F3} s after the previous one");
        }
    }

    // ---- triggers ----

    [Fact]
    public async Task AnExpand_OnFactsYoungerThan60Seconds_EnqueuesNothing()
    {
        using var rig = await Rig.CreateAsync(2);
        rig.SetAge(0, TimeSpan.FromSeconds(30));
        rig.SetAge(1, TimeSpan.FromSeconds(61));

        Assert.False(rig.Service.Enqueue(rig.Ids[0]));
        await Task.Delay(100);
        Assert.Equal(0, rig.Service.RequestCount);

        Assert.True(rig.Service.Enqueue(rig.Ids[1]));
        await rig.PumpUntilAsync(() => rig.Service.RequestCount == 3);
    }

    [Fact]
    public async Task AnExpand_RightAfterAFailedAttempt_EnqueuesNothing_SoRepeatedExpandsCannotHammer()
    {
        using var rig = await Rig.CreateAsync(1);
        rig.Api.Throw = (_, kind) => kind == "robux" ? new AccountFactsRequestException(403) : null;

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        Assert.False(rig.Service.Enqueue(rig.Ids[0])); // the failed try counts as a try
        Assert.Equal(1, rig.Service.RequestCount);
    }

    [Fact]
    public async Task AnAccountAlreadyQueued_IsNotQueuedTwice_ByAnExpandOrATick()
    {
        using var rig = await Rig.CreateAsync(2);
        var gate = new TaskCompletionSource();
        rig.Api.Gate = gate.Task;

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.WaitUntilAsync(() => rig.Service.RequestCount == 1); // A0 in flight, parked on the gate

        Assert.True(rig.Service.Enqueue(rig.Ids[1]));  // A1 waits behind it
        Assert.False(rig.Service.Enqueue(rig.Ids[1])); // already queued: not twice
        Assert.Null(rig.Service.RunTick());            // the tick's stalest pick is A1, already queued

        gate.SetResult();
        await rig.PumpUntilAsync(() => rig.Updates.Count == 2);
        Assert.Equal(6, rig.Service.RequestCount);     // two accounts, three calls each, nothing doubled
    }

    [Fact]
    public async Task EnqueueOfAnUnknownAccount_IsIgnored()
    {
        using var rig = await Rig.CreateAsync(1);

        Assert.False(rig.Service.Enqueue(Guid.NewGuid()));
        await Task.Delay(50);
        Assert.Equal(0, rig.Service.RequestCount);
    }

    // ---- the tick ----

    [Fact]
    public async Task ATick_PicksTheStalestAccountOlderThanTenMinutes_AndOnlyOne()
    {
        using var rig = await Rig.CreateAsync(5);
        rig.SetAge(0, TimeSpan.FromMinutes(5));    // too young
        rig.SetAge(1, TimeSpan.FromMinutes(40));   // the stalest
        rig.SetAge(2, TimeSpan.FromMinutes(25));
        rig.SetAge(3, TimeSpan.FromMinutes(12));
        rig.SetAge(4, TimeSpan.FromMinutes(9));    // too young

        Assert.Equal(rig.Ids[1], rig.Service.RunTick());
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        // One tick queued one account, and its three calls are the only calls made.
        Assert.Equal(3, rig.Api.Snapshot().Count);
        Assert.All(rig.Api.Snapshot(), c => Assert.Equal("A1", c.Label));

        // The next tick moves down the ladder.
        Assert.Equal(rig.Ids[2], rig.Service.RunTick());
    }

    [Fact]
    public async Task ATick_WhenEveryAccountIsFreshEnough_QueuesNothing()
    {
        using var rig = await Rig.CreateAsync(3);
        for (var i = 0; i < 3; i++) { rig.SetAge(i, TimeSpan.FromMinutes(9)); }

        Assert.Null(rig.Service.RunTick());
        await Task.Delay(50);
        Assert.Equal(0, rig.Service.RequestCount);
    }

    [Fact]
    public async Task ATick_TreatsAnAccountNeverFetchedAsTheStalest()
    {
        using var rig = await Rig.CreateAsync(2);
        rig.SetAge(0, TimeSpan.FromHours(3)); // old, but it has been fetched once
        // account 1: Facts null, never fetched

        Assert.Equal(rig.Ids[1], rig.Service.RunTick());
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);
    }

    [Fact]
    public async Task AnExpiredAccount_IsNeverPicked_ByATickOrAnExpand()
    {
        using var rig = await Rig.CreateAsync(3);
        rig.SetExpired(0);                          // the stalest by far: never fetched, but expired
        rig.SetAge(1, TimeSpan.FromMinutes(30));
        rig.SetAge(2, TimeSpan.FromMinutes(11));

        Assert.False(rig.Service.Enqueue(rig.Ids[0]));
        Assert.Equal(rig.Ids[1], rig.Service.RunTick());
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);
        Assert.Equal(rig.Ids[2], rig.Service.RunTick());
        await rig.PumpUntilAsync(() => rig.Updates.Count == 2);

        Assert.DoesNotContain(rig.Api.Snapshot(), c => c.Label == "A0");
        Assert.Null(rig.Service.RunTick()); // 1 and 2 are fresh now, 0 is still expired
    }

    [Fact]
    public async Task AFailingAccount_DoesNotStarveTheOthers_OnTheTimer()
    {
        using var rig = await Rig.CreateAsync(2);
        rig.Api.Throw = (label, kind) => label == "A0" && kind == "robux" ? new AccountFactsRequestException(401) : null;

        Assert.Equal(rig.Ids[0], rig.Service.RunTick()); // both never fetched: the first wins
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        // A0 failed and so kept its null FetchedAtUtc. Without an attempt clock the next tick would
        // pick A0 again, a minute after the last, forever.
        Assert.Equal(rig.Ids[1], rig.Service.RunTick());
    }

    // ---- failure handling ----

    [Theory]
    [InlineData("robux", 401, 1)]
    [InlineData("robux", 403, 1)]
    [InlineData("friends", 401, 2)]
    [InlineData("friends", 403, 2)]
    [InlineData("membership", 401, 3)]
    [InlineData("membership", 403, 3)]
    public async Task A401Or403_FromAnyCall_LeavesPreviousValuesStanding_AndSetsLastError(string failingKind, int status, int callsMade)
    {
        using var rig = await Rig.CreateAsync(1);
        var previous = new AccountFacts(Robux: 50, Membership: "none", FriendsOnline: 2, FetchedAtUtc: T0 - TimeSpan.FromMinutes(20), LastError: null);
        await rig.SeedAsync(0, previous);
        rig.Api.Throw = (_, kind) => kind == failingKind && kind != "membership" ? new AccountFactsRequestException(status) : null;
        rig.Api.MembershipStatus = failingKind == "membership" ? status : 200;

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        var expected = previous with { LastError = status.ToString() };
        Assert.Equal(expected, Assert.Single(rig.Updates).Facts);
        Assert.Equal(expected, Assert.Single(await rig.Store.ListAsync()).Facts);
        Assert.Equal(callsMade, rig.Api.Snapshot().Count); // a rejected cookie is not asked a fourth question
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("500")]
    [InlineData("error")]
    public async Task AnyOtherFailure_OfARobuxCall_AlsoKeepsThePreviousValues(string kind)
    {
        using var rig = await Rig.CreateAsync(1);
        var previous = new AccountFacts(50, "plus", 2, T0 - TimeSpan.FromMinutes(20), null);
        await rig.SeedAsync(0, previous);
        rig.Api.Throw = (_, k) => k != "robux" ? null : kind switch
        {
            "timeout" => new TaskCanceledException("the request timed out"),
            "network" => new HttpRequestException("connection reset"),
            "500" => new AccountFactsRequestException(500),
            _ => new InvalidOperationException("boom"),
        };

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        Assert.Equal(previous with { LastError = kind }, Assert.Single(rig.Updates).Facts);
    }

    [Fact]
    public async Task AFailureWithNoPreviousFacts_RecordsTheReasonAndInventsNoValues()
    {
        using var rig = await Rig.CreateAsync(1);
        rig.Api.Throw = (_, kind) => kind == "robux" ? new AccountFactsRequestException(403) : null;

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        Assert.Equal(new AccountFacts(null, null, null, null, "403"), Assert.Single(rig.Updates).Facts);
    }

    [Fact]
    public async Task AFreshSuccess_StampsTheTimeAndClearsAnOldError()
    {
        using var rig = await Rig.CreateAsync(1);
        await rig.SeedAsync(0, new AccountFacts(1, "none", 1, T0 - TimeSpan.FromMinutes(30), "403"));
        rig.Api.Robux = 1240;
        rig.Api.Friends = 3;

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        var facts = Assert.Single(rig.Updates).Facts;
        Assert.Equal(1240, facts.Robux);
        Assert.Equal(3, facts.FriendsOnline);
        Assert.Equal("none", facts.Membership); // default probe answers 200 false
        Assert.Null(facts.LastError);
        Assert.True(facts.FetchedAtUtc >= T0);
    }

    // ---- membership mapping ----

    [Theory]
    [InlineData(200, "true", "plus", null)]
    [InlineData(200, "True", "plus", null)]
    [InlineData(200, " false\n", "none", null)]
    [InlineData(200, "", null, "membership-body")]
    [InlineData(200, "{\"isPremium\":true}", null, "membership-body")]
    [InlineData(404, null, null, "404")]
    [InlineData(500, "oops", null, "500")]
    public async Task Membership_MapsTheProbe(int status, string? body, string? expectedMembership, string? expectedError)
    {
        using var rig = await Rig.CreateAsync(1);
        rig.Api.MembershipStatus = status;
        rig.Api.MembershipBody = body;

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        var facts = Assert.Single(rig.Updates).Facts;
        Assert.Equal(expectedMembership, facts.Membership);
        Assert.Equal(expectedError, facts.LastError);
        Assert.Equal(100, facts.Robux);       // the other two facts are not poisoned by the gated endpoint
        Assert.Equal(2, facts.FriendsOnline);
        Assert.NotNull(facts.FetchedAtUtc);
    }

    // ---- membership is off until C1 measures it ----

    [Fact]
    public async Task Membership_IsOffByDefault()
    {
        using var rig = await Rig.CreateAsync(1);
        var service = new AccountFactsService(rig.Store, rig.Api, () => []);

        Assert.False(service.MembershipEnabled);
        Assert.True(rig.Service.MembershipEnabled); // the rig opts in so the mapping tests keep running
    }

    [Fact]
    public async Task WithMembershipOff_ARefreshIsTwoRequests_AndTheMembershipEndpointIsNeverCalled()
    {
        using var rig = await Rig.CreateAsync(2, membershipEnabled: false);
        // A dead endpoint: were it called, every refresh would carry "404".
        rig.Api.MembershipStatus = 404;
        rig.Api.MembershipBody = null;
        await rig.SeedAsync(0, new AccountFacts(50, "plus", 2, T0 - TimeSpan.FromMinutes(20), null));

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        Assert.True(rig.Service.Enqueue(rig.Ids[1]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 2);

        Assert.Equal(4, rig.Service.RequestCount); // two accounts, two requests each
        Assert.DoesNotContain(rig.Api.Snapshot(), c => c.Kind == "membership");
        Assert.All(rig.Updates, u =>
        {
            Assert.Null(u.Facts.Membership);  // stays null, even where a value was stored before
            Assert.Null(u.Facts.LastError);   // no membership error can ever be set
            Assert.Equal(100, u.Facts.Robux);
            Assert.Equal(2, u.Facts.FriendsOnline);
        });
    }

    [Fact]
    public async Task WithMembershipOn_ARefreshIsThreeRequests()
    {
        using var rig = await Rig.CreateAsync(1, membershipEnabled: true);

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        Assert.Equal(3, rig.Service.RequestCount);
        Assert.Equal(new[] { "robux", "friends", "membership" }, rig.Api.Snapshot().Select(c => c.Kind).ToArray());
    }

    // ---- accounts without a resolved user id ----

    [Fact]
    public async Task AnAccountWithoutARobloxUserId_GetsRobuxOnly()
    {
        using var rig = await Rig.CreateAsync(1, withUserIds: false);

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        Assert.Equal(new[] { "robux" }, rig.Api.Snapshot().Select(c => c.Kind).ToArray());
        var facts = Assert.Single(rig.Updates).Facts;
        Assert.Equal(100, facts.Robux);
        Assert.Null(facts.FriendsOnline);
        Assert.Null(facts.Membership);
    }

    // ---- resilience and separation ----

    [Fact]
    public async Task AThrowingSubscriber_DoesNotStopTheLane()
    {
        using var rig = await Rig.CreateAsync(2);
        rig.Service.AccountFactsUpdated += (_, _) => throw new InvalidOperationException("a bad subscriber");

        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        Assert.True(rig.Service.Enqueue(rig.Ids[1]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 2);

        Assert.Equal(6, rig.Service.RequestCount);
    }

    [Fact]
    public void TheService_HasNoReferenceToPresenceOrHealth_SoAFactsFailureCannotFlipOne()
    {
        // The facts lane records a reason and nothing else: PresenceService stays the only authority
        // on Expired / Limited. This pins the separation structurally (no constructor parameter and
        // no field of a presence type); the row-side half is pinned in
        // MainViewModelTests.ApplyFacts_WithAFailedRefresh_NeverTouchesTheRowsHealth.
        var ctor = Assert.Single(typeof(AccountFactsService).GetConstructors());
        var types = ctor.GetParameters().Select(p => p.ParameterType)
            .Concat(typeof(AccountFactsService).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.FieldType))
            .ToList();
        Assert.DoesNotContain(types, t => t.FullName!.Contains("Presence", StringComparison.Ordinal));

        var members = typeof(AccountFactsService).GetMembers(BindingFlags.Instance | BindingFlags.Public).Select(m => m.Name);
        Assert.DoesNotContain(members, n => n.Contains("SessionExpired", StringComparison.Ordinal) || n.Contains("SessionLimited", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoCookieValue_AppearsInAnyEventPayload()
    {
        using var rig = await Rig.CreateAsync(1);
        Assert.True(rig.Service.Enqueue(rig.Ids[0]));
        await rig.PumpUntilAsync(() => rig.Updates.Count == 1);

        var json = System.Text.Json.JsonSerializer.Serialize(Assert.Single(rig.Updates).Facts);
        Assert.DoesNotContain("FAKE-COOKIE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("FAKE-COOKIE", Assert.Single(rig.Updates).Facts.ToString(), StringComparison.Ordinal);
    }

    // =========================================================================================
    // rig
    // =========================================================================================

    private sealed class CountingClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private int _timers;
        public int TimersCreated => Volatile.Read(ref _timers);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _timers);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed record ApiCall(int Seq, string Label, string Kind, DateTimeOffset At);

    private sealed class ScriptedFactsApi(Func<DateTimeOffset> now, Dictionary<string, string> labelByCookie) : IAccountFactsApi
    {
        private readonly object _lock = new();
        private readonly List<ApiCall> _calls = [];

        public Func<string, string, Exception?>? Throw { get; set; }
        public Task? Gate { get; set; }
        public long Robux { get; set; } = 100;
        public int Friends { get; set; } = 2;
        public int MembershipStatus { get; set; } = 200;
        public string? MembershipBody { get; set; } = "false";

        public List<ApiCall> Snapshot() { lock (_lock) { return [.. _calls]; } }

        public async Task<long?> GetRobuxAsync(string cookie, CancellationToken ct)
        {
            await EnterAsync(cookie, "robux");
            return Robux;
        }

        public async Task<int?> GetFriendsOnlineCountAsync(string cookie, long userId, CancellationToken ct)
        {
            await EnterAsync(cookie, "friends");
            return Friends;
        }

        public async Task<MembershipProbe> GetMembershipAsync(string cookie, long userId, CancellationToken ct)
        {
            await EnterAsync(cookie, "membership");
            return new MembershipProbe(MembershipStatus, MembershipBody);
        }

        private async Task EnterAsync(string cookie, string kind)
        {
            var label = labelByCookie[cookie];
            lock (_lock) { _calls.Add(new ApiCall(_calls.Count, label, kind, now())); }
            await Task.Yield();
            if (Gate is { } gate) { await gate; }
            if (Throw?.Invoke(label, kind) is { } ex) { throw ex; }
        }
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _dir;
        private readonly object _lock = new();
        private readonly List<FactsTarget> _targets = [];

        public CountingClock Clock { get; } = new(T0);
        public AccountStore Store { get; }
        public ScriptedFactsApi Api { get; }
        public AccountFactsService Service { get; }
        public List<Guid> Ids { get; } = [];
        public List<AccountFactsUpdatedEventArgs> Updates { get; } = [];

        private Rig(string dir, int count, bool membershipEnabled)
        {
            _dir = dir;
            Store = new AccountStore(Path.Combine(dir, "accounts.dat"));
            var labels = new Dictionary<string, string>();
            for (var i = 0; i < count; i++) { labels[CookieFor(i)] = $"A{i}"; }
            Api = new ScriptedFactsApi(() => Clock.GetUtcNow(), labels);
            Service = new AccountFactsService(Store, Api, Snapshot, time: Clock, membershipEnabled: membershipEnabled);
            // What MainViewModel.ApplyFacts does to the live row, so the next decision sees fresh facts.
            Service.AccountFactsUpdated += (_, e) =>
            {
                lock (_lock)
                {
                    var i = _targets.FindIndex(t => t.Id == e.AccountId);
                    _targets[i] = _targets[i] with { Facts = e.Facts };
                    Updates.Add(e);
                }
            };
        }

        public static string CookieFor(int i) => $"FAKE-COOKIE-facts-{i}";

        // membershipEnabled defaults to TRUE here so the membership mapping tests keep covering the
        // three-call path; the production default is false (see the membership-off tests).
        public static async Task<Rig> CreateAsync(int count, bool withUserIds = true, bool membershipEnabled = true)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"ROROROblox-facts-svc-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            var rig = new Rig(dir, count, membershipEnabled);
            for (var i = 0; i < count; i++)
            {
                var account = await rig.Store.AddAsync($"Alt{i}", "", CookieFor(i));
                long? userId = withUserIds ? 1000 + i : null;
                if (userId is { } id) { await rig.Store.UpdateRobloxUserIdAsync(account.Id, id); }
                rig.Ids.Add(account.Id);
                rig._targets.Add(new FactsTarget(account.Id, userId, null, false));
            }
            return rig;
        }

        private IReadOnlyList<FactsTarget> Snapshot()
        {
            lock (_lock) { return [.. _targets]; }
        }

        public void SetAge(int index, TimeSpan age)
        {
            lock (_lock)
            {
                _targets[index] = _targets[index] with { Facts = new AccountFacts(10, "none", 1, Clock.GetUtcNow() - age, null) };
            }
        }

        public void SetExpired(int index)
        {
            lock (_lock) { _targets[index] = _targets[index] with { SessionExpired = true }; }
        }

        public async Task SeedAsync(int index, AccountFacts facts)
        {
            lock (_lock) { _targets[index] = _targets[index] with { Facts = facts }; }
            await Store.SetFactsAsync(Ids[index], facts);
        }

        /// <summary>Advance the fake clock a step at a time, giving the worker real time to run in between.</summary>
        public async Task PumpUntilAsync(Func<bool> done, TimeSpan? step = null)
        {
            var by = step ?? TimeSpan.FromMilliseconds(100);
            for (var i = 0; i < 2000; i++)
            {
                if (done()) { return; }
                Clock.Advance(by);
                await Task.Delay(3);
            }
            throw new TimeoutException("The facts lane did not reach the expected state.");
        }

        /// <summary>Wait in real time with the clock held still.</summary>
        public async Task WaitUntilAsync(Func<bool> done)
        {
            for (var i = 0; i < 1000; i++)
            {
                if (done()) { return; }
                await Task.Delay(10);
            }
            throw new TimeoutException("The facts lane did not reach the expected state.");
        }

        public void Dispose()
        {
            Service.Dispose();
            Store.Dispose();
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }
    }
}
