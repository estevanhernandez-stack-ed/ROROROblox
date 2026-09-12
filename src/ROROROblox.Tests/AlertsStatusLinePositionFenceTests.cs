namespace ROROROblox.Tests;

/// <summary>
/// The alerts status line has to sit between the routing rows it summarises and the fields it
/// points at, because its own sentences say so.
/// <para>
/// Three of the eight arms <c>AlertStatusLine</c> can return are directional: "make a new webhook
/// and paste it <i>below</i>", "check the key <i>below</i>", "haven't finished setting up the push
/// service <i>below</i>". A fourth points the other way — "Pick what you want to hear about
/// <i>above</i>". So the sentence is only true in one position, and until 2026-09-12 the control was
/// not in it: the line sat at the very bottom of the card, beneath the Pushover tokens, pointing
/// "below" at two fields that were above it.
/// </para>
/// <para>
/// That is worth a fence rather than a comment because the failure is invisible in the obvious
/// case. The arm a developer sees while working is usually "Sending to your channel and your phone",
/// which reads correctly from anywhere on the page. The three broken arms need a deleted webhook, a
/// rejected push key, or a half-finished setup to appear at all, so moving this control back down
/// would look completely harmless and break three states nobody had on screen.
/// </para>
/// <para>
/// Ordinal position in the markup, which is what determines visual order inside the card's
/// StackPanel. A cheap proxy, and honest about being one: it cannot tell that the line is legible
/// or that a future layout has not wrapped something in a Grid that reorders it visually.
/// </para>
/// </summary>
public class AlertsStatusLinePositionFenceTests
{
    private static string SettingsMarkup()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.False(appDir is null, "Could not locate the app project above the test assembly.");

        var path = Path.Combine(appDir!, "Preferences", "SettingsPage.xaml");
        Assert.True(File.Exists(path), $"SettingsPage.xaml is not at {path}.");
        return File.ReadAllText(path);
    }

    private static int IndexOfOrFail(string markup, string needle, string what)
    {
        var at = markup.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(at >= 0, $"Could not find {what} in SettingsPage.xaml (looked for \"{needle}\").");
        return at;
    }

    [Fact]
    public void TheStatusLineComesAfterTheRoutingRowsItSummarises()
    {
        var markup = SettingsMarkup();

        // The last control in the rows block: the unmute-all button that closes the routing area.
        var rows = IndexOfOrFail(markup, "OnUnmuteAllClick", "the routing rows' unmute button");
        var status = IndexOfOrFail(markup, "x:Name=\"AlertsStatusLine\"", "the alerts status line");

        Assert.True(rows < status,
            "The alerts status line now sits ABOVE the routing rows. One of its arms reads "
            + "\"Pick what you want to hear about above\", which is then pointing at nothing.");
    }

    [Fact]
    public void TheStatusLineComesBeforeTheFieldsItPointsAt()
    {
        var markup = SettingsMarkup();

        var status = IndexOfOrFail(markup, "x:Name=\"AlertsStatusLine\"", "the alerts status line");
        var webhook = IndexOfOrFail(markup, "x:Name=\"MineWebhookInput\"", "the personal webhook field");
        var phoneKey = IndexOfOrFail(markup, "x:Name=\"PushoverUserKeyInput\"", "the Pushover user key field");

        Assert.True(status < webhook,
            "The alerts status line now sits BELOW the webhook field. Its webhook-deleted arm says "
            + "\"make a new webhook and paste it below\", which is then pointing backwards up the page.");

        Assert.True(status < phoneKey,
            "The alerts status line now sits BELOW the phone credentials. Its push-rejected arm says "
            + "\"check the key below\", which is then pointing backwards up the page.");
    }
}
