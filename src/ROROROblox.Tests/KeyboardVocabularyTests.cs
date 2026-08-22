using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using ROROROblox.App.Input;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// F-112. The vocabulary is one table consumed by three surfaces — the windows' KeyBindings, the
/// menu/tooltip hints, and the About page's list — and these tests are what keep the three from
/// drifting: every hint the markup shows must be a gesture the table defines, every gesture must
/// be unique, and none may sit on a key Ur Task holds globally (win32 RegisterHotKey wins
/// system-wide, so such a binding would look dead exactly when the plugin runs).
/// </summary>
public class KeyboardVocabularyTests
{
    [Fact]
    public void EveryGestureIsUnique()
    {
        var duplicates = KeyboardVocabulary.Shortcuts
            .GroupBy(s => (s.Key, s.Modifiers))
            .Where(g => g.Count() > 1)
            .Select(g => g.First().GestureText)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Two shortcuts share a gesture — the second can never fire: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void NoGestureUsesAPluginGlobalHotkey()
    {
        // Ctrl+Shift+R/P/A/L are Ur Task's global hotkeys. A window-level binding on one of them
        // is shadowed system-wide while the plugin runs — and only then, which hands the user an
        // intermittent nobody can diagnose from the symptom.
        var collisions = KeyboardVocabulary.Shortcuts
            .Where(s => KeyboardVocabulary.ReservedByPluginGlobalHotkeys
                .Any(r => r.Key == s.Key && r.Modifiers == s.Modifiers))
            .Select(s => s.GestureText)
            .ToList();

        Assert.True(collisions.Count == 0,
            "These gestures are reserved by Ur Task's global hotkeys: " + string.Join(", ", collisions));
    }

    [Fact]
    public void TheDisplayTextAgreesWithTheGesture()
    {
        foreach (var s in KeyboardVocabulary.Shortcuts)
        {
            var expected = Render(s.Key, s.Modifiers);
            Assert.True(expected == s.GestureText,
                $"'{s.Label}' displays \"{s.GestureText}\" but its gesture renders as \"{expected}\" — "
                + "the list would teach a key that does not fire (or fire a key the list hides).");
        }

        static string Render(Key key, ModifierKeys modifiers)
        {
            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            parts.Add(key switch
            {
                Key.OemComma => ",",
                >= Key.F1 and <= Key.F24 => key.ToString(),
                >= Key.D0 and <= Key.D9 => key.ToString()[1..],
                _ => key.ToString(),
            });
            return string.Join("+", parts);
        }
    }

    [Fact]
    public void EveryHintInTheMarkup_IsAGestureTheTableDefines()
    {
        // The drift fence: markup hints (InputGestureText on menu items, "(Ctrl+X)" tooltip
        // suffixes, AutomationProperties.AcceleratorKey) are strings the vocabulary cannot see.
        // A renamed gesture with a stale hint teaches users a key that does nothing.
        var known = KeyboardVocabulary.Shortcuts.Select(s => s.GestureText).ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            var text = File.ReadAllText(file.FullPath);

            foreach (Match m in Regex.Matches(text, @"InputGestureText=""([^""]+)"""))
            {
                if (!known.Contains(m.Groups[1].Value))
                {
                    offenders.Add($"{file.Label}: InputGestureText \"{m.Groups[1].Value}\"");
                }
            }

            foreach (Match m in Regex.Matches(text, @"AutomationProperties\.AcceleratorKey=""([^""]+)"""))
            {
                if (!known.Contains(m.Groups[1].Value))
                {
                    offenders.Add($"{file.Label}: AcceleratorKey \"{m.Groups[1].Value}\"");
                }
            }

            foreach (Match m in Regex.Matches(text, @"ToolTip=""[^""]*\((Ctrl\+[^)\s]+|F\d+)\)"""))
            {
                if (!known.Contains(m.Groups[1].Value))
                {
                    offenders.Add($"{file.Label}: tooltip hint \"({m.Groups[1].Value})\"");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Markup shows shortcut hints the vocabulary does not define:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheMarkupHintFence_IsActuallyLookingAtSomething()
    {
        // Vacuity floor, same shape every scanning fence here carries: the menu hints and the two
        // toolbar accelerators exist today, so a scan finding none of them means the regex broke,
        // not that the app went hint-free.
        var hintCount = XamlStyleScanner.EnumerateAppXamlFiles()
            .Select(f => File.ReadAllText(f.FullPath))
            .Sum(t => Regex.Matches(t, @"InputGestureText=""|AutomationProperties\.AcceleratorKey=""").Count);

        Assert.True(hintCount >= 8,
            $"Expected at least the 10 known markup hints; found {hintCount}. The scan broke.");
    }

    [Fact]
    public void BuildBindings_BindsWhatTheWindowMaps_AndSkipsTheRest()
    {
        var all = KeyboardVocabulary.BuildBindings(_ => new FakeCommand()).ToList();
        Assert.Equal(KeyboardVocabulary.Shortcuts.Count, all.Count);

        // A window that answers for nothing gets nothing — the shell has no account filter, and
        // an unmapped action must be skipped rather than bound to a null command.
        Assert.Empty(KeyboardVocabulary.BuildBindings(_ => null));

        var one = KeyboardVocabulary.BuildBindings(
            a => a == ShortcutAction.OpenGames ? new FakeCommand() : null).Single();
        Assert.Equal(Key.G, one.Key);
        Assert.Equal(ModifierKeys.Control, one.Modifiers);
    }

    private sealed class FakeCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
