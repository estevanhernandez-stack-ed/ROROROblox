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

        // ConsentSheet.xaml.cs's entry was retired by v1.21 item 7. It covered NamespaceBrush's two
        // TryFindResource fallbacks; the property is gone and the branch is now a Style +
        // DataTrigger on IsHostEnforced in ConsentSheet.xaml. Retired rather than left matching
        // nothing, the same way F-085's and F-089's were — the register row it wanted got written
        // as F-087 and closes with this cycle.
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

    /// <summary>
    /// The part of <paramref name="line"/> outside an XML comment, tracking multi-line
    /// <c>&lt;!-- --&gt;</c> spans through <paramref name="inComment"/>.
    /// <para>
    /// Added when ControlStyles.xaml grew a comment QUOTING the Aero literals the v1.20 template
    /// replaced (<c>#BEE6FD</c>, <c>#C4E5F6</c>, <c>#F4F4F4</c>) as the evidence for replacing
    /// them — and this fence read the evidence as the crime. Second time a fence here has read
    /// prose as code; <c>SettingsReachabilityTests</c> grew <c>WithoutComments</c> in v1.18 for the
    /// same reason. A scanner over source text has to know which text is source.
    /// </para>
    /// </summary>
    internal static string StripXmlComment(string line, ref bool inComment)
    {
        var kept = new System.Text.StringBuilder();
        var i = 0;
        while (i < line.Length)
        {
            if (inComment)
            {
                var close = line.IndexOf("-->", i, StringComparison.Ordinal);
                if (close < 0) return kept.ToString();
                inComment = false;
                i = close + 3;
                continue;
            }

            var open = line.IndexOf("<!--", i, StringComparison.Ordinal);
            if (open < 0)
            {
                kept.Append(line, i, line.Length - i);
                return kept.ToString();
            }

            kept.Append(line, i, open - i);
            inComment = true;
            i = open + 4;
        }
        return kept.ToString();
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
            var inComment = false;
            for (var i = 0; i < lines.Length; i++)
            {
                // A hex inside <!-- --> renders nothing and no theme change can reach it, so it
                // cannot be the defect this fence exists to catch. See StripXmlComment.
                var codeOnly = StripXmlComment(lines[i], ref inComment);
                if (codeOnly.Trim().Length == 0) continue;

                var match = LiteralColour
                    .Select(rx => rx.Match(codeOnly))
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

        // Re-measured 2026-08-11 by v1.21 item 7: 9 allowed literals, all of them in Converters.cs
        // (8 AutoPalette entries + MainColor). It read 11 until this cycle; the other 2 were
        // ConsentSheet.xaml.cs's TryFindResource fallbacks, which went with F-087's fix, and their
        // allow-list entry was retired rather than left matching nothing.
        // Floor of 6 leaves room for the caption palette to be retuned without tripping, and sits
        // far enough above zero to catch a dead scan. Left at 6 deliberately: the remaining 9 are
        // one palette, and a floor that tracks a single allow-listed region this closely would fail
        // the next legitimate change to it rather than catching a broken scan.
        Assert.True(allowed >= 6,
            $"This clause matched only {allowed} allow-listed colour literals. It should be seeing "
            + "nine — a count this low means the regexes or the anchor lookback broke, not that the "
            + "app stopped constructing colours.");

        Assert.True(offenders.Count == 0,
            "A colour literal built in App code is off the governed path: ThemeService.ApplyTo cannot "
            + "reach it, so it survives every theme change unchanged. Bind a theme slot through "
            + "{DynamicResource} instead — Style + DataTrigger when the value depends on state (spec "
            + "§5.2). If the colour genuinely is not a theme colour, add it to AllowList with the "
            + "reason, the way spec §7's per-account identity paint is listed:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The XAML clause, added by item 3a after item 6's register pass found a FIFTH status-colour
    /// site the spec never enumerated: the status bar's live-process dot, a raw
    /// <c>&lt;SolidColorBrush Color="#4FE08C" /&gt;</c> nested in a <c>Setter.Value</c> at
    /// <c>MainWindow.xaml:1877</c>, swapping to <c>#4A5C70</c> at zero clients — the same brand
    /// green and grey the deleted <c>StatusDotBrushConverter</c> held (F-088).
    /// <para>
    /// WHY IT COULD HIDE. Both fences item 3 shipped are structurally blind to it, for different
    /// reasons. The clause above walks <c>*.cs</c> and this is markup.
    /// <c>ContrastPairGateTests</c> reads <c>Background=</c> / <c>Foreground=</c> attributes and
    /// this is a literal nested two levels inside a style setter. A hex in XAML matched neither
    /// pattern, so nothing would have caught a sixth site being added tomorrow either.
    /// </para>
    /// <para>
    /// THE RULE THAT MAKES THIS WORTH HAVING: <b>a literal is permitted only when an open register
    /// row already owns it.</b> Every entry below cites the finding id that owns it, or spec §7 for
    /// the one set the spec itself puts out of the theme's reach. That is deliberately harder than
    /// "list the files we do not want to fix today" — an exemption nobody can trace back to a row is
    /// how the fifth site lived on <c>main</c> unnoticed since PR #96. The one entry with no finding
    /// is <c>App.xaml</c>'s brush dictionary, and it has none because it is not a defect: those
    /// eleven instances ARE the governed path's origin.
    /// </para>
    /// </summary>
    private sealed record AllowedXamlLiteral(string Path, string? Anchor, int Span, string Reason);

    /// <summary>
    /// A colour literal written into markup. Six or eight hex digits, which is every form this app
    /// actually uses — a sweep of App XAML on 2026-08-10 found no <c>#RGB</c> / <c>#ARGB</c>
    /// shorthand anywhere. The <c>(?&lt;![&amp;\w])</c> guard keeps XML character entities out:
    /// <c>&amp;#x2630;</c> and a six-digit decimal entity are both markup escapes, not colours, and
    /// this app writes ten of the former.
    /// <para>
    /// CORRECTED 2026-08-11 — this paragraph described the opposite of what the file does. It read:
    /// "It scans lines, so a hex inside an XML comment counts too. That is deliberate and it fired
    /// on its first run against this item's own comment." True when written at v1.17, and made
    /// false by v1.20's <see cref="StripXmlComment"/>, which was added for the opposite reason
    /// entirely — a ControlStyles comment quoting the Aero literals it had just replaced was being
    /// read as the crime. Comments have not counted since, and the ceiling below went a whole cycle
    /// without noticing.
    /// </para>
    /// <para>
    /// The ADVICE in the old paragraph is still right even though its mechanism is gone: a comment
    /// naming a shipped colour is a claim that goes false the moment a theme changes that value, so
    /// the hex belongs in the register row and the comment cites the row. It is now a convention
    /// this scanner does not enforce, which is worth knowing before relying on it.
    /// </para>
    /// </summary>
    private static readonly Regex XamlLiteralColour =
        new(@"(?<![&\w])#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b", RegexOptions.Compiled);

    /// <summary>
    /// Where a hex may sit in App XAML, and the open row that owns it.
    /// <para>
    /// <c>Path</c> is the repo-relative file, or a directory prefix when it ends in <c>/</c>.
    /// <c>Anchor</c> must appear on the offending line or within <c>Span</c> lines above it; a null
    /// <c>Anchor</c> exempts the whole file, which is used ONLY where an open row's own evidence
    /// covers the whole file. <c>MainWindow.xaml</c> never gets a whole-file entry — it is the file
    /// the fifth site hid in, so blanket-exempting it would blind this fence in the one place it
    /// exists to watch.
    /// </para>
    /// </summary>
    private static readonly AllowedXamlLiteral[] XamlAllowList =
    [
        new("src/ROROROblox.App/App.xaml", "<SolidColorBrush x:Key=", 0,
            "NOT a finding, and the only entry here that has none. These eleven instances are the "
            + "governed path's own origin: ten theme slots plus the derived InteractiveEdgeBrush "
            + "fallback, and ThemeService.ApplySlot REPLACES each instance on every theme change "
            + "(ThemeService.cs:262-269). The hex in the markup is the pre-startup value of a brush "
            + "the theme owns, not a colour that escaped it. Flagging these would flag the mechanism."),

        // F-089's entry was retired by item 3b, which rebound SelectionDotStyle's four hexes to
        // MutedTextBrush and CyanBrush. It is deliberately not replaced by a narrower entry: an
        // allow-list entry that outlives the finding it cites is the defect
        // NoExemptionOutlivesItsFinding exists to catch, one layer up. A closed row grants nothing,
        // so a new literal in that ControlTemplate now fails this clause instead of inheriting a
        // permission it never earned.

        // F-066's MainWindow entry was retired by v1.21 item 12, and the way it emptied is the
        // point. It covered five hex occurrences on three lines: Foreground #F1B232 on the
        // contested-lock banner, then #17D4FA on #0F1F31 and #FFFFFF on #22314A across the two
        // recovery buttons. FOUR of the five left in v1.20 when those buttons took CtaButtonStyle
        // and SecondaryButtonStyle — a button sweep quietly closing most of a colour row, which
        // nothing announced and nothing re-counted. Item 12 bound the fifth to RowExpiredAccent,
        // measured on the page field at 8.85 / 7.23 / 9.85 / 12.84.
        // MainWindow.xaml now holds ZERO colour literals, so this file's most-watched region is
        // clean for the first time. Fourth retirement this cycle, on the same rule as F-085's,
        // About's narrowing and F-087's: an entry that outlives its literals is a permission
        // nobody re-earned.

        // F-085's entry was retired by v1.21 item 2, which rebound the Bloxstrap banner's three
        // literals — Background #3F3000, BorderBrush #8F7000, body Foreground #FFE3A6 — onto the
        // same RowExpiredBg / RowExpiredAccent recipe the compat banner already used. Retired the
        // same way F-089's was and for the same reason: an allow-list entry that outlives the
        // literals it cites is a standing permission nobody re-earned, so a new hex in that banner
        // now fails the offender clause instead of inheriting a badge. The row itself stays open
        // until item 12 flips it, which is the register's rule, not this file's.

        // AboutWindow.xaml was a WHOLE-FILE entry until v1.21 item 5, on the grounds that F-063's
        // surface was the whole window. That is no longer true: item 4 bound the two grounds the row
        // was actually about, so what remains is artwork, and artwork earns a narrow entry naming
        // what it is rather than a blanket one. The narrowing is the point — a whole-file exemption
        // on this file is what let a theme slot sit inside the mark for a cycle and a half
        // (AboutArtworkTests found it), and it would equally have hidden the next literal typed into
        // any other part of the window.

        new("src/ROROROblox.App/About/AboutWindow.xaml", "<Window.Resources>", 9,
            "Spec §7 (invariant 2) — the 626 Labs mark, NOT a finding and not debt. Eight keyed "
            + "brushes painting the nine faces of the iso voxel stack: the cyan trio, the magenta "
            + "pair, the teal pair and the soft navy. Same category as the per-account caption "
            + "palette allow-listed above — these paint WHO this product is, not WHAT STATE "
            + "something is in, and a themed logo is a broken logo. AboutArtworkTests holds the "
            + "line in both directions: no face may take a theme slot, and the plate under them "
            + "must. Anchored on the resource block with a span that reaches the eighth brush, so a "
            + "ninth added below the anchor's reach is NOT covered and has to justify itself."),

        new("src/ROROROblox.App/About/AboutWindow.xaml", "<TextBlock.Effect>", 2,
            "Spec §7, same invariant, different surface. The Easter-egg glow is a DropShadow in the "
            + "brand magenta, deliberately the fixed brand hue rather than the theme's Magenta slot "
            + "— it is the reward for finding the egg, and under flatline the theme value is a dark "
            + "achromatic grey that would render the glow invisible. Two-line span: the effect and "
            + "the element that owns it, nothing else."),

        new("src/ROROROblox.App/CookieCapture/CookieCaptureWindow.xaml", null, 0,
            "F-066, open. That row's surface is literally 'Modals/CookieCapture vs rest' and its "
            + "62-hex count is where these fourteen live. Whole-file for the same reason: the row "
            + "owns the file, not a line in it."),

        new("src/ROROROblox.App/Modals/", null, 0,
            "F-079 and F-066, both open. F-079 names StopAllConfirmWindow, LeftoverProcessesWindow, "
            + "RobloxAlreadyRunningWindow, RobloxNotInstalledWindow, WebView2NotInstalledWindow and "
            + "RenameWindow, and its verdict is exactly this defect — modals that opt out of theming "
            + "and render brand colours on a themed field. DpapiCorruptWindow is the one file in the "
            + "folder F-079 does not name; F-066's 'mostly in out-of-scope modals' count owns it. "
            + "Directory prefix rather than seven near-identical entries, and the ceiling below is "
            + "what stops a new modal quietly widening it."),
    ];

    /// <summary>
    /// Measured against the tree on 2026-08-10, after item 3a rebound F-088's two literals and item
    /// 3b rebound F-089's four: 97 allowed hex occurrences. App.xaml 11 (the seed dictionary, and
    /// only that — SelectionDotStyle's four are gone), MainWindow.xaml 8 (5 mutex-recovery +
    /// 3 Bloxstrap), AboutWindow.xaml 10, CookieCaptureWindow.xaml 14, Modals/ 54 — occurrences,
    /// not lines: several of those lines set a foreground and a background in one attribute pair.
    /// <para>
    /// The drop from 101 is F-089 closing, and the ceiling moved with it on purpose. A ceiling that
    /// keeps room for a closed row's literals is an open invitation to re-add them.
    /// </para>
    /// <para>
    /// A CEILING, not just a floor, and that is the point of it. An allow-listed region is a region
    /// a row has already counted; a literal added inside one is a NEW literal wearing an old row's
    /// badge, and the offender list would never see it. F-032 went from 11 offending controls at
    /// audit to 15 while two waves built machinery around it and nobody re-counted — the register's
    /// own rule about a row not being a static thing, enforced here in arithmetic instead of prose.
    /// </para>
    /// </summary>
    /// <summary>
    /// Re-measured 2026-08-11 after v1.18 item 10: <b>95, down from 97</b>. The Stop all confirm's
    /// destructive button gave up its raw fill and foreground when it took
    /// <c>DestructiveButtonStyle</c>, so two of <c>Modals/</c>'s 54 occurrences moved onto the
    /// governed path. F-079 does not close — its other six modals are untouched — but the two
    /// literals it counted are gone, and the ceiling moves with them for the same reason it moved
    /// when F-089 closed: slack left inside an allow-listed region is room for a new literal to
    /// arrive wearing an old row's badge, which is the one thing the offender list above cannot see.
    /// F-079's own count needs the same correction; that belongs to the register pass, not here.
    /// </summary>
    /// <summary>
    /// <b>RE-DERIVED FROM THE TREE 2026-08-11 by v1.21 item 5: 58, down from 95 — and 33 of that
    /// drop had already happened before this cycle touched anything.</b>
    /// <para>
    /// THE CEILING HAD BEEN CARRYING SLACK FOR A WHOLE CYCLE, and the mechanism is worth naming
    /// because this constant exists to prevent exactly it. 95 was measured at v1.18. v1.20 then
    /// added <see cref="StripXmlComment"/> — because a ControlStyles comment QUOTING the Aero
    /// literals it had just replaced was being read as the crime — and that change removed every
    /// commented hex from this count without anyone re-deriving the number. v1.20's button sweep
    /// separately took four literals out of <c>MainWindow.xaml</c>'s two recovery buttons. The
    /// assertion is <c>allowed &lt;= ceiling</c>, so both improvements were invisible: the gate went
    /// green while the room it was leaving grew to 33 unaccounted occurrences. Measured at this
    /// cycle's branch point the real figure was <b>62</b> against a ceiling of 95.
    /// </para>
    /// <para>
    /// That is this constant's own thesis arriving from the direction it was not watching. The
    /// doc above says slack "is an open invitation to re-add them" and worries about the count
    /// growing; nobody considered that a ceiling never lowered after a fix is the same hole, and a
    /// ceiling is only a ceiling while somebody re-derives it.
    /// </para>
    /// <para>
    /// Derived, not adjusted, per the register's rule — App.xaml 11 + CookieCaptureWindow 14 +
    /// Modals/ 23 + AboutWindow 9 + MainWindow 0, counted outside comments, reconciling exactly
    /// with what this clause reports. This cycle removed five: the Bloxstrap banner's three
    /// (item 2), AboutWindow's card ground (item 4), and the contested-lock banner's
    /// <c>#F1B232</c> (item 12, closing F-066's in-scope residue).
    /// <b><c>MainWindow.xaml</c> now holds ZERO colour literals</b> — the file this fence was built
    /// to watch, and the one it has never been able to blanket-exempt, is clean for the first time.
    /// </para>
    /// </summary>
    private const int AllowedXamlLiteralCeiling = 57;

    /// <summary>
    /// Vacuity floor. Well under the ceiling so that genuinely CLOSING F-079 or F-066 — which would
    /// delete 53 and 14 hits respectively — does not turn a fix into a red build, while still
    /// catching a repo-root walk that found nothing.
    /// </summary>
    private const int AllowedXamlLiteralFloor = 25;

    private static IEnumerable<(string RelativePath, string[] Lines)> AppXaml()
    {
        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            yield return (file.Label, File.ReadAllLines(file.FullPath));
        }
    }

    private static AllowedXamlLiteral? AllowedXamlAt(string relativePath, string[] lines, int index)
    {
        foreach (var entry in XamlAllowList)
        {
            var fileMatches = entry.Path.EndsWith('/')
                ? relativePath.StartsWith(entry.Path, StringComparison.OrdinalIgnoreCase)
                : string.Equals(entry.Path, relativePath, StringComparison.OrdinalIgnoreCase);
            if (!fileMatches) continue;

            if (entry.Anchor is null) return entry;

            for (var i = index; i >= 0 && i >= index - entry.Span; i--)
            {
                if (lines[i].Contains(entry.Anchor, StringComparison.Ordinal)) return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// The fifth-site clause. Item 3 rebound four status-colour sites and the app had five; this is
    /// what makes a sixth impossible to add quietly, in the one language the other two fences do not
    /// read.
    /// </summary>
    [Fact]
    public void NoColourLiteralIsWrittenIntoAppXamlOutsideTheAllowList()
    {
        var offenders = new List<string>();
        var allowed = 0;

        foreach (var (relativePath, lines) in AppXaml())
        {
            var inComment = false;
            for (var i = 0; i < lines.Length; i++)
            {
                // A hex inside <!-- --> renders nothing and no theme change can reach it, so it
                // cannot be the defect this fence exists to catch. See StripXmlComment — added
                // when ControlStyles.xaml grew a comment QUOTING the Aero literals the v1.20
                // template replaced, as the evidence for replacing them, and this fence read the
                // evidence as the crime.
                var codeOnly = StripXmlComment(lines[i], ref inComment);
                if (codeOnly.Trim().Length == 0) continue;

                var matches = XamlLiteralColour.Matches(codeOnly);
                if (matches.Count == 0) continue;

                if (AllowedXamlAt(relativePath, lines, i) is not null)
                {
                    allowed += matches.Count;
                    continue;
                }

                var values = string.Join(", ", matches.Select(m => m.Value));
                offenders.Add($"{relativePath}:{i + 1}  [{values}]  {lines[i].Trim()}");
            }
        }

        // Offenders first, deliberately. The count assertions below are integrity checks on this
        // clause; this one is the clause. A secondary failure that masks "MainWindow.xaml:1877 holds
        // a brand hex" would hand the reader a number where a file and a line were available.
        Assert.True(offenders.Count == 0,
            "A colour literal written into App XAML is off the governed path: ThemeService.ApplyTo "
            + "replaces brush INSTANCES in the resource dictionary, and a hex typed into markup is "
            + "not one of them, so it survives every theme change unchanged. Bind a theme slot "
            + "through {DynamicResource} instead — Style + DataTrigger when the value depends on "
            + "state (spec §5.2, §5.3), which is what the status bar's live-process dot now does. "
            + "An exemption is available, but only on the register's terms: a literal is permitted "
            + "here only when an OPEN finding already owns it, and the entry cites that finding id "
            + "with its reason inline. If no row owns it, the answer is a new row (F-087 and F-089 "
            + "are both that), not a new entry:\n  "
            + string.Join("\n  ", offenders));

        Assert.True(allowed >= AllowedXamlLiteralFloor,
            $"This clause matched only {allowed} allow-listed colour literals in App XAML. It should "
            + $"be seeing {AllowedXamlLiteralCeiling} — a count this low means the XAML walk or the "
            + "anchor lookback broke, not that the app stopped writing hexes into markup.");

        Assert.True(allowed <= AllowedXamlLiteralCeiling,
            $"An allow-listed region of App XAML now holds {allowed} colour literals, up from the "
            + $"{AllowedXamlLiteralCeiling} measured on 2026-08-10. Every entry on this list is a "
            + "region an OPEN register row has already counted, so a new literal inside one is a new "
            + "defect wearing an old row's id — and the offender list above will never see it. "
            + "Re-count, update the row with the new number and the direction, and move this "
            + "constant. Do not move the constant alone.");
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

        // Same backstop for the XAML clause's walk, off XamlStyleScanner's enumerator rather than a
        // second copy of the same directory logic. The app ships 30 .xaml files outside obj/bin.
        var xaml = AppXaml().Count();
        Assert.True(xaml >= 20,
            $"The XAML walk found only {xaml} App .xaml files. It should be seeing roughly 30 — "
            + "the repo-root walk is broken, not the app.");
    }
}
