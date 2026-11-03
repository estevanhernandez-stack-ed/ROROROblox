using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 9, the Library page's markup as text: the rules this cycle already broke once and the fences did not catch.
/// <para>
/// WHY THESE ARE SOURCE SCANS. A button style whose trigger sets <c>Content</c> or <c>Command</c> loads and renders; the
/// fences (rank, state, naming) read the rank and the setters they know and said nothing, twice. The check glyph on a
/// chip is therefore its own element bound to the view model, and the scan below fails the day a trigger tries to set
/// anything but Visibility or IsEnabled. Each scan is run against a synthetic offender first, so a regex that stopped
/// matching cannot report a clean page.
/// </para>
/// </summary>
public class LibraryPageXamlTests
{
    private static string AppDir => Path.Combine(XamlStyleScanner.FindRepoRoot()!, "src", "ROROROblox.App");

    private static string Read(params string[] relative) => File.ReadAllText(Path.Combine([AppDir, .. relative]));

    private static string GamesPage => Read("Games", "GamesPage.xaml");

    private static string Flyout => Read("Games", "AccountPickerFlyout.xaml");

    private static readonly Regex TriggerBlock = new(@"<Style\.Triggers>.*?</Style\.Triggers>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ForbiddenTriggerSetter = new(
        @"<Setter\s+Property=""(?:Content|Command|CommandParameter|Template|Background|Foreground|BorderBrush|Opacity)""",
        RegexOptions.Compiled);

    /// <summary>Trigger setters that are not Visibility or IsEnabled, one per offender, so the failure names the property.</summary>
    internal static List<string> TriggerOffenders(string xaml)
        => TriggerBlock.Matches(xaml)
            .SelectMany(block => ForbiddenTriggerSetter.Matches(block.Value).Select(m => m.Value))
            .ToList();

    [Fact]
    public void TheTriggerScanSeesAnOffender_SoACleanPageMeansSomething()
    {
        var offender = """
            <Button><Button.Style><Style TargetType="Button"><Style.Triggers>
              <DataTrigger Binding="{Binding On}" Value="True"><Setter Property="Content" Value="x" /></DataTrigger>
            </Style.Triggers></Style></Button.Style></Button>
            """;
        Assert.Equal(["<Setter Property=\"Content\""], TriggerOffenders(offender));

        // ...and it can read the app's own markup: MainWindow has trigger styles.
        Assert.Contains("<Style.Triggers>", Read("MainWindow.xaml"));
    }

    [Fact]
    public void NoTriggerOnTheLibraryPageSetsAnythingButVisibilityOrIsEnabled()
    {
        Assert.Empty(TriggerOffenders(GamesPage));
        Assert.Empty(TriggerOffenders(Flyout));
    }

