using System.IO;

namespace ROROROblox.Tests;

/// <summary>
/// The shipped binary names no vendor. This is the metric-alerts spec's own closing test made
/// executable: "does the shipped binary or the 626-hosted manifest name Big Games? If yes, the
/// separation is a fig leaf." (docs/superpowers/specs/2026-09-09-external-metric-alerts-design.md
/// §1.6). Every plan up to this one honoured that line by review; a reviewer having an off day is
/// exactly the failure mode a fence survives and a checklist item does not.
///
/// <para>
/// <b>Yes, this file contains the very strings it forbids, and that is not an oversight.</b> A
/// test asserting a string is absent has to name the string somewhere to compare against. This
/// file lives in the test project, which never ships — the terms in <see cref="Forbidden"/> reach
/// no user, no NuGet package and no plugin author. Do not "tidy" them out: removing a term from
/// that list is removing the fence, not cleaning it up.
/// </para>
/// <para>
/// Why it matters enough to fence: the metric-alerts architecture rests entirely on RoRoRo being
/// a generic metric watcher rather than a client for one company's API. The person who calls that
/// API is a clan member, on their own machine, through their own plugin — never this binary. The
/// day a vendor hostname or company name shows up in Core, App or PluginContract source, that
/// argument is over, whatever the manifest and the plugin still get right.
/// </para>
/// </summary>
public class NoVendorNameFenceTests
{
    // Lower-cased and matched case-insensitively — a leak is as likely to arrive in a comment as
    // in a string literal, and a comment is exactly where a developer would paste a vendor name
    // "just for context" while wiring up the next metric.
    //
    // This list is shorter than the one the task brief started with. "ps99" / "petsim" / "pet
    // sim" / "pet simulator" are dropped on purpose: that is the *game's* name, not the vendor's,
    // and it is already pervasive, pre-existing and shipped in Core and App for reasons that have
    // nothing to do with metric alerts — the account free-text tag feature suggests "PS99" as an
    // example tag (IAccountStore.cs, AccountExportRecord.cs, MainWindow.xaml, every Strings.*.resx),
    // the memory-headroom advisor cites Pet Sim 99 client behaviour as the empirical basis for its
    // defaults (ClientFootprintLearner.cs, LaunchHeadroomAdvisor.cs, MemoryDefaults.cs), and the
    // Discord roster and session-history accessibility work both name it as the running example
    // (RosterSnapshot.cs, SessionHistoryRowName.cs). None of that is the metric-alerts feature
    // reaching for a vendor's API; it is the tool's own pre-existing, public-facing identity as
    // "the thing the Pet Sim 99 clan runs." Forbidding the game's name would make this fence red
    // on day one for content the metric-alerts spec never touched, which teaches the next reader
    // to stop trusting it. Dropping it also costs nothing on detection: the design spec's actual
    // endpoint (§0.3) is `ps99.biggamesapi.io`, and "biggames" below still matches that substring.
    //
    // Kept: the vendor's own name and the acronym its community trackers use for it. Neither has
    // a legitimate hit anywhere in the shipping tree today (verified before this fence was
    // written) and neither collides with ordinary English.
    private static readonly string[] Forbidden =
    [
        "biggames", "big games", "bgsi",
    ];

    // Source text is not the only way a vendor hostname ships. `.csproj` and `.json` are here
    // because a package reference naming a vendor feed, or a base URL parked in `appsettings.json`,
    // is INSIDE these three projects and would have sailed past a .cs/.proto/.resx/.xaml net —
    // which is the shape this fence exists to catch, not an exotic one. `.json` rather than
    // `appsettings.json` specifically: the next config file to arrive should be covered on the day
    // it lands, not on the day someone remembers this list.
    private static readonly string[] Extensions = [".cs", ".proto", ".resx", ".xaml", ".csproj", ".json"];

    // The three projects that ship. Deliberately not ROROROblox.Tests or
    // ROROROblox.PluginTestHarness — those discuss the vendor by necessity (this file included)
    // and never reach a user, so scanning them would fence the discussion, not the leak.
    private static readonly string[] ShippingProjects =
    [
        "ROROROblox.Core", "ROROROblox.App", "ROROROblox.PluginContract",
    ];

    [Fact]
    public void NoShippingProjectNamesTheVendor()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx above the test assembly.");

        var offenders = new List<string>();
        var scanned = 0;

        foreach (var project in ShippingProjects)
        {
            var dir = Path.Combine(root!, "src", project);
            if (!Directory.Exists(dir)) continue;

            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;

                scanned++;
                var label = Path.GetRelativePath(root!, path).Replace('\\', '/');
                var lines = File.ReadAllLines(path);

                for (var i = 0; i < lines.Length; i++)
                {
                    foreach (var term in Forbidden)
                    {
                        if (lines[i].Contains(term, StringComparison.OrdinalIgnoreCase))
                        {
                            offenders.Add($"{label}:{i + 1}: contains \"{term}\"");
                        }
                    }
                }
            }
        }

        // The floor guards against a broken filesystem walk (FindRepoRoot or a renamed project
        // directory yielding zero files, which would pass the assertion below vacuously). It is a
        // vacuity floor, not a ratchet: it must not be raised to track the tree, or ordinary file
        // deletion turns this fence red for a reason it has nothing to say about. 300 sits
        // comfortably under the 369 matching files measured across the three projects on
        // 2026-09-11 (365 source plus the four .csproj/.json the extension list picked up that
        // day) and comfortably over the zero a broken walk produces.
        Assert.True(scanned >= 300,
            $"Expected to scan Core, App and PluginContract source, found {scanned} files. "
            + "That is the walk breaking, not the tree shrinking.");

        Assert.True(offenders.Count == 0,
            "A shipping project names the vendor. Whatever needs that name belongs in a plugin's "
            + "own manifest or settings — Core, App and PluginContract stay generic. Offenders:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}
