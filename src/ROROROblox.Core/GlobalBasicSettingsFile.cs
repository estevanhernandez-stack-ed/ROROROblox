using System.Text.RegularExpressions;

namespace ROROROblox.Core;

/// <summary>
/// One definition of "which GlobalBasicSettings file is the active one", shared by
/// <see cref="GlobalBasicSettingsWriter"/> and <see cref="GlobalBasicSettingsProbe"/>.
/// <para>
/// Extracted deliberately. A writer and a reader that resolve the target independently can
/// silently disagree, which is exactly the shape of the ClientAppSettingsWriter defect logged in
/// docs/features.md — writes landing in a folder nothing reads, with no error and no symptom.
/// </para>
/// </summary>
internal static partial class GlobalBasicSettingsFile
{
    [GeneratedRegex(@"^GlobalBasicSettings_(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex NamePattern();

    /// <summary>
    /// The highest-numbered <c>GlobalBasicSettings_&lt;N&gt;.xml</c> under <paramref name="root"/>.
    /// The <c>_Studio</c> variant is excluded by the pattern — it belongs to Roblox Studio and
    /// writing to it would do nothing. Returns null when the directory or the file is absent.
    /// </summary>
    internal static FileInfo? Resolve(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        FileInfo? best = null;
        var bestN = -1;

        foreach (var path in Directory.EnumerateFiles(root, "GlobalBasicSettings_*.xml"))
        {
            var info = new FileInfo(path);
            var match = NamePattern().Match(info.Name);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var n))
            {
                continue;
            }

            if (n > bestN)
            {
                bestN = n;
                best = info;
            }
        }

        return best;
    }
}
