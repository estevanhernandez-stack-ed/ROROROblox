using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// Every text field on the Settings page that commits must commit on Enter as well as on losing
/// focus.
/// <para>
/// Found by hand on 2026-09-12, during the phone-alert QR scan test. Eight text fields on that page
/// committed on <c>LostFocus</c> and nothing else, and the page has no Save button — so a user who
/// typed a value and pressed Enter saw nothing happen at all. On the two webhook fields the failure
/// was at least VISIBLE, because the field re-masks itself on commit and simply did not; everywhere
/// else it was silent, and "Enter did nothing" is indistinguishable from "the save failed".
/// </para>
/// <para>
/// This fence exists because the fix is per-field markup. One attribute per TextBox is exactly the
/// kind of thing a later field forgets, and forgetting it reintroduces a silent bug rather than a
/// loud one. Scanning the markup is the only way to catch that: the handler is wired in XAML, so no
/// unit test that constructs the page would notice a missing attribute either.
/// </para>
/// </summary>
public class SettingsCommitOnEnterFenceTests
{
    /// <summary>
    /// The handler every committing field must also route Enter through. Its body deliberately
    /// does not commit anything itself — it dispatches to whichever <c>LostFocus</c> handler the
    /// field already had, so this fence cannot drift from where the commit logic lives.
    /// </summary>
    private const string EnterHandler = "OnCommitOnEnter";

    /// <summary>
    /// The number of committing text fields on the page today: both webhook URLs, the Pushover
    /// user key and app token, the ntfy server, and the three memory/projection numbers.
    /// <para>
    /// Asserted as EQUALITY, not as a floor, and that is the point. A new committing field must
    /// move this number in the same commit that adds it, which is the moment someone has to decide
    /// whether Enter works there too. A floor would let the tenth field arrive unnoticed.
    /// </para>
    /// </summary>
    private const int CommittingFieldsOnThePage = 8;

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlDirective = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static IReadOnlyList<XElement> CommittingTextBoxes()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.False(appDir is null, "Could not locate the app project above the test assembly.");

        var path = Path.Combine(appDir!, "Preferences", "SettingsPage.xaml");
        Assert.True(File.Exists(path), $"SettingsPage.xaml is not at {path}.");

        return [.. XDocument.Load(path)
            .Descendants(Xaml + "TextBox")
            .Where(e => e.Attribute("LostFocus") is not null)];
    }

    private static string NameOf(XElement e) =>
        e.Attribute(XamlDirective + "Name")?.Value
        ?? e.Attribute("LostFocus")?.Value
        ?? "<unnamed TextBox>";

    [Fact]
    public void TheFenceStillFindsTheFieldsItWasWrittenAgainst()
    {
        // The vacuity floor, and it earns its keep here: a changed namespace, a moved file or a
        // switch to setting LostFocus in code-behind would empty this list and make the real
        // assertion below pass over nothing.
        Assert.Equal(CommittingFieldsOnThePage, CommittingTextBoxes().Count);
    }

    [Fact]
    public void EveryCommittingFieldAlsoCommitsOnEnter()
    {
        var missing = CommittingTextBoxes()
            .Where(e => e.Attribute("PreviewKeyDown")?.Value != EnterHandler)
            .Select(NameOf)
            .ToList();

        Assert.True(missing.Count == 0,
            $"These Settings fields commit on LostFocus but not on Enter: {string.Join(", ", missing)}. "
            + $"Add PreviewKeyDown=\"{EnterHandler}\" to each. The page has no Save button, so a field "
            + "that ignores Enter gives the user no way to tell a no-op from a failed save.");
    }
}
