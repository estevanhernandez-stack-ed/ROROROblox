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
    /// <summary>
    /// The footer line: how many clients are running and what they cost together (F-080).
    /// <para>
    /// The total is the number a multi-client user actually needs, and it lived nowhere in the app
    /// — getting it on 2026-08-07 took a PowerShell probe against the process list, while the
    /// watchdog had been sampling it every 15 seconds all along. Risk from N clients is aggregate;
    /// no per-row chip can show it.
    /// </para>
    /// <para>
    /// Marked with the same "▲" the row chips use when the machine is out of headroom, so one
    /// glance at the line you already read tells you the state.
    /// </para>
    /// </summary>
    public static string FormatFooter(int clientCount, long aggregateBytes, bool belowReserve)
    {
        var clients = clientCount switch
        {
            0 => "No Roblox clients running",
            1 => "1 Roblox client running",
            _ => $"{clientCount} Roblox clients running",
        };

        // No clients, or nothing readable yet: say nothing rather than "0.0 GB", which reads as a
        // measurement when it is really an absence of one.
        if (clientCount == 0 || aggregateBytes <= 0)
        {
            return clients;
        }

        var gb = aggregateBytes / 1024d / 1024d / 1024d;
        return belowReserve
            ? $"{clients} · ▲ {gb:F1} GB"
            : $"{clients} · {gb:F1} GB";
    }

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
