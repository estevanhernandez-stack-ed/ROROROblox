using ROROROblox.App.Localization;

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
            clauses.Add(Loc.Plural("Shell_Leftover_Windowless", windowless));
        if (windowed > 0)
            clauses.Add(Loc.Plural("Shell_Leftover_Windowed", windowed));

        var found = string.Join(Loc.Get("Shell_Leftover_Connector"), clauses);
        return Loc.Format("Shell_Leftover_Found", found);
    }
}
