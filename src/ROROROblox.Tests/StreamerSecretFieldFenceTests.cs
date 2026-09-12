using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// Every credential field on the Settings page must be one streamer mode knows about.
/// <para>
/// Found by hand on 2026-09-12, while testing the phone-setup QR code. That code was correctly
/// hidden when streamer mode came on — and it was the ONLY thing on the page that was.
/// <c>StreamerModeToggle</c> was read in exactly one place in the entire Settings page, the QR
/// refresh, so a revealed ntfy topic, either Discord webhook URL, or either Pushover credential
/// simply stayed on screen. The webhook URLs are the sharpest case: short enough to read off a
/// single paused frame, and a webhook URL grants posting rights to the channel.
/// </para>
/// <para>
/// The fix is a list in code-behind, <c>SecretFields()</c>, which both the re-mask and the
/// confirm-before-reveal walk. A list is exactly the thing a sixth field gets left out of, and
/// leaving one out is silent: the field keeps working, the Show button keeps working, and only
/// streamer mode quietly stops covering it. So this fence reads the markup for reveal toggles and
/// proves the list names every one.
/// </para>
/// <para>
/// Source-scanning rather than behavioural, and that is a real limit worth stating: this proves the
/// field is LISTED, not that masking it works. Constructing <c>SettingsPage</c> needs the whole DI
/// graph, and the behaviour is three lines of WPF property setting. The listing is where the bug
/// was, so the listing is what is fenced.
/// </para>
/// </summary>
public class StreamerSecretFieldFenceTests
{
    /// <summary>
    /// The reveal toggles on the page today: both webhooks, the Pushover pair, and the ntfy topic.
    /// Asserted as equality so a sixth one has to come here and be thought about.
    /// </summary>
    private const int RevealTogglesOnThePage = 5;

    private static string SettingsDirectory()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.False(appDir is null, "Could not locate the app project above the test assembly.");
        return Path.Combine(appDir!, "Preferences");
    }

    private static string Read(string fileName)
    {
        var path = Path.Combine(SettingsDirectory(), fileName);
        Assert.True(File.Exists(path), $"{fileName} is not at {path}.");
        return File.ReadAllText(path);
    }

    /// <summary>Every <c>x:Name</c> ending in <c>Reveal</c> declared in the Settings markup.</summary>
    private static IReadOnlyList<string> RevealToggleNames() =>
        [.. Regex.Matches(Read("SettingsPage.xaml"), @"x:Name=""([A-Za-z0-9_]+Reveal)""")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    /// <summary>The body of <c>SecretFields()</c>, which is the list streamer mode walks.</summary>
    private static string SecretFieldsBody()
    {
        var code = Read("SettingsPage.xaml.cs");
        var start = code.IndexOf("SecretFields()", StringComparison.Ordinal);
        Assert.True(start >= 0,
            "SettingsPage.xaml.cs has no SecretFields() method. If it was renamed, this fence needs "
            + "the new name — do not delete the fence, it is the only thing proving streamer mode "
            + "covers every credential field.");

        var end = code.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not find the end of SecretFields().");
        return code[start..end];
    }

    [Fact]
    public void TheFenceStillFindsTheRevealTogglesItWasWrittenAgainst()
    {
        // Vacuity floor: a renamed convention or a moved file would empty this and make the real
        // assertion below pass over nothing at all.
        Assert.Equal(RevealTogglesOnThePage, RevealToggleNames().Count);
    }

    [Fact]
    public void StreamerModeKnowsAboutEveryRevealableCredential()
    {
        var body = SecretFieldsBody();

        var unprotected = RevealToggleNames()
            .Where(name => !body.Contains(name, StringComparison.Ordinal))
            .ToList();

        Assert.True(unprotected.Count == 0,
            $"These Settings fields have a Show toggle but are not in SecretFields(): "
            + $"{string.Join(", ", unprotected)}. Streamer mode will not re-mask them when it turns "
            + "on, and clicking Show will not ask for confirmation while it is on. Add each to "
            + "SecretFields() and move RevealTogglesOnThePage in the same commit.");
    }

    [Fact]
    public void TheStreamerNotificationStillReMasks()
    {
        // The list is useless if nothing walks it when the toggle flips. This is the one line that
        // connects them, and it is easy to lose in a refactor of that notification handler.
        var code = Read("SettingsPage.xaml.cs");
        Assert.Contains("ReMaskRevealedSecrets()", code, StringComparison.Ordinal);
        Assert.Matches(
            @"StreamerModeToggle\.IsChecked == true\)\s*ReMaskRevealedSecrets\(\)",
            code);
    }
}
