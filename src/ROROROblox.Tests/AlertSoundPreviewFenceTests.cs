using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// The alert sound must be previewable from Settings, through the SAME player the alert path uses.
/// <para>
/// <b>Why this exists.</b> Item 5 shipped three sounds and no way to hear any of them. The picker
/// only saved; the string "What a desktop alert sounds like" is its
/// <c>AutomationProperties.Name</c>, which reads like a play button in the markup and is not one.
/// A user chose between three sounds blind for a whole release, and the v1.33 live smoke could not
/// perform its own sound run from the Settings page at all — an alert had to be forced before
/// anyone could hear anything. Nothing failed, because nothing asserted it.
/// </para>
/// <para>
/// <b>Why it insists on the shared player.</b> A preview that calls its own <c>SoundPlayer</c>
/// would pass a looser version of this test and still lie to the user the moment the two
/// implementations drift — which is exactly the failure mode the three-paths-to-the-screen work
/// spent item 3 removing. One player, one sound.
/// </para>
/// </summary>
public class AlertSoundPreviewFenceTests
{
    private static string Read(params string[] parts)
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        var path = Path.Combine(new[] { root! }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Missing {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void SettingsOffersAWayToHearTheAlertSound()
    {
        var xaml = Read("src", "ROROROblox.App", "Preferences", "SettingsPage.xaml");

        var button = Regex.Match(xaml, @"<Button[^>]*?x:Name=""HearAlertSoundButton""(?<attrs>[^>]*?)/?>",
            RegexOptions.Singleline);
        Assert.True(button.Success,
            "The alert sound preview button is gone. Settings then offers three sounds and no way "
            + "to hear any of them, which is the state v1.33's smoke found and fixed.");

        var attrs = button.Groups["attrs"].Value;
        Assert.Contains("Click=\"OnHearAlertSoundClick\"", attrs, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=", attrs, StringComparison.Ordinal);
        // Copy comes from the catalogues, like every other string on this page.
        Assert.Contains("{loc:Loc ", attrs, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePreviewPlaysThroughTheSamePlayerTheAlertPathUses()
    {
        var cs = Read("src", "ROROROblox.App", "Preferences", "SettingsPage.xaml.cs");

        Assert.Contains("IAlertSoundPlayer", cs, StringComparison.Ordinal);

        var handler = Regex.Match(cs, @"OnHearAlertSoundClick\s*\([^)]*\)\s*(=>|\{)(?<body>.{0,400})",
            RegexOptions.Singleline);
        Assert.True(handler.Success, "OnHearAlertSoundClick is missing.");
        Assert.Contains("_alertSoundPlayer.Play()", handler.Groups["body"].Value, StringComparison.Ordinal);

        // A second SoundPlayer on this page would be a preview that can drift from the real thing.
        Assert.DoesNotContain("new SoundPlayer", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemSounds.", cs, StringComparison.Ordinal);
    }
}
