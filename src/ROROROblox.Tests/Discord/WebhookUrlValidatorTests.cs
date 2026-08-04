using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class WebhookUrlValidatorTests
{
    [Fact]
    public void Inspect_ValidWebhook_IsAcceptedAndNormalized()
    {
        var verdict = WebhookUrlValidator.Inspect("https://discord.com/api/webhooks/000000000000000000/abcDEF123");

        Assert.Equal(WebhookUrlKind.Valid, verdict.Kind);
        Assert.Equal("https://discord.com/api/webhooks/000000000000000000/abcDEF123", verdict.NormalizedUrl);
    }

    [Fact]
    public void Inspect_WebhookPastedInsideOtherText_IsExtracted()
    {
        // People paste with surrounding chat text. Rejecting that is a support ticket.
        var verdict = WebhookUrlValidator.Inspect(
            "here you go: https://discord.com/api/webhooks/123/tok  (from #alerts)");

        Assert.Equal(WebhookUrlKind.Valid, verdict.Kind);
        Assert.Equal("https://discord.com/api/webhooks/123/tok", verdict.NormalizedUrl);
    }

    [Fact]
    public void Inspect_DiscordappDotComVariant_IsStillAWebhook()
    {
        // Older webhooks were handed out on discordapp.com and they still work. Rejecting one
        // because the host reads differently would be a wrong answer stated confidently.
        var verdict = WebhookUrlValidator.Inspect("https://discordapp.com/api/webhooks/123/tok");

        Assert.Equal(WebhookUrlKind.Valid, verdict.Kind);
    }

    [Fact]
    public void Inspect_ServerInvite_SaysWhatItIsAndWhereToGetTheRealThing()
    {
        var verdict = WebhookUrlValidator.Inspect("https://discord.gg/abc123");

        Assert.Equal(WebhookUrlKind.ServerInvite, verdict.Kind);
        Assert.Contains("invite", verdict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Integrations", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_ChannelLink_IsDistinguishedFromAWebhook()
    {
        var verdict = WebhookUrlValidator.Inspect("https://discord.com/channels/123456/789012");

        Assert.Equal(WebhookUrlKind.ChannelLink, verdict.Kind);
        Assert.Contains("not a webhook", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_BotToken_WarnsRatherThanJustRejecting()
    {
        // A bot token in a paste field is a credential the user is about to leak. Say so.
        var verdict = WebhookUrlValidator.Inspect("EXAMPLE-NOT-A-REAL-TOKEN-00000.EXAMPL.EXAMPLE-NOT-A-REAL-TOKEN-00000");

        Assert.Equal(WebhookUrlKind.BotToken, verdict.Kind);
        Assert.Contains("don't share", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_ARejectedPaste_IsNeverEchoedBackInTheMessage()
    {
        // The message renders in Settings and gets screenshotted into a clan channel when someone
        // asks for help. Echoing the rejected paste would put a credential in that screenshot —
        // the bot-token case above is exactly the paste most worth NOT repeating.
        const string secretish = "EXAMPLE-NOT-A-REAL-TOKEN-11111.EXAMPL.EXAMPLE-NOT-A-REAL-TOKEN-11111";

        var verdict = WebhookUrlValidator.Inspect(secretish);

        Assert.DoesNotContain(secretish, verdict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(verdict.NormalizedUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Inspect_Empty_IsItsOwnQuietCase(string? input)
    {
        Assert.Equal(WebhookUrlKind.Empty, WebhookUrlValidator.Inspect(input).Kind);
    }

    [Fact]
    public void Inspect_Nonsense_IsUnrecognizedNotValid()
    {
        Assert.Equal(WebhookUrlKind.Unrecognized, WebhookUrlValidator.Inspect("banana").Kind);
    }
}
