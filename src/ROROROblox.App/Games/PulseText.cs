using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using ROROROblox.Core.Library;

namespace ROROROblox.App.Games;

/// <summary>
/// The words for a game's pulse, shared by the Library and the account panel. Each half is its own
/// string so a surface can draw one alone (the grid tile shows the count only); <see cref="Joined"/>
/// joins both with the Library's existing meta joiner and either drops on its own.
/// </summary>
internal static class PulseText
{
    /// <summary>A last update older than this reads as a month and year; inside it, as the "last played" ladder.</summary>
    private const int RelativeMonths = 11;

    /// <summary>"81,827 playing", grouped by the app's language. Zero reads "0 playing". Empty when the count is absent or the entry is a private server.</summary>
    internal static string Count(GamePulseView? pulse, bool isPrivateServer)
        => !isPrivateServer && pulse is { Playing: { } n }
            ? Loc.Format("Library_Playing", n.ToString("N0", TranslationSource.Instance.CurrentCulture))
            : string.Empty;

    /// <summary>"updated 2 days ago", or "updated Mar 2023" past eleven months. Empty when the date is absent.</summary>
    internal static string Updated(GamePulseView? pulse)
    {
        if (pulse is not { UpdatedUtc: { } updated })
        {
            return string.Empty;
        }

        var when = updated > AccountSummary.UtcNow.AddMonths(-RelativeMonths)
            ? AccountSummary.RelativeAgo(updated)
            : DisplayDate.MonthYear(updated);
        return Loc.Format("Library_Pulse_Updated", when);
    }

    /// <summary>Both halves joined, whichever is present alone, or empty.</summary>
    internal static string Joined(GamePulseView? pulse, bool isPrivateServer)
    {
        var count = Count(pulse, isPrivateServer);
        var updated = Updated(pulse);
        if (count.Length == 0) return updated;
        if (updated.Length == 0) return count;
        return Loc.Format("Library_Meta_Played", count, updated);
    }
}