    [Fact]
    public void EveryChipHasItsOwnGlyphBoundToTheViewModel_AndNoContentSetter()
    {
        var xaml = GamesPage;
        var start = xaml.IndexOf("x:Name=\"LibraryFiltersRow\"", StringComparison.Ordinal);
        Assert.True(start > 0);
        var end = xaml.IndexOf("<StackPanel Grid.Row=\"3\">", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var chips = Regex.Matches(xaml[start..end], @"<ToggleButton\b.*?</ToggleButton>", RegexOptions.Singleline);
        Assert.Equal(3, chips.Count);
        foreach (Match chip in chips)
        {
            Assert.Matches(@"Text=""&#x2713;""[^>]*\s+Visibility=""\{Binding Is(Recent|Favourites|PrivateServers)On, Converter=\{StaticResource BoolToVisibilityConverter\}\}""", chip.Value);
            Assert.Contains("AutomationProperties.Name=\"{loc:Loc Library_Chip_", chip.Value);
            Assert.Matches(@"IsChecked=""\{Binding Is(Recent|Favourites|PrivateServers)On, Mode=TwoWay\}""", chip.Value);
        }
    }

    /// <summary>C3 (Este, 2026-11-03): the chips are the mock's rounded pills, whose checked state is the Cyan outline.</summary>
    [Fact]
    public void TheChipsAreRoundedPills()
    {
        var style = Regex.Match(GamesPage, @"<Style x:Key=""LibraryChipStyle""[^>]*>", RegexOptions.Singleline);
        Assert.True(style.Success, "LibraryChipStyle not found");
        Assert.Contains("BasedOn=\"{StaticResource PillToggleButtonStyle}\"", style.Value);
    }

    [Fact]
    public void EveryInteractiveElementOnTheLibraryPageIsNamed()
    {
        var unnamed = new List<string>();
        foreach (var (file, text) in new[] { ("GamesPage.xaml", GamesPage), ("AccountPickerFlyout.xaml", Flyout) })
        {
            // Element start tags up to their closing '>' (attribute values here never contain '>').
            foreach (Match m in Regex.Matches(text, @"<(Button|ToggleButton|RadioButton|ui:TextBox|MenuItem)(?=[\s/>])[^>]*>", RegexOptions.Singleline))
            {
                if (!m.Value.Contains("AutomationProperties.Name=", StringComparison.Ordinal))
                    unnamed.Add($"{file}: {m.Groups[1].Value} at offset {m.Index}");
            }
        }

        Assert.True(unnamed.Count == 0, "These Library controls carry no accessible name (the naming fence does not scan RadioButton or MenuItem, the next one will):\n  " + string.Join("\n  ", unnamed));

        // The flyout's items are generated from the roster, so their name is a container-style setter.
        Assert.Contains("<Setter Property=\"AutomationProperties.Name\" Value=\"{Binding PlayAsItemName}\"", Flyout);
    }

    [Fact]
    public void EveryMoreButtonOwnsTheEntryMenu_BecauseOnMoreClickOpensTheButtonsOwnMenu()
    {
        var buttons = Regex.Matches(GamesPage, @"<Button\b[^>]*Click=""OnMoreClick""[^>]*>", RegexOptions.Singleline);
        Assert.Equal(2, buttons.Count); // grid tile and list row
        Assert.All(buttons, m => Assert.Contains("ContextMenu=\"{StaticResource LibraryEntryMenu}\"", m.Value));
    }

    [Fact]
    public void NoEnglishFormatStringIsLeftInTheLibraryPage()
    {
        Assert.DoesNotContain("StringFormat", GamesPage);
        Assert.DoesNotContain("StringFormat", Flyout);
    }

    [Fact]
    public void NoStockDialogIsLeftInTheGamesFolder()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(AppDir, "Games"))
            .Where(f => File.ReadAllText(f).Contains("MessageBox", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .ToList();
        Assert.True(offenders.Count == 0, "MessageBox appears in: " + string.Join(", ", offenders) + ". Remove asks in the Library's themed bar.");
    }

    [Fact]
    public void PlayIsTheCtaAndPlayAsIsTheSecondary_EachWithItsGuardId_InTheTileAndTheRow()
    {
        var xaml = GamesPage;
        var play = Regex.Matches(xaml, @"<Button\b[^>]*AutomationProperties\.AutomationId=""DangerPlay""[^>]*>", RegexOptions.Singleline);
        var playAs = Regex.Matches(xaml, @"<Button\b[^>]*AutomationProperties\.AutomationId=""DangerPlayAs""[^>]*>", RegexOptions.Singleline);
        Assert.Equal(2, play.Count);    // grid tile and list row
        Assert.Equal(2, playAs.Count);
        Assert.All(play, m => Assert.Contains("Style=\"{StaticResource CtaButtonStyle}\"", m.Value));
        Assert.All(playAs, m => Assert.Contains("Style=\"{StaticResource SecondaryButtonStyle}\"", m.Value));
    }

    [Fact]
    public void TheGridIsAListBoxOverTheVirtualizingWrapPanel_WithVirtualizationOn()
    {
        var xaml = GamesPage;
        Assert.Contains("x:Name=\"LibraryGrid\"", xaml);
        Assert.Contains("<ui:VirtualizingWrapPanel", xaml);
        Assert.Contains("<Setter Property=\"VirtualizingPanel.IsVirtualizing\" Value=\"True\" />", xaml);
    }

    [Fact]
    public void TheRowsGamePickerNoLongerReadsAnEnglishSuffixFromCore()
    {
        var main = Read("MainWindow.xaml");
        Assert.DoesNotContain("DropdownLabel", main);
        Assert.Contains("{loc:Loc Shell_Games_PrivateSuffix}", main);
        var core = File.ReadAllText(Path.Combine(XamlStyleScanner.FindRepoRoot()!, "src", "ROROROblox.Core", "FavoriteGame.cs"));
        Assert.DoesNotContain("private server)", core);
        Assert.DoesNotContain("DropdownLabel", core);
    }
}
