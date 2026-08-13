using System.Diagnostics;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Refuses to render while a RoRoRo instance is running, because the failure it prevents is
/// unreadable.
///
/// <para><b>What happens without this guard.</b> With <c>ROROROblox.App.exe</c> up, every render
/// path throws <c>NotSupportedException: The URI prefix is not recognized</c> out of
/// <c>WebRequest.Create</c>, from inside <c>PackWebRequest.GetRequest</c>, while constructing
/// <c>Wpf.Ui.Markup.ThemesDictionary</c>. Around 100 gate tests fail at once — including
/// <c>ContrastPairGateTests</c> and <c>ButtonStateGateTests</c>, the two the keystone leans on
/// hardest — and often the test host crashes outright and aborts the run partway. Nothing in that
/// output points at the running app.</para>
///
/// <para><b>Measured 2026-08-12, both directions.</b> App running: 103 failed, 98 failed, host
/// crash, then 99 / 100 / 100. App closed: 1643 of 1643, eight consecutive runs. Rendering tests
/// run alone with it up crash after three tests, so this is environmental rather than a
/// test-ordering race inside the suite.</para>
///
/// <para><b>The mechanism is still unknown, and this is not a fix.</b> Ruled out by measurement:
/// CPU load (three runs under full 16-core saturation, 3x slower, all clean); the
/// "Roblox is already running" startup modal (constructed only inside <c>OnStartup</c>, which the
/// render harness never raises because it never calls <c>Run()</c>); test ordering; and forcing the
/// pack WebRequest factory to register via an explicit Application (changed nothing — same
/// exception, same line). See F-105. This guard converts a mystifying failure into an actionable
/// one and buys nothing else.</para>
///
/// <para><b>CI is unaffected</b> — a runner has no RoRoRo running — so this is a dev-loop
/// guard.</para>
/// </summary>
internal static class RenderEnvironment
{
    /// <summary>The process name of a running RoRoRo, debug or installed.</summary>
    private const string AppProcessName = "ROROROblox.App";

    private static readonly Lazy<string?> Offender = new(FindRunningApp, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Throws with an actionable message when a RoRoRo instance is running. Called from the two
    /// entry points that would otherwise fail incomprehensibly: <c>ThemedRender</c>'s static
    /// constructor and <c>WindowRenderHost</c>'s thread body.
    /// <para>
    /// The scan is done once and cached. Starting the app halfway through a run is not a case worth
    /// paying a process enumeration per render for, and a stale answer here is a worse failure than
    /// the one being prevented.
    /// </para>
    /// </summary>
    public static void RequireClean()
    {
        if (Offender.Value is not { } detail)
        {
            return;
        }

        throw new InvalidOperationException(
            $"A RoRoRo instance is running ({detail}), and the render gates cannot run while it is. "
            + "They fail with 'The URI prefix is not recognized' from deep inside WPF's pack: URI "
            + "resolution, roughly 100 at a time, and frequently take the test host down with them. "
            + "Nothing in that output names the running app, which is why this check exists. "
            + "Close RoRoRo and run again. "
            + "This is a dev-loop condition only; CI has no instance running. The underlying "
            + "mechanism is unsolved and tracked as F-105 in the findings register.");
    }

    private static string? FindRunningApp()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(AppProcessName);
        }
        catch
        {
            // A scan failure must not be the reason a suite refuses to run. If we cannot tell, let
            // the render proceed and fail on its own terms.
            return null;
        }

        try
        {
            if (processes.Length == 0)
            {
                return null;
            }

            var pids = string.Join(", ", processes.Select(p => p.Id));
            return processes.Length == 1 ? $"pid {pids}" : $"{processes.Length} instances, pids {pids}";
        }
        finally
        {
            // Same anti-leak discipline as RobloxRunningProbe and the tracker.
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }
}
