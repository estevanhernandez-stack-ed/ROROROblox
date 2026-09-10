using ROROROblox.Core.Discord;

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
}
