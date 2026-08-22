using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// F-048. Every window is a destination or an interruption, and its chrome follows from which.
/// <para>
/// The audit's complaint was that resizability "isn't derived from window kind" — fifteen windows
/// locked, eleven resizable, and no rule connecting either to what the window is for. That is the
/// kind of drift no one notices while adding the twenty-seventh window, so the rule is a fence
/// rather than a paragraph, in the shape this repo already uses for button ranks, contrast pairs
/// and settings reachability.
/// </para>
/// <para>
/// A <b>destination</b> is somewhere you go and do work: the roster, Settings, Games, History,
/// Diagnostics, Plugins, the theme builder. It resizes, and it declares minimums, because a
/// resizable window without them can be dragged down to a sliver the user then has to rebuild.
/// </para>
/// <para>
/// An <b>interruption</b> asks one question and leaves: confirms, blockers, pickers, the rename
/// prompt. It is fixed. It must NOT declare minimums either — a MinWidth on a window that cannot
/// resize is a line that will never run, and this fence exists partly because
/// <c>ImportAccountsWindow</c> carried exactly that pair while its sibling
/// <c>ExportAccountsWindow</c> resized.
/// </para>
/// </summary>
public class WindowChromeFenceTests
{
    /// <summary>Somewhere you go and work. Resizable, with minimums.</summary>
    private static readonly string[] Destinations =
    [
        "MainWindow",
        "ShellWindow",
        "ThemeBuilderWindow",
        "SquadLaunchWindow",
        "FriendFollowWindow",
        "WelcomeWindow",
        "CookieCaptureWindow",
        "ExportAccountsWindow",
        "ImportAccountsWindow",
    ];

    /// <summary>Asks one thing and goes away. Fixed, no minimums.</summary>
    private static readonly string[] Interruptions =
    [
        "JoinByLinkWindow",
        "CaptionColorPickerWindow",
        "DpapiCorruptWindow",
        "EdgeRemediationWindow",
        "JoinRequestWindow",
        "LaunchHeadroomWindow",
        "LeftoverProcessesWindow",
        "RenameWindow",
        "RobloxAlreadyRunningWindow",
        "RobloxNotInstalledWindow",
        "StopAllConfirmWindow",
        "WebView2NotInstalledWindow",
    ];

    private static IEnumerable<(string Name, string Text, string Label)> Windows() =>
        XamlStyleScanner.EnumerateAppXamlFiles()
            .Where(f => Path.GetFileName(f.FullPath).EndsWith("Window.xaml", StringComparison.Ordinal))
            .Select(f => (Path.GetFileNameWithoutExtension(f.FullPath), File.ReadAllText(f.FullPath), f.Label));

    /// <summary>The window element's own attributes, not a child's — children carry Width too.</summary>
    private static string RootAttributes(string xaml)
    {
        var end = xaml.IndexOf('>', xaml.IndexOf("<Window", StringComparison.Ordinal) is var i && i >= 0
            ? i
            : xaml.IndexOf('<', StringComparison.Ordinal));
        return end < 0 ? xaml : xaml[..end];
    }

    private static bool Declares(string attrs, string name) =>
        Regex.IsMatch(attrs, $@"\b{name}\s*=\s*""");

    private static string? ResizeMode(string attrs)
    {
        var m = Regex.Match(attrs, @"\bResizeMode\s*=\s*""([A-Za-z]+)""");
        return m.Success ? m.Groups[1].Value : null;
    }

    [Fact]
    public void EveryWindowIsClassifiedAsADestinationOrAnInterruption()
    {
        var known = Destinations.Concat(Interruptions).ToHashSet(StringComparer.Ordinal);
        var unclassified = Windows().Select(w => w.Name).Where(n => !known.Contains(n)).OrderBy(n => n).ToList();

        Assert.True(unclassified.Count == 0,
            "New window(s) with no chrome class. Decide which this is and add it to the list:\n"
            + "  DESTINATION  — somewhere you go and work. ResizeMode absent or CanResize, and it MUST\n"
            + "                 declare MinWidth and MinHeight, or it can be dragged to a sliver.\n"
            + "  INTERRUPTION — asks one thing and leaves. ResizeMode=\"NoResize\", and NO minimums,\n"
            + "                 which would be lines that never run.\n"
            + "Unclassified: " + string.Join(", ", unclassified));
    }

    [Fact]
    public void DestinationsResizeAndDeclareTheirMinimums()
    {
        var offenders = new List<string>();
        foreach (var (name, text, label) in Windows().Where(w => Destinations.Contains(w.Name)))
        {
            var attrs = RootAttributes(text);
            var mode = ResizeMode(attrs);
            if (mode is not null && mode != "CanResize")
            {
                offenders.Add($"{label}: destination with ResizeMode=\"{mode}\"");
            }
            if (!Declares(attrs, "MinWidth") || !Declares(attrs, "MinHeight"))
            {
                offenders.Add($"{label}: destination without MinWidth/MinHeight — it can be collapsed to nothing");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    [Fact]
    public void InterruptionsAreFixedAndDoNotPretendOtherwise()
    {
        var offenders = new List<string>();
        foreach (var (name, text, label) in Windows().Where(w => Interruptions.Contains(w.Name)))
        {
            var attrs = RootAttributes(text);
            if (ResizeMode(attrs) != "NoResize")
            {
                offenders.Add($"{label}: interruption must declare ResizeMode=\"NoResize\"");
            }
            if (Declares(attrs, "MinWidth") || Declares(attrs, "MinHeight"))
            {
                offenders.Add($"{label}: interruption declares minimums it can never reach — delete them or make it a destination");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }
}
