using System.IO;

namespace ROROROblox.Core;

/// <summary>
/// Finds the newest installed <c>RobloxPlayerBeta.exe</c> across the standalone
/// (<c>%LOCALAPPDATA%\Roblox\Versions</c>) and Microsoft-Store/UWP
/// (<c>%LOCALAPPDATA%\Packages\ROBLOXCORPORATION.ROBLOX_*\LocalCache\Local\Roblox\Versions</c>)
/// install layouts.
///
/// <para>Deliberately narrow: it locates the player binary so the seamless-takeover flow can put a
/// tray client back after reclaiming the singleton. "Newest by last-write" mirrors
/// <c>ClientAppSettingsWriter</c>'s own version-folder selection; the two are kept as separate
/// small readers rather than one shared abstraction to avoid destabilizing that well-tested class
/// for a best-effort relaunch.</para>
/// </summary>
public static class RobloxInstallLocator
{
    private static string DefaultStandaloneRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");

    private static string DefaultPackagesRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");

    /// <summary>Newest <c>RobloxPlayerBeta.exe</c> full path, or null when no install is found.</summary>
    public static string? FindNewestPlayerBeta()
        => FindNewestPlayerBeta(DefaultStandaloneRoot(), DefaultPackagesRoot());

    /// <summary>Testable overload — roots injected so a fixture tree can stand in for the real install.</summary>
    public static string? FindNewestPlayerBeta(string standaloneVersionsRoot, string packagesRoot)
    {
        (string exe, DateTime writeUtc)? best = null;

        void Consider((string exe, DateTime writeUtc)? candidate)
        {
            if (candidate is null) return;
            if (best is null || candidate.Value.writeUtc > best.Value.writeUtc) best = candidate;
        }

        Consider(NewestInVersionsRoot(standaloneVersionsRoot));

        if (Directory.Exists(packagesRoot))
        {
            foreach (var pkg in SafeEnumerate(packagesRoot, "ROBLOXCORPORATION.ROBLOX_*"))
            {
                Consider(NewestInVersionsRoot(Path.Combine(pkg, "LocalCache", "Local", "Roblox", "Versions")));
            }
        }

        return best?.exe;
    }

    private static (string exe, DateTime writeUtc)? NewestInVersionsRoot(string versionsRoot)
    {
        if (!Directory.Exists(versionsRoot)) return null;
        (string exe, DateTime writeUtc)? best = null;
        foreach (var dir in SafeEnumerate(versionsRoot, "version-*"))
        {
            var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
            if (!File.Exists(exe)) continue;
            var lastWrite = File.GetLastWriteTimeUtc(exe);
            if (best is null || lastWrite > best.Value.writeUtc) best = (exe, lastWrite);
        }
        return best;
    }

    private static IEnumerable<string> SafeEnumerate(string root, string pattern)
    {
        try { return Directory.EnumerateDirectories(root, pattern); }
        catch { return Array.Empty<string>(); }
    }
}
