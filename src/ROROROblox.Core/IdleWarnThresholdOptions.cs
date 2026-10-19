using System;

namespace ROROROblox.Core;

/// <summary>
/// The idle-warn thresholds the Settings picker offers, in minutes, and the rule that maps any
/// stored value onto one of them. One place, so the picker and <c>AppSettings</c> cannot drift:
/// a hand-edited <c>settings.json</c> holding 1 used to survive the getter, run the monitor at one
/// minute, and paint the picker as 15.
/// </summary>
public static class IdleWarnThresholdOptions
{
    /// <summary>Ascending. The Settings picker's <c>ComboBoxItem</c> tags must match (a test pins it).</summary>
    public static readonly int[] Offered = [10, 12, 15, 18];

    /// <summary>What a fresh install, or a non-positive stored value, gets.</summary>
    public const int Default = 15;

    /// <summary>
    /// Maps a stored value to the nearest offered one. A tie (11, between 10 and 12) takes the
    /// lower value, the earlier warning. Zero and negatives are not a preference, they are a bad
    /// write, and take <see cref="Default"/>.
    /// </summary>
    public static int Normalise(int minutes)
    {
        if (minutes <= 0)
        {
            return Default;
        }

        var best = Offered[0];
        foreach (var option in Offered)
        {
            if (Math.Abs(option - minutes) < Math.Abs(best - minutes))
            {
                best = option;
            }
        }
        return best;
    }
}
