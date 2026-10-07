using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// No toggle-type control may be wired with <c>Click</c> (v1.33 item 7).
/// <para>
/// F-102: <c>TogglePattern.Toggle()</c> — the only pattern UI Automation offers for a checkbox, and
/// therefore the only way a screen reader, an automation harness or our own smoke scripts can
/// operate one — raises <c>Checked</c>/<c>Unchecked</c> and NEVER <c>Click</c>. A <c>Click</c>-wired
/// toggle flips visually and saves nothing. The measured failure that reopened this in v1.33 was
/// <c>MemoryWatchdogEnabledToggle</c>: automation ticked it, the box reported Off, and the watchdog
/// kept sampling, because the handler that would have stopped it never ran.
/// </para>
/// <para>
/// This fence is item 7's deliverable, not the conversion. The conversion without it is one release
/// from regressing: <c>Click</c> is what the XAML designer and every tutorial reach for, so the next
/// toggle anyone adds will arrive wired the wrong way and nothing but this test will say so.
/// </para>
/// <para>
/// It scans the whole App, not just the Settings page, for the reason the v1.33 sweep found
/// <c>SquadLaunchWindow</c>'s careful-mode checkbox carrying the identical defect: a fence scoped to
/// one file sends the next instance to a different file.
/// </para>
/// </summary>
public class ToggleWiringFenceTests
{
    /// <summary>
    /// Controls whose checked state is the control's own business. A <c>CheckBox</c> inside a
    /// template, or one whose <c>Click</c> does something unrelated to its state, could in principle
    /// live here. It ships EMPTY: every entry is a hole, and <c>Click</c> on a toggle is almost
    /// always the F-102 mistake rather than an intention. Add one only with the reason beside it.
    /// </summary>
    private static readonly HashSet<string> AllowedClickWiredToggles =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The App carried ~45 toggle-type controls when this fence was written (28 of them the alert
    /// destination grid alone). A floor, so a broken regex or an empty scan cannot read as "no
    /// Click-wired toggles".
    /// </summary>
    private const int ToggleScanFloor = 30;

    private static readonly string[] ToggleTypes = ["CheckBox", "ToggleButton", "ToggleSwitch", "RadioButton"];

    /// <summary>
    /// An element start tag for any toggle type, across newlines — attributes in this codebase are
    /// routinely spread over five or six lines, so a line-oriented scan misses most of them. That is
    /// not hypothetical: the first single-line grep for this sweep returned zero matches while ten
    /// Click-wired toggles were sitting in the tree.
    /// </summary>
    private static readonly Regex ToggleTag = new(
        @"<(?:[A-Za-z0-9_]+:)?(" + string.Join("|", ToggleTypes) + @")\b(?<attrs>[^>]*?)/?>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex NameAttr = new(@"x:Name\s*=\s*""(?<n>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ClickAttr = new(@"\bClick\s*=\s*""(?<h>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex CheckedAttr = new(@"\bChecked\s*=\s*""[^""]+""", RegexOptions.Compiled);
    private static readonly Regex UncheckedAttr = new(@"\bUnchecked\s*=\s*""[^""]+""", RegexOptions.Compiled);

    private sealed record Toggle(string File, int Line, string Kind, string Name, string Attrs);

    private static string AppRoot()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        return Path.Combine(root!, "src", "ROROROblox.App");
    }

    private static List<Toggle> Toggles()
    {
        var found = new List<Toggle>();
        foreach (var path in Directory.EnumerateFiles(AppRoot(), "*.xaml", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            foreach (Match m in ToggleTag.Matches(text))
            {
                var attrs = m.Groups["attrs"].Value;
                var nameMatch = NameAttr.Match(attrs);
                found.Add(new Toggle(
                    Path.GetFileName(path),
                    text[..m.Index].Count(c => c == '\n') + 1,
                    m.Groups[1].Value,
                    nameMatch.Success ? nameMatch.Groups["n"].Value : "(unnamed)",
                    attrs));
            }
        }
        return found;
    }

    [Fact]
    public void NoToggleIsWiredWithClick()
    {
        var toggles = Toggles();
        Assert.True(toggles.Count >= ToggleScanFloor,
            $"Only {toggles.Count} toggle controls found — the multiline scan broke, not the UI. "
            + "A zero-match scan must never read as a clean bill of health.");

        var clickWired = toggles
            .Where(t => ClickAttr.IsMatch(t.Attrs))
            .Where(t => !AllowedClickWiredToggles.Contains(t.Name))
            .OrderBy(t => t.File, StringComparer.Ordinal).ThenBy(t => t.Line)
            .ToList();

        Assert.True(clickWired.Count == 0,
            "Click-wired toggle controls. TogglePattern.Toggle() raises Checked/Unchecked and never "
            + "Click, so UI Automation (and screen readers) flip these without the handler ever "
            + "running — the control shows the new state and nothing is saved (F-102). Wire them "
            + "Checked AND Unchecked, or bind IsChecked two-way:\n  "
            + string.Join("\n  ", clickWired.Select(t =>
                $"{t.File}:{t.Line} {t.Kind} {t.Name} -> Click=\"{ClickAttr.Match(t.Attrs).Groups["h"].Value}\"")));
    }

    [Fact]
    public void EveryToggleWiredForOneStateIsWiredForBoth()
    {
        // Half-wiring is the quieter version of the same bug: a Checked handler with no Unchecked
        // saves when the user ticks and silently keeps the old value when they untick, which reads
        // as "the setting won't turn off" and sends the next person hunting in the store layer.
        var toggles = Toggles();
        Assert.True(toggles.Count >= ToggleScanFloor,
            $"Only {toggles.Count} toggle controls found — the multiline scan broke, not the UI.");

        var half = toggles
            .Where(t => CheckedAttr.IsMatch(t.Attrs) ^ UncheckedAttr.IsMatch(t.Attrs))
            .OrderBy(t => t.File, StringComparer.Ordinal).ThenBy(t => t.Line)
            .ToList();

        Assert.True(half.Count == 0,
            "Toggles wired for one state only — one direction saves and the other silently does "
            + "nothing:\n  "
            + string.Join("\n  ", half.Select(t =>
                $"{t.File}:{t.Line} {t.Kind} {t.Name} has "
                + (CheckedAttr.IsMatch(t.Attrs) ? "Checked but no Unchecked" : "Unchecked but no Checked"))));
    }
}
