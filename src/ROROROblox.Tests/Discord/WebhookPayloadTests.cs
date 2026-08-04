using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class WebhookPayloadTests
{
    private static AlertTrigger Dropped(string name) =>
        new(AlertKind.AccountDroppedOut, Guid.NewGuid(), name, "Pet Simulator 99!", null,
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
