using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// A destructive action must never be the Enter-key default.
///
/// <para>Found 2026-07-10: <c>RobloxAlreadyRunningWindow</c> shipped with
/// <c>IsDefault="True"</c> on "Close Roblox for me", and that modal appears unprompted at app
/// startup. A reflexive Enter — the reflex every Windows user has for a dialog they didn't
/// expect — force-closed every running Roblox client and any unsaved in-game progress with it.
/// <c>StopAllConfirmWindow</c> had the same shape: a confirmation whose default was the thing it
/// existed to confirm.</para>
///
/// <para>These tests parse the shipped XAML rather than instantiating the Window, which would
/// need an STA dispatcher and a full WPF app context. The property under test — which Button
/// carries <c>IsDefault</c> — lives in the markup, so the markup is what we assert on.</para>
/// </summary>
public class ModalDefaultButtonSafetyTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>
    /// Button labels that destroy user state when clicked. Any of these carrying IsDefault is a
    /// bug, not a preference.
    /// </summary>
    public static TheoryData<string, string> DestructiveButtons() => new()
    {
        { "RobloxAlreadyRunningWindow.xaml", "Close Roblox for me" }, // force-closes Roblox clients
        { "StopAllConfirmWindow.xaml", "Stop all" },                  // force-closes Roblox clients
        { "LeftoverProcessesWindow.xaml", "Clean up + continue" },    // runs the stop-all teardown
    };

    [Theory]
    [MemberData(nameof(DestructiveButtons))]
    public void DestructiveButton_IsNeverTheEnterKeyDefault(string modalFile, string destructiveLabel)
    {
        var button = FindButton(modalFile, destructiveLabel);
        Assert.True(button is not null, $"'{destructiveLabel}' not found in {modalFile} — did the label change?");

        var isDefault = (string?)button!.Attribute("IsDefault");
        Assert.False(
            string.Equals(isDefault, "True", StringComparison.OrdinalIgnoreCase),
            $"'{destructiveLabel}' in {modalFile} is the Enter-key default. A destructive action " +
            "must require a deliberate click.");
    }

    [Fact]
    public void BlockedModal_DefaultsToRetry_TheNonDestructiveRecovery()
    {
        // Retry re-acquires the mutex in place. On failure it reveals the still-locked tick and
        // leaves the modal up — safe to press repeatedly, and the flow the modal's own steps teach.
        Assert.Equal("Retry", DefaultButtonLabel("RobloxAlreadyRunningWindow.xaml"));
    }

    [Fact]
    public void StopAllConfirm_DefaultsToCancel()
        => Assert.Equal("Cancel", DefaultButtonLabel("StopAllConfirmWindow.xaml"));

    [Fact]
    public void LeftoverModal_DefaultsToContinue()
        => Assert.Equal("Continue", DefaultButtonLabel("LeftoverProcessesWindow.xaml"));

    [Theory]
    [InlineData("RobloxAlreadyRunningWindow.xaml")]
    [InlineData("StopAllConfirmWindow.xaml")]
    [InlineData("LeftoverProcessesWindow.xaml")]
    public void EachModal_HasExactlyOneDefaultButton(string modalFile)
        => Assert.Single(Buttons(modalFile).Where(IsDefault));

    private static bool IsDefault(XElement button)
        => string.Equals((string?)button.Attribute("IsDefault"), "True", StringComparison.OrdinalIgnoreCase);

    private static string DefaultButtonLabel(string modalFile)
        => (string?)Buttons(modalFile).Single(IsDefault).Attribute("Content")
           ?? throw new InvalidOperationException($"default button in {modalFile} has no Content");

    private static XElement? FindButton(string modalFile, string content)
        => Buttons(modalFile).FirstOrDefault(b => (string?)b.Attribute("Content") == content);

    private static IEnumerable<XElement> Buttons(string modalFile)
        => XDocument.Load(ModalPath(modalFile)).Descendants(Presentation + "Button");

    private static string ModalPath(string modalFile)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Modals", modalFile);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{modalFile} was not copied next to the test binary. See the None/CopyToOutputDirectory " +
                "item group in ROROROblox.Tests.csproj.", path);
        }
        return path;
    }
}
