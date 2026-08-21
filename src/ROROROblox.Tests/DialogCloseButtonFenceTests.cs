using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// F-041. The most-repeated control in the app gets one declaration, not nine.
/// <para>
/// Nine windows carry a Close button. Wave 5 settled their RANK — all nine are
/// <c>SecondaryStrongButtonStyle</c> — and nothing settled the rest: padding came in FIVE values
/// (14,6 / 14,8 / 16,8 / 20,8 / 22,8), and <c>IsDefault</c> split four-to-four. The split looked
/// principled and was not: the four whose Close was not the default had no default button at all,
/// so Enter did nothing in Diagnostics, Friends, Games and Squad Launch.
/// </para>
/// <para>
/// A fence rather than a convention, because a convention is what the last five paddings were.
/// </para>
/// </summary>
public class DialogCloseButtonFenceTests
{
    private const string Style = "DialogCloseButtonStyle";

    /// <summary>Properties the style owns. Restating one at a site is how the drift started.</summary>
    private static readonly string[] OwnedProperties =
        ["Padding", "IsDefault", "IsCancel", "HorizontalAlignment", "Background", "Foreground", "FontWeight"];

    private static IEnumerable<(string Label, string Element)> CloseButtons()
    {
        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            var text = File.ReadAllText(file.FullPath);
            foreach (Match m in Regex.Matches(text, @"<Button\b[^>]*?Content=""Close""[^>]*?/>", RegexOptions.Singleline))
            {
                yield return (file.Label, m.Value);
            }
        }
    }

    [Fact]
    public void EveryCloseButtonUsesTheOneStyle()
    {
        var offenders = CloseButtons()
            .Where(b => !b.Element.Contains(Style, StringComparison.Ordinal))
            .Select(b => $"{b.Label}: a Close button not using {Style}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Close is the app's most repeated control and has one declaration. Use "
            + $"Style=\"{{StaticResource {Style}}}\" and let it own padding, alignment and the "
            + "Enter/Esc behaviour.\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoCloseButtonRestatesWhatTheStyleAlreadyOwns()
    {
        var offenders = new List<string>();
        foreach (var (label, element) in CloseButtons())
        {
            foreach (var prop in OwnedProperties)
            {
                if (Regex.IsMatch(element, $@"\b{prop}\s*=\s*"""))
                {
                    offenders.Add($"{label}: Close restates {prop}, which {Style} owns");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A local override is how nine Close buttons ended up with five paddings. Change the "
            + $"style if the shape is wrong for all of them.\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheFenceIsActuallyLookingAtSomething()
    {
        // Without this, a regex that stopped matching would report a clean app forever — the same
        // vacuity trap ThemedStatusColourTests guards with its floor. The floor was nine until
        // F-013: six Close buttons left with the windows the shell absorbed — a shell page is a
        // destination, not a dialog, and the shell's title bar owns closing. The dialogs that
        // remain dialogs keep theirs.
        Assert.True(CloseButtons().Count() >= 3,
            $"Expected at least the three known Close buttons; found {CloseButtons().Count()}. The scan broke.");
    }
}
