using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Adapts <see cref="IRobloxProcessTracker"/> onto <see cref="IPluginAccountStopper"/>.
///
/// <para>The stop sequence mirrors the main window's per-account close button exactly:
/// <c>RequestClose</c> (graceful CloseMainWindow) and, only if that fails,
/// <c>Kill</c> — which the tracker's own docs mark as "use only as a fallback".</para>
///
/// <para>Account ids cross the plugin boundary as strings; the tracker keys on Guid.
/// An unparseable id is a false return, never a throw, matching AccountActivityMarker's
/// defensive-parse posture.</para>
/// </summary>
public sealed class ProcessTrackerAccountStopper : IPluginAccountStopper
{
    private readonly IRobloxProcessTracker _tracker;

    public ProcessTrackerAccountStopper(IRobloxProcessTracker tracker)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
    }

    public IReadOnlyList<string> TrackedAccountIds =>
        _tracker.Attached.Keys.Select(id => id.ToString()).ToList();

    public bool StopAccount(string accountId)
    {
        if (!Guid.TryParse(accountId, out var id)) return false;
        if (!_tracker.IsTracking(id)) return false;

        try
        {
            return _tracker.RequestClose(id) || _tracker.Kill(id);
        }
        catch
        {
            // A dying process can race us between IsTracking and the close call.
            // Degrade to "failed for this account" rather than failing the whole batch.
            return false;
        }
    }
}
