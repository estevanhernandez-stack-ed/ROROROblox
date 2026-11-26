using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// Pins why the compat drift banner cannot fire, without repairing it (ruled "leave", 2026-11-26).
/// <c>RobloxCompatChecker.CheckAsync</c> hands the installed <c>FileVersion</c> to
/// <see cref="Version.TryParse(string?, out Version?)"/> as the file reports it, and that call rejects the
/// comma form. Whoever repairs the banner deletes this class on purpose, with the ruling in hand.
/// </summary>
public class RobloxCompatDriftPinTests
{
    [Fact]
    public void VersionTryParse_RejectsTheCommaForm_WhichIsWhyTheBannerCannotFire()
    {
        Assert.False(Version.TryParse("0, 748, 0, 7481526", out _));
    }

    [Fact]
    public void Checker_ReadsTheInstalledStringUnmodified_AndDoesNotRepairIt()
    {
        var source = ReadChecker();

        Assert.Contains("Version.TryParse(installed, out", source);
        Assert.DoesNotContain("RobloxVersion.", source);
        Assert.False(
            Regex.IsMatch(source, @"\.Replace\(\s*','\s*,\s*'\.'\s*\)|\.Replace\(\s*"",""\s*,\s*"".""\s*\)"),
            "RobloxCompatChecker must not turn commas into dots: that repairs the drift banner, which is a ruling.");
    }

    private static string ReadChecker()
    {
        var path = Path.Combine(XamlStyleScanner.FindRepoRoot(), "src", "ROROROblox.Core", "RobloxCompatChecker.cs");
        return File.ReadAllText(path);
    }
}
