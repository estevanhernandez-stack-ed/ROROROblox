using System.IO;

namespace ROROROblox.App.Localization;

/// <summary>
/// The one-time "RoRoRo now follows your system language" tray notice, gated by a sentinel file
/// next to <c>accounts.dat</c> — the same show-once mechanism <c>WelcomeWindow</c> uses for the
/// first-run tour (<c>.welcome-shown</c>).
/// <para>
/// WHY IT EXISTS. v1.27 is the first version whose whole UI is localized, and the culture bootstrap
/// (<see cref="UiCulture.ApplyFromSettings"/>) follows the OS language for anyone who never picked
/// one — so a returning user on non-English Windows opens the update and the app is suddenly in
/// their language. That is the intended behaviour, but it deserves one quiet pointer: where to
/// change it (Settings › Language), which serves both the user who welcomes it and the one who
/// wants English back. Shown once per install, then never again.
/// </para>
/// </summary>
internal static class LanguageNoticeSentinel
{
    /// <summary>Sentinel written after the notice is shown. Absent = not yet shown. Sits with
    /// <c>accounts.dat</c>/<c>settings.json</c>/<c>.welcome-shown</c> in the per-user data dir.</summary>
    private static readonly string SentinelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        ".language-notice-shown");

    /// <summary>True when the notice has not yet been shown (sentinel absent).</summary>
    public static bool IsPending()
    {
        try
        {
            return !File.Exists(SentinelPath);
        }
        catch
        {
            // A failed LOCALAPPDATA probe must not spam the toast on every launch — assume shown.
            return false;
        }
    }

    /// <summary>Drop the sentinel so later launches don't re-show the notice.</summary>
    public static void MarkShown()
    {
        try
        {
            var dir = Path.GetDirectoryName(SentinelPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(SentinelPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            // Best-effort. Worst case the notice shows once more next launch, never a crash.
        }
    }

    /// <summary>
    /// Whether to show the one-time notice this startup. Pure, so the rule is testable without
    /// touching <c>%LOCALAPPDATA%</c>. The caller must call <see cref="MarkShown"/> only when this
    /// returns true — marking unconditionally is the bug <c>WelcomeWindow</c> documents (a user who
    /// never saw it recorded that they had).
    /// <para>
    /// Gated on there being a language to point AT: with only the neutral (English) catalog shipping,
    /// "follows your language" would just mean English and the picker offers nothing else, so the
    /// notice would be noise. In shipping builds a satellite always ships, so this is a future-proof
    /// guard rather than a live branch.
    /// </para>
    /// </summary>
    internal static bool ShouldShow(bool pending, bool aLocalizedLanguageIsAvailable) =>
        pending && aLocalizedLanguageIsAvailable;
}
