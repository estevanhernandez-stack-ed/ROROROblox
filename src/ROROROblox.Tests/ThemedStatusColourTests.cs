using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// Status and chip colours resolve from the active theme, and stay that way.
/// <para>
/// WHAT BROKE. <c>StatusDotBrushConverter</c> and <c>IdleChipBrushConverter</c> held six RGB
/// literals in C# and handed back static brushes. <c>ThemeService.ApplyTo</c> could not reach
/// either, so under an achromatic theme the row still painted brand green, brand amber and brand
/// magenta onto a flat field. The fix was deletion, not a smarter converter:
/// <c>IValueConverter.Convert</c> re-runs when the binding SOURCE changes, never when the resource
/// dictionary does, and <c>ThemeService.ApplySlot</c> REPLACES the brush instance rather than
/// mutating it — so a converter resolving <c>Application.Current.Resources</c> at Convert time
/// hands back a stale instance the moment the theme changes. That fix passes review and fails the
/// live repaint on screen, which is the worst shape a fix can have.
/// </para>
/// <para>
/// THIS IS A FENCE, NOT A GATE. It proves the colours come from the theme. It does not prove they
/// are legible. <c>ContrastPairGateTests</c> scans elements declaring BOTH Background and
/// Foreground inline, and a <c>Style</c>/<c>DataTrigger</c> setter is not an inline attribute — so
/// that gate could not see the status dot before this change and cannot see it after. A green run
/// here is not "the status dot's contrast is verified." Extending the gate to resolved-style
/// setters is Phase 2 work with its own design (spec §5.4, §16).
/// </para>
/// </summary>
public class ThemedStatusColourTests
{
    /// <summary>
    /// The shipped App assembly, reached the same way <c>XamlStyleIntegrityTests</c> reaches it —
    /// through a type the test project already compiles against, not <c>Assembly.Load</c> by name.
    /// </summary>
    private static readonly Assembly AppAssembly =
        typeof(ROROROblox.App.ViewModels.MainViewModel).Assembly;

    /// <summary>
    /// A colour literal built in C#. Exactly the two forms spec §5.4 names: a
    /// <c>Color.FromRgb(...)</c> call, and a <c>SolidColorBrush</c> constructed straight from a
    /// literal hex string. Anything matching is off the governed path — <c>ThemeService.ApplyTo</c>
    /// cannot reach it, so it survives every theme change unchanged.
    /// </summary>
    private static readonly Regex[] LiteralColour =
    [
        new(@"Color\.FromRgb\s*\(", RegexOptions.Compiled),
        new(@"new\s+SolidColorBrush\s*\(\s*""#[0-9A-Fa-f]{6,8}""", RegexOptions.Compiled),
    ];

    /// <summary>
    /// A site that is allowed to hold a colour literal, and the reason it is allowed. The list is
    /// spec §7's out-of-scope set — per-account IDENTITY paint, which is not a theme colour and
    /// must not follow the theme, because two accounts that look alike is the defect there.
    /// <para>
    /// <c>Anchor</c> is the declaring member, and it must appear on the offending line or within
    /// <see cref="AnchorLookback"/> lines above it. Deliberately not a whole-file exemption:
    /// <c>Converters.cs</c> is the file the two deleted converters lived in, so allow-listing all
    /// of it would blind this fence in exactly the place it exists to watch. A new literal in that
    /// file that is not part of the caption palette still fails.
    /// </para>
    /// </summary>
    private sealed record AllowedLiteral(string File, string Anchor, string Reason);

    private const int AnchorLookback = 12;

    private static readonly AllowedLiteral[] AllowList =
    [
        new("src/ROROROblox.App/Converters.cs", "AutoPalette",
            "Spec §7. Per-account caption identity, chosen by a stable hash of Account.Id. It paints "
            + "WHO a row is, not WHAT STATE it is in, and it must stay distinguishable per account "
            + "rather than collapse into one themed accent. Kept in sync with "
            + "RobloxWindowDecorator.AutoPalette by hand."),

        new("src/ROROROblox.App/Converters.cs", "MainColor",
            "Spec §7. The main account's caption identity colour, same palette, same reasoning — "
            + "identity paint, not theme paint."),

        new("src/ROROROblox.App/Tray/RobloxWindowDecorator.cs", "AutoPalette",
            "Spec §7. The same per-account palette, applied through DwmSetWindowAttribute to a Win32 "
            + "title bar the theme does not own. Today it holds packed uint ARGB constants, so it "
            + "trips neither pattern above; the entry stands because §7 names the surface and a "
            + "future rewrite in Color terms belongs on this list rather than being a surprise."),

        new("src/ROROROblox.App/Plugins/ConsentSheet.xaml.cs", "NamespaceBrush",
            "NOT in spec §7 — found in the tree at build time, allow-listed rather than absorbed "
            + "silently. Both literals are TryFindResource FALLBACKS: the themed CyanBrush and "
            + "MagentaBrush win whenever App.xaml's dictionary is loaded, and the literal is only "
            + "reached when it is not (design-time, or a host that never merged it). Out of scope for "
            + "this cycle, which owns the four MainWindow status bindings. Wants a register row."),
    ];

    /// <summary>
    /// Every App source file, obj/ and bin/ excluded — generated copies double-report every
    /// finding. Same walk <c>MutedTextFenceTests</c>' code-behind clause uses, off the same
    /// repo-root discovery, so there is one way to find the source tree and not three.
    /// </summary>
    private static IEnumerable<(string RelativePath, string[] Lines)> AppSource()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        var appDir = XamlStyleScanner.AppSourceDirectory();
        if (root is null || appDir is null) yield break;

