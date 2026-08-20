using System.Diagnostics;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Refuses to render while something on this machine makes the result untrustworthy. Two
/// conditions, with different mechanisms and the same verdict.
///
/// <para><b>1. A RoRoRo instance is running</b> — the renders fail, unreadably. Detail below.</para>
///
/// <para><b>2. A Roblox client is running</b> — the renders are STARVED. A client saturates the GPU
/// and much of the CPU, and the render tests share one WPF host thread with a 60s per-render
/// budget, so a render that normally takes tens of milliseconds does not finish inside a minute.
/// Which tests trip depends on scheduling. Measured 2026-08-20 on one commit, the only variable
/// being whether a client was alive: 2 of 5 failed, then 1 of 5, then 1 of 5 on <c>main</c> — and
/// 5 of 5 passed in TWO SECONDS once the client was killed. This had been carried as an
/// unidentified flake for weeks, with re-runs going green on a quiet machine.</para>
///
/// <para><b>Why the client case refuses rather than tolerating a pass.</b> Unlike condition 1, the
/// renders sometimes succeed under a live client — so a hard refusal does invent failures that
/// would not otherwise have happened. That is the point. A pass under starvation is not evidence
/// the render is correct; it is evidence the machine was briefly free. Both outcomes are
/// untrustworthy, and the intermittency is precisely what hid this: "re-run it, it went green" was
/// always available. A gate whose result depends on whether you happen to be playing a game is not
/// a gate.</para>
///
/// <para><b>Why refuse rather than skip.</b> <c>WindowRenderFactAttribute</c> already skips nine of
/// these gates on CI, on an explicit promise: they still run on every developer machine and before
/// every release. If they ALSO skipped locally whenever Roblox was open, they could run nowhere —
/// CI skips them, the desktop skips them, and the release ships having never rendered. That is
/// F-105's own disease, which is a row about a suite printing green over a run that stopped early.
/// A refusal is loud and one second from fixed; a silent skip is how coverage disappears.</para>
///
/// <para>The RoRoRo case, in detail:</para>
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
/// <para><b>CI is unaffected by either condition</b> — a runner has neither RoRoRo nor a Roblox
/// client running — so this is a dev-loop guard on both counts.</para>
/// </summary>
internal static class RenderEnvironment
{
    /// <summary>The process name of a running RoRoRo, debug or installed.</summary>
    private const string AppProcessName = "ROROROblox.App";

    /// <summary>The Roblox client. Same name for every install flavour, Store or standalone.</summary>
    private const string ClientProcessName = "RobloxPlayerBeta";

    /// <summary>The reason rendering must not proceed, or null when the machine is clean.</summary>
    private static readonly Lazy<string?> Blocker = new(FindBlocker, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Throws with an actionable message when a RoRoRo instance or a Roblox client is running.
    /// Called from the two entry points that would otherwise fail incomprehensibly:
    /// <c>ThemedRender</c>'s static constructor and <c>WindowRenderHost</c>'s thread body.
    /// <para>
    /// The scan is done once and cached. Starting either process halfway through a run is not a
    /// case worth paying two process enumerations per render for, and a stale answer here is a
    /// worse failure than the one being prevented. The cache does mean a client launched mid-run
    /// goes unnoticed — accepted, because the realistic case is a client that was already up when
    /// someone typed the command.
    /// </para>
    /// </summary>
    public static void RequireClean()
    {
        if (Blocker.Value is { } reason)
        {
            throw new InvalidOperationException(reason);
        }
    }

    /// <summary>
    /// RoRoRo first: it is the condition where every render fails outright, so it is the more
    /// urgent thing to name when both are true.
    /// </summary>
    private static string? FindBlocker()
    {
        if (FindPids(AppProcessName) is { Count: > 0 } app)
        {
            return AppMessage(Describe(app));
        }

        if (FindPids(ClientProcessName) is { Count: > 0 } client)
        {
            return ClientMessage(Describe(client));
        }

        return null;
    }

    /// <summary>Renders a pid list the way both messages want to read it.</summary>
    internal static string Describe(IReadOnlyList<int> pids) =>
        pids.Count == 1 ? $"pid {pids[0]}" : $"{pids.Count} instances, pids {string.Join(", ", pids)}";

    internal static string AppMessage(string detail) =>
        $"A RoRoRo instance is running ({detail}), and the render gates cannot run while it is. "
        + "They fail with 'The URI prefix is not recognized' from deep inside WPF's pack: URI "
        + "resolution, roughly 100 at a time, and frequently take the test host down with them. "
        + "Nothing in that output names the running app, which is why this check exists. "
        + "Close RoRoRo and run again. "
        + "This is a dev-loop condition only; CI has no instance running. The underlying "
        + "mechanism is unsolved and tracked as F-105 in the findings register.";

    internal static string ClientMessage(string detail) =>
        $"A Roblox client is running ({detail}), and the render gates cannot be trusted while it "
        + "is. The client saturates the GPU, the gates share one WPF host thread with a 60s "
        + "per-render budget, and renders that normally take milliseconds do not finish inside a "
        + "minute. Which tests trip depends on scheduling, which is why this looked like flakiness "
        + "for weeks. Measured on one commit: 2 of 5 failed with a client up, 5 of 5 passed in two "
        + "seconds with it closed. "
        + "This refuses instead of reporting a result because a PASS under starvation is not "
        + "evidence either — it only means the machine was briefly free. "
        + "Close Roblox and run again. CI has no client running. "
        + "See docs/investigations/2026-08-20-render-test-flake-cause.md.";

    private static IReadOnlyList<int> FindPids(string processName)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            // A scan failure must not be the reason a suite refuses to run. If we cannot tell, let
            // the render proceed and fail on its own terms.
            return [];
        }

        try
        {
            return processes.Select(p => p.Id).ToList();
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
