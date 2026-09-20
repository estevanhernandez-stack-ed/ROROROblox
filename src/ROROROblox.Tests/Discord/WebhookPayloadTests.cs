using System.Globalization;
using System.Text;
using ROROROblox.App.Notify;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Discord;

public class WebhookPayloadTests
{
    private static AlertTrigger Dropped(string name) =>
        new(AlertKind.AccountDroppedOut, Guid.NewGuid(), name, $"real_{name}", "Pet Simulator 99!", null,
            new DateTimeOffset(2026, 8, 3, 3, 14, 0, TimeSpan.Zero));

    [Fact]
    public void ForAlert_SingleDroppedAccount_NamesItAndTheGame()
    {
        var payload = WebhookPayload.ForAlert(AlertKind.AccountDroppedOut, [Dropped("BaronBloxwell")]);

        Assert.Contains("BaronBloxwell", payload.Body, StringComparison.Ordinal);
        Assert.Contains("Pet Simulator 99!", payload.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ForAlert_ManyAccountsAtOnce_IsOneMessageNotSeveral()
    {
        // Eight accounts crossing a threshold in one watchdog sweep is one buzz, not eight.
        var payload = WebhookPayload.ForAlert(AlertKind.MemoryWarning,
            [Dropped("A"), Dropped("B"), Dropped("C")]);

        Assert.Contains("3 accounts", payload.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A", payload.Body, StringComparison.Ordinal);
        Assert.Contains("C", payload.Body, StringComparison.Ordinal);
    }

    private static AlertTrigger Breach(string name, double? value) =>
        new(AlertKind.MetricBreach, Guid.NewGuid(), name, $"real_{name}", "battle.points", null,
            new DateTimeOffset(2026, 9, 9, 3, 14, 0, TimeSpan.Zero), value);

    [Fact]
    public void ForAlert_MetricBreach_RendersTheObservedValueWithoutFlatteningIt()
    {
        // No Rule on this trigger: this pins the 1.28 rendering a rule-less trigger keeps (2026-09-15).
        // The fractional value is the point. This line carried a long? until 2026-09-09, so a
        // Level rule on a 0.0-1.0 ratio read "at 0" for 0.79 and for a genuine zero alike — the
        // unknown-is-not-zero conflation the metric core defends against, arriving at the last
        // step instead. Unit-free by design: Level and Event rules raise this same kind, so
        // "per minute" would be wrong for two of the three.
        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Breach("BaronBloxwell", 0.79)]);

        Assert.Equal("• BaronBloxwell — battle.points at 0.79", payload.Body);
    }

    [Fact]
    public void ForAlert_MetricBreachWithNoValue_FallsThroughToTheGenericLine()
    {
        // No reading is not a reading of zero, here too: with nothing to report the line names the
        // account and the metric and claims no number at all.
        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Breach("BaronBloxwell", null)]);

