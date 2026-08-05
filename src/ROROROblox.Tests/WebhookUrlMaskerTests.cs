using ROROROblox.Core.Discord;

namespace ROROROblox.Tests;

/// <summary>
/// F-076. A Discord webhook URL is a bearer credential — the token segment is the entire
/// authentication. These tests pin the one property that matters: the token never survives masking.
/// </summary>
public class WebhookUrlMaskerTests
{
    // Shaped like a real one, invented. Never put a live webhook in a test file.
    private const string RealShape =
        "https://discord.com/api/webhooks/1534061441160712242/7H-gCzcgningFW7XtYi40K9Gu7IyIHtLzeuc-hrIpnUBeqXP";

    [Fact]
    public void Mask_DropsTheToken()
    {
        var masked = WebhookUrlMasker.Mask(RealShape);

        // The token is everything after the last slash. None of it, beyond the short tail, may show.
        var token = RealShape[(RealShape.LastIndexOf('/') + 1)..];
        Assert.DoesNotContain(token, masked);
        Assert.False(masked.Contains(token[..20], StringComparison.Ordinal));
    }

    [Fact]
    public void Mask_DropsTheWebhookId()
    {
        // The id cannot post on its own, but it names the channel — and on a stream that is still
        // more than a viewer should get.
        Assert.DoesNotContain("1534061441160712242", WebhookUrlMasker.Mask(RealShape));
    }

    [Fact]
    public void Mask_KeepsEnoughToTellTwoWebhooksApart()
    {
        var masked = WebhookUrlMasker.Mask(RealShape);

        // The whole point of masking rather than blanking: someone with a personal and a clan
        // webhook has to be able to tell which field holds which.
        Assert.StartsWith("https://discord.com/", masked);
        Assert.EndsWith("BeqXP", masked);

        var other = WebhookUrlMasker.Mask(RealShape[..^5] + "ZZZZZ");
        Assert.NotEqual(masked, other);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Mask_EmptyInputGivesEmptyString(string? input)
    {
        // Bound straight to a TextBox, so "nothing saved" must render as an empty field, not "…".
        Assert.Equal("", WebhookUrlMasker.Mask(input));
    }

    [Fact]
    public void Mask_UnparseableInputIsTreatedAsASecretInFull()
    {
        // Whatever this is, it came out of the encrypted config, so it is a stored credential.
        // Failing a format check is not a reason to show it.
        var masked = WebhookUrlMasker.Mask("not-a-url-but-still-a-saved-secret-value");

        Assert.DoesNotContain("not-a-url", masked);
        Assert.StartsWith("…", masked);
    }

    [Fact]
    public void Mask_ShortValueRevealsNothing()
    {
        // Showing the last five of an eight-character secret is showing most of it.
        Assert.Equal("…", WebhookUrlMasker.Mask("abcdefgh"));
    }

    [Fact]
    public void IsMasked_TellsAMaskFromARealUrl()
    {
        // The commit path uses this to know the user never revealed or edited the field, instead of
        // a separate bool that could drift out of sync with what is actually on screen.
        Assert.True(WebhookUrlMasker.IsMasked(WebhookUrlMasker.Mask(RealShape)));
        Assert.False(WebhookUrlMasker.IsMasked(RealShape));
        Assert.False(WebhookUrlMasker.IsMasked(""));
        Assert.False(WebhookUrlMasker.IsMasked(null));
    }
}
