using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core.Feeds;
using ROROROblox.Core.Library;

namespace ROROROblox.Tests.Library;

/// <summary>
/// <see cref="GamePulseService"/> on a <see cref="FakeTimeProvider"/> with a counting fake API. The fake
/// completes synchronously, so a timer callback runs the whole pass on the advancing thread.
/// </summary>
public class GamePulseServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeApi : IGamePulseApi
    {
        public List<long[]> Requests { get; } = [];
        public int InFlight;
        public int MaxInFlight;
        public Func<long[], GamePulseRead>? Respond;
        public TaskCompletionSource? Gate;

        public async Task<GamePulseRead> ReadAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default)
        {
            var ids = universeIds.ToArray();
            Requests.Add(ids);
            var now = Interlocked.Increment(ref InFlight);
            MaxInFlight = Math.Max(MaxInFlight, now);
            try
            {
                if (Gate is { } gate)
                {
                    await gate.Task;
                }

                return Respond?.Invoke(ids) ?? Good(ids);
            }
            finally
            {
                Interlocked.Decrement(ref InFlight);
            }
        }

        public int Count => Requests.Count;
    }

    private static GamePulseRead Good(long[] ids, int playing = 100, DateTimeOffset? updated = null) =>
        new(FeedOutcome.Ok, new GamePulseAnswer(ids.ToDictionary(id => id, _ => new GamePulseReading(playing, updated ?? Start.AddDays(-2)))));

    private sealed class Rig
    {
        public FakeTimeProvider Clock { get; } = new(Start);
        public FakeApi Api { get; } = new();
        public GamePulseService Service { get; }
        public List<long[]> Changes { get; } = [];

        public Rig(bool allow = true)
        {
            Service = new GamePulseService(() => Api, Clock);
            Service.Changed += (_, e) => Changes.Add(e.UniverseIds.ToArray());
            if (allow)
            {
                Service.AllowReadsFrom(Start);
            }
        }

        public void Settle() => Clock.Advance(GamePulseService.Debounce);
    }

    private static long[] Ids(int from, int n) => Enumerable.Range(from, n).Select(i => (long)i).ToArray();

    [Fact]
    public void OneScreenful_IsOneRequestAfterTheDebounce()
    {
        var rig = new Rig();
        rig.Service.SetInterest(this, [1, 2, 3]);

        Assert.Equal(0, rig.Api.Count);
        rig.Settle();

        Assert.Equal(1, rig.Api.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, rig.Api.Requests[0]);
        Assert.Equal(100, rig.Service.TryGet(2)!.Value.Playing);
    }

    [Fact]
    public void OneTwentyIds_AreThreeRequestsOneAfterAnother_NeverInParallel()
    {
        var rig = new Rig();
        rig.Service.SetInterest(this, Ids(1, 120));

        rig.Settle();

        Assert.Equal(3, rig.Api.Count);
        Assert.Equal([50, 50, 20], rig.Api.Requests.Select(r => r.Length).ToArray());
        Assert.Equal(1, rig.Api.MaxInFlight);
    }

    [Fact]
    public void Chunks_WaitForTheOneBefore()
    {
        var rig = new Rig();
        rig.Api.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Service.SetInterest(this, Ids(1, 120));

        rig.Settle();

        Assert.Equal(1, rig.Api.Count);
        rig.Api.Gate.SetResult();
        // The first gate is open for every later chunk too, so the rest run now.
        SpinWait.SpinUntil(() => rig.Api.Count == 3, TimeSpan.FromSeconds(5));
        Assert.Equal(3, rig.Api.Count);
        Assert.Equal(1, rig.Api.MaxInFlight);
    }

    [Fact]
    public void NothingIsAsked_BeforeTheGateInstant()
    {
        var rig = new Rig(allow: false);
        rig.Service.AllowReadsFrom(Start.AddSeconds(30));
        rig.Service.SetInterest(this, [1]);

        rig.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, rig.Api.Count);

        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, rig.Api.Count);
    }

    [Fact]
    public void NothingIsEverAsked_WhenTheGateWasNeverSet()
    {
        var rig = new Rig(allow: false);
        rig.Service.SetInterest(this, [1, 2]);

        rig.Clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(0, rig.Api.Count);
        Assert.Null(rig.Service.TryGet(1));
    }

    [Fact]
    public void TenInterestChanges_InsideTheDebounce_AreOneRequest()
    {
        var rig = new Rig();
        for (var i = 1; i <= 10; i++)
        {
            rig.Service.SetInterest(this, Ids(1, i));
            rig.Clock.Advance(TimeSpan.FromMilliseconds(20));
        }

        Assert.Equal(0, rig.Api.Count);
        rig.Clock.Advance(GamePulseService.Debounce);

        Assert.Equal(1, rig.Api.Count);
        Assert.Equal(10, rig.Api.Requests[0].Length);
    }

    [Fact]
    public void AGame_IsNotAskedAgain_WithinFiveMinutesOfItsLastAttempt()
    {
        var rig = new Rig();
        rig.Service.SetInterest(this, [1]);
        rig.Settle();
        Assert.Equal(1, rig.Api.Count);

        rig.Service.SetInterest(this, [1, 2]);
        rig.Settle();
        Assert.Equal(2, rig.Api.Count);
        Assert.Equal(new long[] { 2 }, rig.Api.Requests[1]);

        rig.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(2, rig.Api.Count);

        rig.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(3, rig.Api.Count);
        Assert.Equal(new long[] { 1, 2 }, rig.Api.Requests[2]);
    }

    [Fact]
    public void AFailedAttempt_CountsAsTheAttempt()
    {
        var rig = new Rig();
        rig.Api.Respond = _ => new GamePulseRead(FeedOutcome.HttpError, null);
        rig.Service.SetInterest(this, [1]);
        rig.Settle();

        rig.Clock.Advance(TimeSpan.FromMinutes(4));

        Assert.Equal(1, rig.Api.Count);
        Assert.Null(rig.Service.TryGet(1));

        rig.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(2, rig.Api.Count);
    }

    [Fact]
    public void Refused_HoldsEveryReadForFiveMinutes()
    {
        var rig = new Rig();
        rig.Api.Respond = _ => new GamePulseRead(FeedOutcome.Refused, null);
        rig.Service.SetInterest(this, Ids(1, 120));
        rig.Settle();

        Assert.Equal(1, rig.Api.Count);

        // A new game is not asked either, until the hold is over.
        rig.Service.SetInterest(this, [500]);
        rig.Settle();
        rig.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(1, rig.Api.Count);

        rig.Api.Respond = null;
        rig.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(2, rig.Api.Count);
        Assert.Equal(new long[] { 500 }, rig.Api.Requests[1]);
    }

    [Fact]
    public void AtFifteenMinutes_TheCountReadsNull_TheDateStays_AndChangedIsRaised()
    {
        var rig = new Rig();
        rig.Api.Respond = ids => new(FeedOutcome.Ok, new GamePulseAnswer(new Dictionary<long, GamePulseReading>
        {
            [1] = new(500, Start.AddDays(-3)),
        }));
        rig.Service.SetInterest(this, [1]);
        rig.Settle();
        // After the first read the later refreshes fail, so the reading is only ever the first one.
        rig.Api.Respond = _ => new GamePulseRead(FeedOutcome.HttpError, null);
        rig.Changes.Clear();

        rig.Clock.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(500, rig.Service.TryGet(1)!.Value.Playing);
        Assert.Empty(rig.Changes);

        rig.Clock.Advance(TimeSpan.FromMinutes(1.1));

        var view = rig.Service.TryGet(1)!.Value;
        Assert.Null(view.Playing);
        Assert.Equal(Start.AddDays(-3), view.UpdatedUtc);
        Assert.Contains(rig.Changes, c => c.Contains(1L));
    }

    [Fact]
    public void AGameAbsentFromAGoodAnswer_HasNoView_AndChangedIsRaisedWhenItHadOne()
    {
        var rig = new Rig();
        rig.Service.SetInterest(this, [1, 2]);
        rig.Settle();
        Assert.NotNull(rig.Service.TryGet(2));
        rig.Changes.Clear();

        rig.Api.Respond = _ => new(FeedOutcome.Ok, new GamePulseAnswer(new Dictionary<long, GamePulseReading>
        {
            [1] = new(7, Start),
        }));
        rig.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.NotNull(rig.Service.TryGet(1));
        Assert.Null(rig.Service.TryGet(2));
        Assert.Contains(rig.Changes, c => c.Contains(2L));
    }

    [Fact]
    public void AShapeFailure_ChangesNoReading()
    {
        var rig = new Rig();
        rig.Service.SetInterest(this, [1]);
        rig.Settle();
        rig.Api.Respond = _ => new GamePulseRead(FeedOutcome.Shape, null);

        rig.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(2, rig.Api.Count);
        Assert.Equal(100, rig.Service.TryGet(1)!.Value.Playing);
    }

    [Fact]
    public void Interest_IsTheUnionAcrossOwners_AndAnEmptySetRemovesTheOwner()
    {
        var rig = new Rig();
        var a = new object();
        var b = new object();
        rig.Service.SetInterest(a, [1, 2]);
        rig.Service.SetInterest(b, [2, 3]);
        rig.Settle();

        Assert.Equal(new long[] { 1, 2, 3 }, rig.Api.Requests.Single());

        rig.Service.SetInterest(a, []);
        rig.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(new long[] { 2, 3 }, rig.Api.Requests[^1]);
    }

    [Fact]
    public void WithNoInterest_ThereIsNoTimer()
    {
        var clock = new TimerCountingClock(Start);
        var api = new FakeApi();
        using var service = new GamePulseService(() => api, clock);
        service.AllowReadsFrom(Start);
        var owner = new object();

        service.SetInterest(owner, [1]);
        Assert.True(clock.Active > 0);

        service.SetInterest(owner, []);
        Assert.Equal(0, clock.Active);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, api.Count);
    }

    [Fact]
    public void SetEnabledFalse_ClearsReadings_RaisesChanged_AsksNothing_AndKeepsTheInterest()
    {
        var rig = new Rig();
        rig.Service.SetInterest(this, [1, 2]);
        rig.Settle();
        rig.Changes.Clear();

        rig.Service.SetEnabled(false);

        Assert.Null(rig.Service.TryGet(1));
        Assert.Single(rig.Changes);
        Assert.Contains(1L, rig.Changes[0]);

        rig.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, rig.Api.Count);

        rig.Service.SetEnabled(true);
        Assert.Equal(1, rig.Api.Count);

        rig.Clock.Advance(GamePulseService.RefreshTick);

        Assert.Equal(2, rig.Api.Count);
        Assert.Equal(new long[] { 1, 2 }, rig.Api.Requests[1]);
        Assert.NotNull(rig.Service.TryGet(1));

        rig.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(2, rig.Api.Count);
    }

    [Fact]
    public void StartedDisabled_TwoHoursWithInterest_AsksNothing()
    {
        var rig = new Rig();
        rig.Service.SetEnabled(false);
        rig.Service.SetInterest(this, [1, 2]);

        rig.Clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(0, rig.Api.Count);
    }

    private sealed class TimerCountingClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private int _active;
        public int Active => Volatile.Read(ref _active);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _active);
            return new CountedTimer(base.CreateTimer(callback, state, dueTime, period), () => Interlocked.Decrement(ref _active));
        }

        private sealed class CountedTimer(ITimer inner, Action onDispose) : ITimer
        {
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    onDispose();
                }

                inner.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