        foreach (var path in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            yield return (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllLines(path));
        }
    }

    private static AllowedLiteral? AllowedAt(string relativePath, string[] lines, int index)
    {
        foreach (var entry in AllowList)
        {
            if (!string.Equals(entry.File, relativePath, StringComparison.OrdinalIgnoreCase)) continue;

            for (var i = index; i >= 0 && i > index - AnchorLookback; i--)
            {
                if (lines[i].Contains(entry.Anchor, StringComparison.Ordinal)) return entry;
            }
        }

        return null;
    }

    private static IEnumerable<Type> AppTypes()
    {
        try
        {
            return AppAssembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A WPF assembly can fail to load a type without the assembly itself being unusable.
            // Judge on what did load rather than turning a partial load into a green run.
            return ex.Types.Where(t => t is not null)!;
        }
    }

    /// <summary>
    /// Deletion IS the assertion. A converter that returns a brush the theme cannot reach cannot be
    /// half-fixed — either the type is gone or some binding somewhere can still reach it. Reflection
    /// by NAME rather than <c>typeof</c> on purpose: <c>typeof(StatusDotBrushConverter)</c> would
    /// stop compiling the moment the fix landed, which would make this fence a build error instead
    /// of a test, and a build error cannot be watched going red first.
    /// </summary>
    [Theory]
    [InlineData("StatusDotBrushConverter")]
    [InlineData("IdleChipBrushConverter")]
    public void TheHardcodedStatusConverterNoLongerExists(string typeName)
    {
        var found = AppTypes().FirstOrDefault(t => t.Name == typeName);

        Assert.True(found is null,
            $"{typeName} still exists in {AppAssembly.GetName().Name} as {found?.FullName}. Status "
            + "and chip colours come from a Style + DataTrigger setting {DynamicResource}, per spec "
            + "§5.2 — a converter cannot observe a resource-dictionary change, so a converter that "
            + "reads Application.Current.Resources at Convert time still fails the live repaint.");
    }

    /// <summary>
    /// The regrowth clause. Deleting two classes does not stop a third from being written, and the
    /// cheapest wrong answer to "what colour should this dot be" is a hex typed into C#.
    /// </summary>
    [Fact]
    public void NoColourLiteralIsConstructedInAppCodeOutsideTheAllowList()
    {
        var offenders = new List<string>();

        // This clause's own vacuity floor. A scan that matches nothing passes while checking
        // nothing — the failure mode both sibling fences in this project carry a floor for. Counting
        // ALLOWED hits rather than files is the tighter check: it proves the regexes still fire and
        // the anchor lookback still resolves, not merely that the directory walk found something.
        var allowed = 0;

        foreach (var (relativePath, lines) in AppSource())
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var match = LiteralColour
                    .Select(rx => rx.Match(lines[i]))
                    .FirstOrDefault(m => m.Success);
                if (match is null) continue;

                if (AllowedAt(relativePath, lines, i) is not null)
                {
                    allowed++;
                    continue;
                }

                offenders.Add($"{relativePath}:{i + 1}  {lines[i].Trim()}");
            }
        }

        // Measured 2026-08-10, after the two converters were deleted: 11 allowed literals — 9 in
        // Converters.cs (8 AutoPalette entries + MainColor) and 2 in ConsentSheet.xaml.cs's
        // TryFindResource fallbacks. Floor of 6 leaves room for the caption palette to be retuned
        // without tripping, and sits far enough above zero to catch a dead scan.
        Assert.True(allowed >= 6,
            $"This clause matched only {allowed} allow-listed colour literals. It should be seeing "
            + "roughly eleven — a count this low means the regexes or the anchor lookback broke, not "
            + "that the app stopped constructing colours.");

        Assert.True(offenders.Count == 0,
            "A colour literal built in App code is off the governed path: ThemeService.ApplyTo cannot "
            + "reach it, so it survives every theme change unchanged. Bind a theme slot through "
            + "{DynamicResource} instead — Style + DataTrigger when the value depends on state (spec "
            + "§5.2). If the colour genuinely is not a theme colour, add it to AllowList with the "
            + "reason, the way spec §7's per-account identity paint is listed:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheFenceSeesTheAppItClaimsTo()
    {
        // Backstops the reflection clause, which is the one that cannot fail loudly on its own: if
        // AppAssembly ever resolved to the wrong assembly, or GetTypes returned an empty partial
        // load, "the type is not here" would be true for the most useless possible reason. Pinning a
        // converter that SURVIVES this cycle is what separates "deleted" from "never looked."
        Assert.Contains(AppTypes(), t => t.Name == "CaptionColorBrushConverter");

        // And the source walk behind the regrowth clause. The app ships ~124 .cs files outside
        // obj/bin; a floor well under that catches a broken repo-root walk without tripping on churn.
        var files = AppSource().Count();
        Assert.True(files >= 60,
            $"The source walk found only {files} App .cs files. It should be seeing roughly 124 — "
            + "the repo-root walk is broken, not the app.");
    }

    // AboutWindow is NOT on the allow-list and does not need to be. Spec §7 puts its fixed logo
    // hexes out of scope (invariant 2 — the 626 Labs duo is never split), but every one of them is
    // declared in AboutWindow.xaml as <SolidColorBrush Color="#..."/> markup; AboutWindow.xaml.cs
    // constructs no colour at all. This fence scans C# only, per §5.4's "constructed in App code",
    // so it never looks at those hexes. An allow-list entry for them would be a claim this fence
    // had considered and permitted something it structurally cannot see.
}
