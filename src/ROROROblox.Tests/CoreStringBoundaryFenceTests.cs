using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// The Core string boundary fence
/// (docs/superpowers/specs/2026-09-05-core-string-boundary-design.md): Core hands the App a
/// key and data, never prose. This is the UseCookies=false class of regression — a Core type
/// that grows a composed <c>Message</c> again compiles green, renders fine in English, and
/// quietly rebuilds the wall Phase C of the localization plan cannot translate through. So the
/// signature is banned at the source level: no Core type may declare a member or positional
/// parameter named <c>Message</c>. (Exception ctors take lowercase <c>message</c> — inherited
/// diagnostics, allowed by design; the App's <c>CoreMessageCatalog</c> is where the sentences
/// live.) Migrated to zero 2026-09-05: LaunchResult, CookieCaptureResult, WebhookUrlVerdict,
/// PhoneCredentialVerdict, and SessionHistoryStatus's prose all crossed the boundary in one
/// move — this fence keeps the count at zero rather than ratcheting toward it.
/// </summary>
public class CoreStringBoundaryFenceTests
{
    // Matches a declared member or positional record parameter of type string named Message —
    // "string Message", "string? Message" — but not lowercase ctor params or unrelated words.
    private static readonly Regex MessageMember = new(@"\bstring\??\s+Message\b", RegexOptions.Compiled);

    [Fact]
    public void NoCoreTypeCarriesAComposedMessage()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx above the test assembly.");

        var coreDir = Path.Combine(root!, "src", "ROROROblox.Core");
        var offenders = Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => MessageMember.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root!, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Core declares a `string Message` member — prose belongs to the App's CoreMessageCatalog, "
            + "Core hands over an enum Kind plus data (see the Core string boundary spec). Offenders: "
            + string.Join(", ", offenders));
    }
}
