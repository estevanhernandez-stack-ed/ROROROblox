namespace ROROROblox.App.ViewModels;

/// <summary>Formats the passive idle-summary banner strip. Empty when none.</summary>
public static class IdleSummary
{
    public static string Format(int count, int thresholdMinutes)
    {
        if (count <= 0) return string.Empty;
        var noun = count == 1 ? "account" : "accounts";
        return $"{count} {noun} idle > {thresholdMinutes}m";
    }
}
