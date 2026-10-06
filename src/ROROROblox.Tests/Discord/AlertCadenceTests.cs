using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// The cadence half of v1.33 item 2: the five-minute cooldown that was a <c>static readonly</c> in
/// <c>AlertRouter</c> from v1.0 to v1.32 is now <c>AlertCadenceMinutes</c> plus a per-kind override
/// map, and the router is handed the resolved value instead of owning it.
/// <para>
/// The boundary itself (<c>&gt;=</c>, both directions) is pinned next door in
/// <c>AlertRouterTests</c>, where the rest of the cooldown cases already live. This file is about
/// where the number comes from.
/// </para>
/// </summary>
public class AlertCadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 23, 33, 3, TimeSpan.Zero);
    private static readonly Guid AccountA = Guid.NewGuid();

    private static AlertTrigger Trigger(AlertKind kind, Guid id, string name) =>
        new(kind, id, name, $"real_{name}", "Pet Simulator 99!", 4_000_000_000, Now);

    private static Dictionary<AlertCooldownKey, DateTimeOffset> SentAt(AlertKind kind, DateTimeOffset when) =>
        new() { [AlertCooldownKey.For(Trigger(kind, AccountA, "A"))] = when };

    // ---- Resolution -------------------------------------------------------------------------

    [Fact]
    public void FromSettings_TheShippedDefault_IsStillFiveMinutes()
    {
        // An upgrade changes nobody's pace. 5 is the number AlertRouter.Cooldown held through
        // v1.32, and SettingsBlob's AlertCadenceMinutes default has to agree with it.
        var cadence = AlertCadence.FromSettings(5, "");

        Assert.Equal(TimeSpan.FromMinutes(5), cadence.Global);
        Assert.Equal(TimeSpan.FromMinutes(5), cadence.For(AlertKind.MemoryWarning));
    }

    [Fact]
    public void FromSettings_Zero_MeansEveryTime()
    {
        // 0 as a deliberate off, matching MemoryCapMb's idiom in the same record rather than
        // adding a nullable. "Every time" has to be a real zero quiet period, not a very small one.
        Assert.Equal(TimeSpan.Zero, AlertCadence.FromSettings(0, "").For(AlertKind.AccountDroppedOut));
    }

    [Fact]
    public void FromSettings_AnOverride_AppliesToItsKindOnly()
    {
        var cadence = AlertCadence.FromSettings(5, """{"MemoryWarning":30}""");

        Assert.Equal(TimeSpan.FromMinutes(30), cadence.For(AlertKind.MemoryWarning));
        Assert.Equal(TimeSpan.FromMinutes(5), cadence.For(AlertKind.AccountDroppedOut));
        Assert.Equal(TimeSpan.FromMinutes(5), cadence.Global);
    }

    [Fact]
    public void FromSettings_AnOverrideOfZero_MeansEveryTimeForThatKindAlone()
    {
        // The override map inherits the global's idiom rather than inventing a second one: 0 in a
        // row means that row speaks every time, with the rest of the table unchanged.
        var cadence = AlertCadence.FromSettings(5, """{"AccountDroppedOut":0}""");

        Assert.Equal(TimeSpan.Zero, cadence.For(AlertKind.AccountDroppedOut));
        Assert.Equal(TimeSpan.FromMinutes(5), cadence.For(AlertKind.MemoryWarning));
    }

    [Theory]
    // Not JSON at all — a truncated write, or a file edited by hand and left mid-sentence.
    [InlineData("{\"MemoryWarning\":")]
    [InlineData("not json")]
    // JSON, but not the shape: an array, a bare number, a string where the map goes.
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    [InlineData("\"MemoryWarning\"")]
    // The right shape with wrong-typed values.
    [InlineData("""{"MemoryWarning":"thirty"}""")]
    [InlineData("""{"MemoryWarning":null}""")]
    // Names that are not kinds, including an ordinal — the map is keyed by NAME, and accepting
    // "1" would quietly re-introduce the positional coupling AlertKind's doc comment refuses.
    [InlineData("""{"NotAKind":30}""")]
    [InlineData("""{"1":30}""")]
    [InlineData("""{"":30}""")]
    // A negative minute count is not a user choice; it is a corrupt file, and "every time" is the
    // wrong direction to guess in.
    [InlineData("""{"MemoryWarning":-30}""")]
    public void FromSettings_ACorruptOrNonsenseOverridesBlob_FallsBackToTheGlobal(string overridesJson)
    {
        // "Absent or unparseable means follow the global" — and the whole point is that it degrades
        // rather than throws. A settings file nobody can parse must not be able to stop alerting.
        var cadence = AlertCadence.FromSettings(5, overridesJson);

        Assert.Equal(TimeSpan.FromMinutes(5), cadence.For(AlertKind.MemoryWarning));
        Assert.Equal(TimeSpan.FromMinutes(5), cadence.For(AlertKind.AccountDroppedOut));
    }

    [Fact]
    public void FromSettings_OneBadEntryBesideAGoodOne_DropsOnlyTheBadOne()
    {
        // Per-entry, not all-or-nothing: a hand-edited file with one typo should not discard the
        // row the user got right.
        var cadence = AlertCadence.FromSettings(5, """{"NotAKind":1,"MemoryWarning":30}""");

        Assert.Equal(TimeSpan.FromMinutes(30), cadence.For(AlertKind.MemoryWarning));
        Assert.Equal(TimeSpan.FromMinutes(5), cadence.For(AlertKind.AccountDroppedOut));
    }

    [Fact]
    public void FromSettings_ANegativeGlobal_FallsBackToTheShippedFiveMinutes()
    {
        // Also not a user choice. Reading it as "every time" would turn a corrupt file into the
        // loudest possible setting, which is the one direction a degraded read must not take.
        Assert.Equal(TimeSpan.FromMinutes(5), AlertCadence.FromSettings(-1, "").Global);
    }

    [Fact]
    public void FromSettings_NoOverridesAtAll_IsTheGlobalForEveryKind()
    {
        foreach (var json in new string?[] { null, "", "   ", "{}" })
        {
            var cadence = AlertCadence.FromSettings(7, json);
            foreach (var kind in Enum.GetValues<AlertKind>())
            {
                Assert.Equal(TimeSpan.FromMinutes(7), cadence.For(kind));
            }
        }
    }

    // ---- The router honours it --------------------------------------------------------------

    [Fact]
    public void Route_WithCadenceZero_LetsEveryRepeatThrough()
    {
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Local };
        var oneSecondAgo = SentAt(AlertKind.AccountDroppedOut, Now.AddSeconds(-1));

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")], config, oneSecondAgo, Now,
            cadence: AlertCadence.FromSettings(0, ""));

        Assert.Single(routed);
    }

    [Fact]
    public void Route_WithAPerKindOverride_DoesNotChangeAnotherKindsPace()
    {
        // Memory warnings slowed to 30 minutes, drops left at the global 5. Twenty minutes after
        // both last spoke, the drop speaks and the memory warning does not.
        var config = new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Local,
            MemoryWarningDestination = AlertDestination.Local,
        };
        var twentyMinutesAgo = Now.AddMinutes(-20);
        var lastSent = new Dictionary<AlertCooldownKey, DateTimeOffset>
        {
            [AlertCooldownKey.For(Trigger(AlertKind.AccountDroppedOut, AccountA, "A"))] = twentyMinutesAgo,
            [AlertCooldownKey.For(Trigger(AlertKind.MemoryWarning, AccountA, "A"))] = twentyMinutesAgo,
        };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A"),
             Trigger(AlertKind.MemoryWarning, AccountA, "A")],
            config, lastSent, Now,
            cadence: AlertCadence.FromSettings(5, """{"MemoryWarning":30}"""));

        Assert.Equal(AlertKind.AccountDroppedOut, Assert.Single(routed).Kind);
    }

    [Fact]
    public void Route_CadenceIsCheckedOncePerEvent_NotOncePerDestination()
    {
        // The PRD criterion, pinned rather than merely true. It holds today because the cooldown
        // clause runs per trigger and the GroupBy/SelectMany below it is what fans a kind out
        // across destinations — so the check sits upstream of the multiplication. A refactor that
        // moved it into the fan-out would read the same, behave identically on a one-destination
        // config, and triple the consults the moment anyone ticked a second destination.
        //
        // Counting the dictionary reads is the only way to see that from outside: the routed
        // output is identical either way.
        var config = new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MineWebhookUrl = "https://discord.com/api/webhooks/1/tok",
            ClanWebhookUrl = "https://discord.com/api/webhooks/2/tok",
        };
        var counting = new CountingLastSent(SentAt(AlertKind.AccountDroppedOut, Now.AddHours(-1)));

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")], config, counting, Now,
            phoneConfigured: true, cadence: AlertCadence.FromSettings(5, ""));

        Assert.Single(routed);
        Assert.Equal(1, counting.Lookups);
    }

    [Fact]
    public void Route_CadenceIsCheckedOncePerEvent_EvenWhenTheKindFansOutToThreeDestinations()
    {
        // Same claim with the fan-out actually exercised, so the test above cannot pass by the
        // destination list happening to be one long.
        var config = new DiscordConfig
        {
            DroppedOutDestinations = [AlertDestination.Local, AlertDestination.Mine, AlertDestination.Phone],
            MineWebhookUrl = "https://discord.com/api/webhooks/1/tok",
        };
        var counting = new CountingLastSent(SentAt(AlertKind.AccountDroppedOut, Now.AddHours(-1)));

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")], config, counting, Now,
            phoneConfigured: true, cadence: AlertCadence.FromSettings(5, ""));

        Assert.Equal(3, routed.Count);
        Assert.Equal(1, counting.Lookups);
    }

    /// <summary>
    /// A last-sent map that counts how many times the router asked it. The router takes an
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/>, so a wrapper is enough — no seam to add.
    /// </summary>
    private sealed class CountingLastSent(IReadOnlyDictionary<AlertCooldownKey, DateTimeOffset> inner)
        : IReadOnlyDictionary<AlertCooldownKey, DateTimeOffset>
    {
        public int Lookups { get; private set; }

        public bool TryGetValue(AlertCooldownKey key, out DateTimeOffset value)
        {
            Lookups++;
            return inner.TryGetValue(key, out value);
        }

        public DateTimeOffset this[AlertCooldownKey key]
        {
            get { Lookups++; return inner[key]; }
        }

        public bool ContainsKey(AlertCooldownKey key) { Lookups++; return inner.ContainsKey(key); }

        public IEnumerable<AlertCooldownKey> Keys => inner.Keys;

        public IEnumerable<DateTimeOffset> Values => inner.Values;

        public int Count => inner.Count;

        public IEnumerator<KeyValuePair<AlertCooldownKey, DateTimeOffset>> GetEnumerator() => inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
