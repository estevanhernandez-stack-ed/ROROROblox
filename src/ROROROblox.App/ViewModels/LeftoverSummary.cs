namespace ROROROblox.App.ViewModels;

/// <summary>Formats the LEFTOVER modal's split-aware body: windowless orphans (safe to clean) vs
/// open Roblox windows (live games). Always ends with the reassurance that multi-instance is fine
/// (RoRoRo already holds the lock in the Leftover case).</summary>
public static class LeftoverSummary
{
    public static string Format(int windowless, int windowed)
    {
        var clauses = new System.Collections.Generic.List<string>(2);
        if (windowless > 0)
            clauses.Add($"{windowless} leftover Roblox process{(windowless == 1 ? "" : "es")} with no window");
        if (windowed > 0)
            clauses.Add($"{windowed} open Roblox window{(windowed == 1 ? "" : "s")} from before");

        var found = string.Join(", and ", clauses);
        return $"Found {found}. Multi-instance is fine — RoRoRo has the lock.";
    }
}