        Assert.Equal("• BaronBloxwell — battle.points", payload.Body);
    }

    [Fact]
    public void WebhookPayload_HasNoFieldThatCouldCarryAServerLink()
    {
        // THE test for this task, and it is a design assertion rather than a behavior one: the
        // type is the boundary. A presence Join secret reaches people who can see your Join
        // button; a channel post reaches everyone who ever reads that channel, including people
        // who join it next year. Adding a Url/Link/Code property here makes this fail.
        var properties = typeof(WebhookPayload).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(["Body", "Title"], properties.Order().ToArray());
        Assert.All(properties, p =>
        {
            Assert.DoesNotContain("url", p, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("link", p, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("code", p, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static readonly DateTimeOffset At = new(2026, 9, 15, 3, 14, 0, TimeSpan.Zero);

    private static AlertTrigger Fired(MetricRule rule, string name, double? value) =>
        new(AlertKind.MetricBreach, Guid.NewGuid(), name, $"real_{name}", rule.MetricId, null, At, value, rule);

    private static readonly MetricRule DiamondsAbove =
        new("ps99.diamonds", MetricRuleKind.Level, 0, TimeSpan.Zero, AlertWhenBelow: false, Label: "Diamonds");

    /// <summary>
    /// A metric that belongs to no account. Ur Score's clan-and-field numbers report with
    /// Guid.Empty as the subject — the documented global carrier — and ResolveAlertNames answers
    /// an unmatched id with two empty strings, so the noun reaching this code is "". Every trigger
    /// in this file was built with a real name, so the suite was green while the first alert a
    /// clan would ever receive rendered as " — Clan points went above 9,000,000,000" over a body
    /// of "•  — now 9,040,000,000". With no noun the label IS the subject and the dash goes.
    /// </summary>
    [Fact]
    public void AMetricWithNoAccountLeadsWithItsLabel()
    {
        var rule = new MetricRule("clan.standing.points", MetricRuleKind.Level, 9_000_000_000, TimeSpan.Zero,
            AlertWhenBelow: false, Label: "K0i2 clan points");

        var payload = WebhookPayload.ForAlert(
            AlertKind.MetricBreach,
            [new AlertTrigger(AlertKind.MetricBreach, Guid.Empty, "", "", rule.MetricId, null, At, 9_040_000_000, rule)]);

        Assert.Equal("K0i2 clan points went above 9,000,000,000", payload.Title);
        Assert.Equal("• now 9,040,000,000", payload.Body);
        Assert.DoesNotContain(" — ", payload.Title, StringComparison.Ordinal);
    }

    /// <summary>The same, for a rate rule and for a breach that carries no value at all.</summary>
    [Fact]
    public void AnAccountLessRateAndAValuelessBreachAlsoLeadWithTheLabel()
    {
        var rate = new MetricRule("clan.standing.points", MetricRuleKind.Rate, 5_000_000, TimeSpan.FromMinutes(15),
            AlertWhenBelow: true, Label: "K0i2 clan points");

        var withRate = WebhookPayload.ForAlert(
            AlertKind.MetricBreach,
            [new AlertTrigger(AlertKind.MetricBreach, Guid.Empty, "", "", rate.MetricId, null, At, 1_200_000, rate)]);

        Assert.Equal("K0i2 clan points stopped climbing", withRate.Title);
        Assert.Equal("• 1,200,000 a minute over 15 min (alert under 5,000,000)", withRate.Body);

        var none = WebhookPayload.ForAlert(
            AlertKind.MetricBreach,
            [new AlertTrigger(AlertKind.MetricBreach, Guid.Empty, "", "", rate.MetricId, null, At, null, rate)]);

        Assert.Equal("• K0i2 clan points", none.Body);
    }

    /// <summary>An account-backed alert is untouched: it still leads with the alt's name.</summary>
    [Fact]
    public void AnAccountsAlertStillLeadsWithTheAccount()
    {
        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(DiamondsAbove, "CElCPapa", 12)]);

        Assert.StartsWith("CElCPapa — ", payload.Title, StringComparison.Ordinal);
        Assert.StartsWith("• CElCPapa — ", payload.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLiveTestCase_ReadsAsASentenceWithSeparators()
    {
        // 2026-09-15, RoRoRo 1.28: this exact rule and value posted "CElCPapa — ps99.diamonds" /
        // "• CElCPapa — ps99.diamonds at 2974993".
        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(DiamondsAbove, "CElCPapa", 2974993)]);

        Assert.Equal("CElCPapa — Diamonds went above 0", payload.Title);
        Assert.Equal("• CElCPapa — now 2,974,993", payload.Body);
    }

    [Fact]
    public void Rate_SaysStoppedClimbing_WithTheRateTheWindowAndTheFloor()
    {
        var rule = new MetricRule("battle.points", MetricRuleKind.Rate, 5000, TimeSpan.FromMinutes(10), Label: "Points");

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "CElCPapa", 1234.5)]);

        Assert.Equal("CElCPapa — Points stopped climbing", payload.Title);
        Assert.Equal("• CElCPapa — 1,234.5 a minute over 10 min (alert under 5,000)", payload.Body);
    }

    [Fact]
    public void LevelBelow_SaysFellBelow_AndKeepsTheFraction()
    {
        var rule = new MetricRule("battle.share", MetricRuleKind.Level, 0.8, TimeSpan.Zero, Label: "Share");

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "BaronBloxwell", 0.79)]);

        Assert.Equal("BaronBloxwell — Share fell below 0.8", payload.Title);
        Assert.Equal("• BaronBloxwell — now 0.79", payload.Body);
    }

    [Fact]
    public void Event_SaysChanged()
    {
        var rule = new MetricRule("battle.place", MetricRuleKind.Event, 0, TimeSpan.Zero, Label: "Place");

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "BaronBloxwell", 3)]);

        Assert.Equal("BaronBloxwell — Place changed", payload.Title);
        Assert.Equal("• BaronBloxwell — now 3", payload.Body);
    }

    [Fact]
    public void NoLabel_FallsBackToTheMetricId()
    {
        var rule = DiamondsAbove with { Label = null };

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "CElCPapa", 5)]);

        Assert.Equal("CElCPapa — ps99.diamonds went above 0", payload.Title);
    }

    [Fact]
    public void EightAccounts_AreOneTitleAndOneLineEach()
    {
        var triggers = Enumerable.Range(1, 8).Select(i => Fired(DiamondsAbove, $"Alt{i}", 1000 * i)).ToList();

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, triggers);

        Assert.Equal("8 accounts — Diamonds went above 0", payload.Title);
        var lines = payload.Body.Split('\n');
        Assert.Equal(8, lines.Length);
        Assert.Equal("• Alt1 — now 1,000", lines[0]);
        Assert.Equal("• Alt8 — now 8,000", lines[7]);
    }

    [Fact]
    public void StreamerMasking_IsUnchanged_OnlyTheClanRoomGetsRealNames()
    {
        var trigger = Fired(DiamondsAbove, "DoctorDuck", 5);

        var masked = WebhookPayload.ForAlert(AlertKind.MetricBreach, [trigger]);
        var clan = WebhookPayload.ForAlert(AlertKind.MetricBreach, [trigger], useRealNames: true);

        Assert.Equal("DoctorDuck — Diamonds went above 0", masked.Title);
        Assert.DoesNotContain("real_", masked.Body, StringComparison.Ordinal);
        Assert.Equal("real_DoctorDuck — Diamonds went above 0", clan.Title);
        Assert.Equal("• real_DoctorDuck — now 5", clan.Body);
    }

    [Fact]
    public void AThreshold_PrintsAsWritten_NotRoundedToTwoPlaces()
    {
        var rule = new MetricRule("battle.share", MetricRuleKind.Level, 0.125, TimeSpan.Zero, Label: "Share");

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "A", 0.1)]);

        Assert.Equal("A — Share fell below 0.125", payload.Title);
    }

    [Fact]
    public void Numbers_DoNotFollowTheMachineCulture()
    {
        // The sentence around the number is English, so the number is too: "2.974.993,5" in a German
        // locale would sit inside "now ..." and read as nonsense.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(DiamondsAbove, "A", 2974993.5)]);
            Assert.Equal("• A — now 2,974,993.5", payload.Body);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ARuleBreachWithNoValue_ClaimsNoNumber()
    {
        // Unreachable from the evaluator (an unmeasurable rate is never a breach), but no reading is
        // not a reading of zero, here too.
        var rule = new MetricRule("battle.points", MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10), Label: "Points");

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "A", null)]);

        Assert.Equal("• A", payload.Body);
    }

    // ── Length caps (controller ruling C2, 2026-09-15) ──────────────────────────────────────────
    // The default envelope feeds every REMOTE destination (the desktop toast has its own since the
    // final review, corrected 2026-09-15, below), so it has to fit the tightest envelope of each: the
    // Discord message is "**{Title}**\n{Body}" in at most 2000 characters; Pushover takes a title of
    // at most 250 and a message of at most 1024, and its own truncation must never re-cut ours (it
    // would count the "and N more" trailer as an account); ntfy's body is "{Title}\n{Body}" in at
    // most 4096 UTF-8 bytes.

    private static void AssertFitsEveryDestination(WebhookPayload payload)
    {
        var discord = $"**{payload.Title}**\n{payload.Body}";
        Assert.True(discord.Length <= 2000, $"Discord message is {discord.Length} characters");
        Assert.True(payload.Title.Length <= 250, $"Pushover title is {payload.Title.Length} characters");
        Assert.True(payload.Body.Length <= 992, $"Pushover message is {payload.Body.Length} characters");
        Assert.Equal(payload.Body, PushoverSender.TruncateForPushover(payload.Body));
        var ntfy = Encoding.UTF8.GetByteCount($"{payload.Title}\n{payload.Body}");
        Assert.True(ntfy <= 4096, $"ntfy body is {ntfy} bytes");
    }

    [Fact]
    public void AHugeGroup_FitsEveryDestination_AndSaysHowManyMore()
    {
        var rule = new MetricRule("battle.points", MetricRuleKind.Rate, 5000, TimeSpan.FromMinutes(10), Label: "Points");
        var triggers = Enumerable.Range(1, 200).Select(i => Fired(rule, $"Alt{i:D3}", 1234.5)).ToList();

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, triggers);

        AssertFitsEveryDestination(payload);
        Assert.Equal("200 accounts — Points stopped climbing", payload.Title);
        var lines = payload.Body.Split('\n');
        var shown = lines.Length - 1;
        Assert.True(shown > 0);
        for (var i = 0; i < shown; i++)
        {
            Assert.Equal($"• Alt{i + 1:D3} — 1,234.5 a minute over 10 min (alert under 5,000)", lines[i]);
        }
        Assert.Equal($"and {200 - shown} more", lines[^1]);
    }

    [Fact]
    public void AGroupThatFits_HasNoTrailer()
    {
        var triggers = Enumerable.Range(1, 40).Select(i => Fired(DiamondsAbove, $"Alt{i:D2}", 1000)).ToList();

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, triggers);

        AssertFitsEveryDestination(payload);
        var lines = payload.Body.Split('\n');
        Assert.Equal(40, lines.Length);
        Assert.Equal("• Alt40 — now 1,000", lines[^1]);
        Assert.DoesNotContain("more", payload.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void AMassDropOut_IsCappedToo()
    {
        // Not only metric groups: a hundred accounts dropping in one sweep overflowed a Discord post
        // through 1.28 while the toast and the phone still arrived.
        var triggers = Enumerable.Range(1, 100).Select(i => Dropped($"Account{i:D3}")).ToList();

        var payload = WebhookPayload.ForAlert(AlertKind.AccountDroppedOut, triggers);

        AssertFitsEveryDestination(payload);
        var lines = payload.Body.Split('\n');
        Assert.Equal("• Account001 — Pet Simulator 99!", lines[0]);
        Assert.Equal($"and {100 - (lines.Length - 1)} more", lines[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AVeryLongMetricIdOrLabel_StillFitsEveryDestination(bool longLabel)
    {
        // The label is normalised to 40 characters by the rule source, but the metric id is
        // plugin-supplied and unbounded, and a rule without a label puts it in the title.
        var huge = new string('m', 5000);
        var rule = longLabel
            ? new MetricRule("battle.points", MetricRuleKind.Level, 0, TimeSpan.Zero, AlertWhenBelow: false, Label: huge)
            : new MetricRule(huge, MetricRuleKind.Level, 0, TimeSpan.Zero, AlertWhenBelow: false);
        var triggers = Enumerable.Range(1, 50).Select(i => Fired(rule, $"Alt{i:D2}", 5)).ToList();

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, triggers);

        AssertFitsEveryDestination(payload);
        Assert.StartsWith("50 accounts — mmm", payload.Title, StringComparison.Ordinal);
        // Was EndsWith("…") until the title cut learned to keep the wording's verb (final-review
        // Important 1, 2026-09-15): the long part is cut, the verb and threshold stay.
        Assert.EndsWith("… went above 0", payload.Title, StringComparison.Ordinal);
        Assert.StartsWith("• Alt01 — now 5", payload.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleMonsterLine_IsCutRatherThanDropped()
    {
        // One account whose (masked) name is enormous: no whole line fits, so the front of it stays
        // and there is no "and 0 more".
        var payload = WebhookPayload.ForAlert(AlertKind.AccountDroppedOut, [Dropped(new string('x', 3000))]);

        AssertFitsEveryDestination(payload);
        Assert.StartsWith("• xxx", payload.Body, StringComparison.Ordinal);
        Assert.EndsWith("…", payload.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("more", payload.Body, StringComparison.Ordinal);
    }

    // ── The desktop toast's own envelope (final-review Important 1, 2026-09-15) ────────────────
    // The tray balloon marshals its title into 64 UTF-16 units and its text into 256, each with a
    // terminator, and Windows cuts anything longer mid-line with no marker. So Local gets 63/255 and
    // the same "and N more" logic; every other destination keeps 250/992.

    private static readonly PayloadLimits Toast = PayloadLimits.For(AlertDestination.Local);

    private static readonly MetricRule PointsRate =
        new("battle.points", MetricRuleKind.Rate, 5000, TimeSpan.FromMinutes(10), Label: "Points");

    private static void AssertFitsTheBalloon(WebhookPayload payload)
    {
        Assert.True(payload.Title.Length <= 63, $"toast title is {payload.Title.Length} characters");
        Assert.True(payload.Body.Length <= 255, $"toast text is {payload.Body.Length} characters");
    }

    [Theory]
    [InlineData(AlertDestination.Local, 63, 255)]
    [InlineData(AlertDestination.Mine, 250, 992)]
    [InlineData(AlertDestination.Clan, 250, 992)]
    [InlineData(AlertDestination.Phone, 250, 992)]
    public void OnlyTheDesktopToast_GetsTheBalloonsLimits(AlertDestination destination, int title, int body)
    {
        Assert.Equal(new PayloadLimits(title, body), PayloadLimits.For(destination));
    }

    [Fact]
    public void ForTheToast_TheOwnersEightAccountRateGroup_NamesWhatFits_AndSaysHowManyMore()
    {
        // The review's case: eight 8-character names, each Rate line 61 characters. The 992 body
        // named all eight and the balloon then cut it inside the fifth line, silently.
        var triggers = Enumerable.Range(1, 8).Select(i => Fired(PointsRate, $"CElCPap{i}", 1234.5)).ToList();

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, triggers, limits: Toast);

        AssertFitsTheBalloon(payload);
        Assert.Equal("8 accounts — Points stopped climbing", payload.Title);
        var lines = payload.Body.Split('\n');
        Assert.Equal(4, lines.Length);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal($"• CElCPap{i + 1} — 1,234.5 a minute over 10 min (alert under 5,000)", lines[i]);
        }
        Assert.Equal("and 5 more", lines[^1]);

        // As many as fit: a fourth whole line plus its own trailer would not.
        Assert.True(string.Join("\n", lines[..3]).Length + 1 + lines[0].Length + "\nand 4 more".Length > 255);

        // The same group for a webhook still names all eight.
        Assert.Equal(8, WebhookPayload.ForAlert(AlertKind.MetricBreach, triggers).Body.Split('\n').Length);
    }

    [Fact]
    public void ForTheToast_ALongLabel_IsCut_AndTheVerbAndThresholdStay()
    {
        // 40 is the rule source's label cap. Front-cut at 63, this title lost "went above 1,000,000".
        var rule = new MetricRule("ps99.diamonds", MetricRuleKind.Level, 1_000_000, TimeSpan.Zero,
            AlertWhenBelow: false, Label: "Diamonds collected in every world so far");
        var name = "BaronBloxwellTheBold";   // 20, Roblox's longest username

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, name, 2_000_000)], limits: Toast);

        AssertFitsTheBalloon(payload);
        Assert.Equal("BaronBloxwellTheBold — Diamonds collected… went above 1,000,000", payload.Title);
        Assert.Equal(63, payload.Title.Length);
        Assert.Equal("• BaronBloxwellTheBold — now 2,000,000", payload.Body);
    }

    [Fact]
    public void ForTheToast_AThresholdTooLongToKeepWhole_FallsBackToCuttingTheEnd()
    {
        // The verb and threshold keep their place only while they take at most half the title;
        // past that the name and label matter more, so the title's front is kept instead.
        var rule = DiamondsAbove with { Threshold = 1e40 };

        var payload = WebhookPayload.ForAlert(AlertKind.MetricBreach, [Fired(rule, "CElCPapa", 5)], limits: Toast);

        AssertFitsTheBalloon(payload);
        Assert.Equal(63, payload.Title.Length);
        Assert.StartsWith("CElCPapa — Diamonds went above 10,000,", payload.Title, StringComparison.Ordinal);
        Assert.EndsWith("…", payload.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void ForTheToast_ASingleAccountThatFits_IsUnchanged()
    {
        var trigger = Fired(DiamondsAbove, "CElCPapa", 2974993);

        var toast = WebhookPayload.ForAlert(AlertKind.MetricBreach, [trigger], limits: Toast);

        Assert.Equal(WebhookPayload.ForAlert(AlertKind.MetricBreach, [trigger]), toast);
        Assert.Equal("CElCPapa — Diamonds went above 0", toast.Title);
        Assert.Equal("• CElCPapa — now 2,974,993", toast.Body);
    }

    [Fact]
    public void ForTheToast_AMassDropOut_IsCappedToo()
    {
        var triggers = Enumerable.Range(1, 30).Select(i => Dropped($"Account{i:D3}")).ToList();

        var payload = WebhookPayload.ForAlert(AlertKind.AccountDroppedOut, triggers, limits: Toast);

        AssertFitsTheBalloon(payload);
        Assert.Equal("30 accounts dropped out", payload.Title);
        var lines = payload.Body.Split('\n');
        Assert.Equal("• Account001 — Pet Simulator 99!", lines[0]);
        Assert.Equal($"and {30 - (lines.Length - 1)} more", lines[^1]);
    }

    [Fact]
    public void ForTheToast_AnEnormousName_CutsTheNameAndKeepsTheWording()
    {
        var payload = WebhookPayload.ForAlert(AlertKind.AccountDroppedOut, [Dropped(new string('x', 300))], limits: Toast);

        AssertFitsTheBalloon(payload);
        Assert.Equal(new string('x', 50) + "… dropped out", payload.Title);
        Assert.StartsWith("• xxx", payload.Body, StringComparison.Ordinal);
        Assert.EndsWith("…", payload.Body, StringComparison.Ordinal);
    }
}
