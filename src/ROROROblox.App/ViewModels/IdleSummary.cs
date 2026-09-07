using ROROROblox.App.Localization;

namespace ROROROblox.App.ViewModels;

/// <summary>Formats the passive idle-summary banner strip. Empty when none.</summary>
public static class IdleSummary
{
    public static string Format(int count, int thresholdMinutes)
    {
        if (count <= 0) return string.Empty;
        return Loc.Plural("Shell_IdleSummary", count, thresholdMinutes);
    }
}
