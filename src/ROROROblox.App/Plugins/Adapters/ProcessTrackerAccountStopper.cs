using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Adapts <see cref="IRobloxProcessTracker"/> onto <see cref="IPluginAccountStopper"/>.
///
/// <para>THE OLD SHAPE WAS <c>RequestClose(id) || Kill(id)</c> — the same inert guard F-111
/// measured on the Stop button, reached here by a different route. <c>RequestClose</c> answers
/// "was there a window to ask", never "did it close": an in-game client answers SC_CLOSE by
/// raising Roblox's OWN confirm dialog, drawn inside the game surface where nothing on this side
/// can see or click it. So the call returned true, the short-circuit skipped <c>Kill</c>, and the
/// stop was reported as issued while the client ran on. Measured live 2026-08-22 against the MCP
/// connector: alive and still growing ninety seconds later.</para>
///
/// <para>This now runs <see cref="ClientStopSequence"/> — the same sequence the per-account Stop
/// button runs, which is why that type lives in Core with an injected delay and no UI of its own.
/// Ask, wait, ask once more, re-check <c>HasExited</c> on every tick, force only when the grace is
/// spent. The previous comment here claimed parity with the main window "exactly"; it was true
/// when written and quietly stopped being true when F-111 moved the UI path and left this one
/// behind. Parity is now structural rather than asserted.</para>
///
/// <para>Account ids cross the plugin boundary as strings; the tracker keys on Guid.
/// An unparseable id is a false return, never a throw, matching AccountActivityMarker's
/// defensive-parse posture.</para>
/// </summary>
public sealed class ProcessTrackerAccountStopper : IPluginAccountStopper
{
    private readonly IRobloxProcessTracker _tracker;
    private readonly IAppSettings? _settings;
    private readonly ILogger<ProcessTrackerAccountStopper>? _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>
    /// One sequence per account at a time. A plugin that calls stop twice should not get two
    /// countdowns racing each other to kill the same pid.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, Task> _inFlight = new();

    /// <remarks>
    /// ONE constructor, required dependency first, seams optional — the shape
    /// TypedHttpClientRegistrationTests exists to enforce elsewhere and the one that keeps
    /// container resolution unambiguous.
    /// </remarks>
    public ProcessTrackerAccountStopper(
        IRobloxProcessTracker tracker,
        IAppSettings? settings = null,
        ILogger<ProcessTrackerAccountStopper>? log = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _settings = settings;
        _log = log;
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
    }

    public IReadOnlyList<string> TrackedAccountIds =>
        _tracker.Attached.Keys.Select(id => id.ToString()).ToList();

    public bool StopAccount(string accountId)
    {
        if (!Guid.TryParse(accountId, out var id)) return false;
        if (!_tracker.IsTracking(id)) return false;

        // Issued, not completed — the contract this interface has always documented and what the
        // caller is told ("confirm with running_status"). What was missing was never the wording;
        // it was the escalation that makes the wording true.
        _inFlight.GetOrAdd(id, key => RunAndForgetAsync(key));
        return true;
    }

    private async Task RunAndForgetAsync(Guid id)
    {
        try
        {
            var grace = ClientStopSequence.DefaultGrace;
            if (_settings is not null)
            {
                try
                {
                    // "Stop clients immediately, without waiting" is a user's standing answer to
                    // this exact question. It should not depend on who is asking.
                    if (await _settings.GetAutoForceStopAsync().ConfigureAwait(false))
                    {
                        grace = TimeSpan.Zero;
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogDebug(ex, "Auto-force-stop read failed; using the default wait.");
                }
            }

            var outcome = await RunStopSequenceAsync(id, grace).ConfigureAwait(false);
            _log?.LogInformation("Plugin StopAccount {AccountId} finished: {Outcome}.", id, outcome);
        }
        catch (Exception ex)
        {
            // Nothing awaits this task, so an unlogged throw here would be a stop that vanished.
            _log?.LogWarning(ex, "Plugin StopAccount {AccountId} failed.", id);
        }
        finally
        {
            _inFlight.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// The sequence itself, separated so tests can await it without racing a fire-and-forget task.
    /// </summary>
    internal Task<ClientStopOutcome> RunStopSequenceAsync(
        Guid id, TimeSpan grace, CancellationToken ct = default)
        => ClientStopSequence.RunAsync(
            hasExited: () => !_tracker.IsTracking(id),
            askToClose: () => _tracker.RequestClose(id),
            forceKill: () => _tracker.Kill(id),
            onSecondsRemaining: _ => { },   // no row to count down out here
            delay: _delay,
            grace: grace,
            ct: ct);

    /// <summary>Test observation point for the fire-and-forget started by StopAccount.</summary>
    internal Task? InFlightFor(Guid id) => _inFlight.TryGetValue(id, out var t) ? t : null;
}
