using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.ViewModels;

/// <summary>
/// Formats one account row's memory chip text from a watchdog reading. Pure — no I/O, no VM
/// state — so the rendering rules (Task 7) are unit-testable without a running app.
/// </summary>
public static class MemoryChipFormatter
{
    /// <summary>
    /// Renders bytes as GB always. Only appends a "· ~N min" countdown when BOTH
    /// <paramref name="warned"/> (the watchdog latched a pressure crossing this sample) AND
    /// <paramref name="hasProjection"/> (the growth-rate math actually resolved) are true — never
    /// render a countdown derived from arithmetic the watchdog could not complete. Returns
    /// <see langword="null"/> when <see cref="AccountMemory.ReadOk"/> is false: an unreadable pid
    /// renders nothing, never a stale or zero figure.
    /// </summary>
    public static string? Format(AccountMemory account, bool warned, bool hasProjection, int minutesToCeiling)
    {
        if (!account.ReadOk)
        {
            return null;
        }

        var gb = account.PrivateBytes / 1024d / 1024d / 1024d;
        if (!warned)
        {
            return $"{gb:F1} GB";
        }

        return hasProjection
            ? $"▲ {gb:F1} GB · ~{minutesToCeiling} min"
            : $"▲ {gb:F1} GB";
    }
}
