using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ROROROblox.App.Games;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// v1.36 item 9, the Play as picker (D2): what the item template binds to and what each item does.
/// <para>
/// THE STREAMER-MODE TEST IS A BINDING TEST ON PURPOSE. The picker holds no masking code; it is safe because its item
/// template reads <c>RenderName</c> and <c>AvatarDisplaySource</c>, the two properties that already flip under streamer
/// mode, and never <c>DisplayName</c> or <c>AvatarUrl</c>. So the test renders the template with an always-active
/// provider and checks both the drawn text and the binding path, because text alone would pass if someone bound the real
/// name and the fixture's real name happened to be the fake. "Eyes at C3" covers the live roster.
/// </para>
/// </summary>
[Collection("MutatesUiCulture")]
public class LibraryFlyoutTests
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static T OnHost<T>(bool streamer, Func<AccountPickerFlyout, T> read)
    {
        var brand = LibraryPageFixtures.Themes().Single(t => t.Id == "brand");
        AccountPickerFlyout? menu = null;
        return ThemedWindowRender.Inspect(
            brand,
            "Play as flyout",
            () =>
            {
                var built = LibraryPageFixtures.BuildFlyout(brand, streamer);
                menu = built.Menu;
                return built.Window;
            },
            _ => read(menu!));
    }

    [WindowRenderFact]
    public void EveryAccountIsAnItem_NamedInTwoWords_AndOnlyTheExpiredOneIsDisabledWithItsReason()
    {
        var items = OnHost(streamer: false, menu => Enumerable.Range(0, menu.Items.Count)
            .Select(i => (MenuItem)menu.ItemContainerGenerator.ContainerFromIndex(i))
            .Select(m => (m.IsEnabled, Tip: m.ToolTip as string, Name: AutomationProperties.GetName(m)))
            .ToList());

        Assert.Equal(5, items.Count);
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Name))); // every MenuItem named

        Assert.Equal("NovaFake, In Petal Simulator", items[0].Name);
        Assert.Equal("PetFake07, Joins by following", items[1].Name);
        Assert.Equal("AltFake, Ready", items[2].Name);
        Assert.Equal("BuildFake, Limited by Roblox", items[3].Name);
        Assert.Equal("OldFake2024, Session expired", items[4].Name);

        // A follow-marked or limited account is still pickable: the launch path decides, not the picker.
        Assert.Equal([true, true, true, true, false], items.Select(i => i.IsEnabled).ToArray());
        Assert.Equal(Loc.Get("Library_PlayAs_ExpiredReason"), items[4].Tip);
        Assert.All(items.Take(4), i => Assert.Null(i.Tip));
    }

    [WindowRenderFact]
    public void Unmasked_TheItemsShowTheRealNames()
    {
        var names = OnHost(streamer: false, menu => Descendants<TextBlock>(menu).Select(t => t.Text).ToList());

        Assert.Contains("NovaFake", names);
        Assert.Contains("OldFake2024", names);
        Assert.DoesNotContain(LibraryPageFixtures.MaskedIdentity.FakeName, names);
    }

    [WindowRenderFact]
    public void UnderStreamerMode_TheItemsShowMaskedNamesAndAvatars_ThroughTheMaskedProperties()
    {
        var (texts, avatarPaths, namePaths, itemNames, avatarSources) = OnHost(streamer: true, menu =>
        {
            var blocks = Descendants<TextBlock>(menu).ToList();
            var images = Descendants<Image>(menu).ToList();
            return (
                blocks.Select(t => t.Text).ToList(),
                images.Select(i => BindingOperations.GetBinding(i, Image.SourceProperty)?.Path.Path).ToList(),
                blocks.Where(t => t.Text == LibraryPageFixtures.MaskedIdentity.FakeName).Select(t => BindingOperations.GetBinding(t, TextBlock.TextProperty)?.Path.Path).ToList(),
                Enumerable.Range(0, menu.Items.Count)
                    .Select(i => AutomationProperties.GetName((MenuItem)menu.ItemContainerGenerator.ContainerFromIndex(i))).ToList(),
                images.Select(i => ((AccountSummary)i.DataContext).AvatarDisplaySource).ToList());
        });

        // The five names drawn are the fake, and no real name is drawn anywhere.
        Assert.Equal(5, texts.Count(t => t == LibraryPageFixtures.MaskedIdentity.FakeName));
        foreach (var real in new[] { "NovaFake", "PetFake07", "AltFake", "BuildFake", "OldFake2024" })
            Assert.DoesNotContain(real, texts);

        // ...because the template reads RenderName and AvatarDisplaySource, never DisplayName or AvatarUrl.
        Assert.Equal(5, avatarPaths.Count);
        Assert.All(avatarPaths, p => Assert.Equal("AvatarDisplaySource", p));
        Assert.All(namePaths, p => Assert.Equal("RenderName", p));
        Assert.All(avatarSources, s => Assert.Equal(LibraryPageFixtures.MaskedIdentity.FakeAvatar, s));

        // The accessible names are masked too: a screen reader on a stream is still a leak.
        Assert.All(itemNames, n => Assert.StartsWith(LibraryPageFixtures.MaskedIdentity.FakeName + ", ", n));
        Assert.DoesNotContain(itemNames, n => n.Contains("Fake0") || n.Contains("NovaFake") || n.Contains("OldFake"));
    }
}
