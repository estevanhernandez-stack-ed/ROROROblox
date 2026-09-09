using ROROROblox.Core.Notify;

namespace ROROROblox.Tests.Notify;

/// <summary>
/// The links behind the phone-setup QR codes. Two very different jobs, and the difference is the
/// reason these live in one file: the ntfy link CARRIES the credential (the topic grants both
/// subscribe and publish), while the Pushover links are public destinations that carry nothing.
/// Anything that renders them has to know which is which.
/// </summary>
public class PhoneSetupLinkTests
{
    // --- ntfy: the subscribe link, which is a bearer credential in URL form ---

    [Fact]
    public void NtfySubscribe_DefaultServer_BuildsSubscribeUrl()
    {
        Assert.Equal("https://ntfy.sh/rororo-abc234",
            PhoneSetupLinks.NtfySubscribe("https://ntfy.sh", "rororo-abc234"));
    }

    [Fact]
    public void NtfySubscribe_TrailingSlashOnServer_DoesNotDoubleUp()
    {
        // The server box is free text and people paste trailing slashes. A double slash is a
        // different path to ntfy, so the phone would subscribe to a topic that never receives.
        Assert.Equal("https://ntfy.sh/rororo-abc234",
            PhoneSetupLinks.NtfySubscribe("https://ntfy.sh/", "rororo-abc234"));
    }

    [Fact]
    public void NtfySubscribe_BlankServer_FallsBackToPublicHost()
    {
        // Mirrors the settings default: an empty server box means ntfy.sh.
        Assert.Equal("https://ntfy.sh/rororo-abc234",
            PhoneSetupLinks.NtfySubscribe("   ", "rororo-abc234"));
    }

    [Fact]
    public void NtfySubscribe_SelfHostedHttpServer_KeepsTheScheme()
    {
        // A self-hosted ntfy on a LAN is commonly plain http. Silently upgrading to https would
        // produce a QR that cannot connect, which is worse than showing what the user configured.
        Assert.Equal("http://nas.local:8080/rororo-abc234",
            PhoneSetupLinks.NtfySubscribe("http://nas.local:8080", "rororo-abc234"));
    }

    [Fact]
    public void NtfySubscribe_ServerWithoutScheme_AssumesHttps()
    {
        Assert.Equal("https://ntfy.example.com/rororo-abc234",
            PhoneSetupLinks.NtfySubscribe("ntfy.example.com", "rororo-abc234"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NtfySubscribe_NoTopic_ReturnsNull(string? topic)
    {
        // Nothing to subscribe to yet. Callers use null to mean "render no QR" rather than
        // rendering a code that points at the server root.
        Assert.Null(PhoneSetupLinks.NtfySubscribe("https://ntfy.sh", topic));
    }

    [Fact]
    public void NtfySubscribe_TopicIsUrlEncoded()
    {
        // Generated topics are base32 and never need encoding, but the box is editable and a
        // stray space must not produce a malformed QR.
        Assert.Equal("https://ntfy.sh/we%20ird",
            PhoneSetupLinks.NtfySubscribe("https://ntfy.sh", "we ird"));
    }

    // --- Pushover: public destinations, carrying no secret ---

    [Fact]
    public void PushoverApplicationForm_IsTheBuildPage()
    {
        // Where the application token comes from. The app already offers to save the RoRoRo icon
        // for this exact form.
        Assert.Equal("https://pushover.net/apps/build", PhoneSetupLinks.PushoverApplicationForm);
    }

    [Fact]
    public void PushoverDashboard_IsWhereTheUserKeyIs()
    {
        Assert.Equal("https://pushover.net/", PhoneSetupLinks.PushoverDashboard);
    }

    [Fact]
    public void PushoverLinks_CarryNoCredential()
    {
        // The property that lets the UI show these openly while hiding the ntfy one. If a future
        // change ever parameterises these, this test should start failing and force the question.
        Assert.DoesNotContain("=", PhoneSetupLinks.PushoverApplicationForm);
        Assert.DoesNotContain("=", PhoneSetupLinks.PushoverDashboard);
    }
}
