using System.ComponentModel;
using ROROROblox.App.Localization;
using ROROROblox.Core;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.App.KnownIssues;

/// <summary>
/// Known Roblox issues, worded for the main window and the page (spec §3). Singleton. Takes a
/// <see cref="KnownIssuesSnapshot"/> from any thread and marshals through <see cref="IUiDispatcher"/>;
/// everything it raises, it raises on the UI thread.
/// <para>
/// Suppressed while the contested-singleton warning shows: that warning is about RoRoRo working at
/// all and must not compete for attention (spec §3, corrected while planning).
/// </para>
/// </summary>
internal sealed class KnownIssuesNoticeModel : INotifyPropertyChanged
{
    private readonly KnownIssuesDismissals _dismissals;
    private readonly Func<Version?> _readRunningVersion;
    private readonly IUiDispatcher _ui;

    private IReadOnlyList<KnownIssue> _issues = [];
    private IReadOnlySet<string> _dismissed;
    private Version? _running;
    private KnownIssuesSource _source = KnownIssuesSource.None;
    private DateTimeOffset? _checkedAt;
    private KnownIssuesRefreshKind? _lastOutcome;
    private bool _suppressed;
    private KnownIssueNoticeSelection _selection = KnownIssueNoticeSelection.None;

    public KnownIssuesNoticeModel(KnownIssuesDismissals dismissals, Func<Version?> readRunningVersion, IUiDispatcher ui)
    {
        _dismissals = dismissals ?? throw new ArgumentNullException(nameof(dismissals));
        _readRunningVersion = readRunningVersion ?? throw new ArgumentNullException(nameof(readRunningVersion));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _dismissed = dismissals.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised on the UI thread after anything the page must redraw for.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<KnownIssue> PageIssues => KnownIssueNotice.PageOrder(_issues);

    public Version? RunningVersion => _running;

    public KnownIssuesSource Source => _source;

    public DateTimeOffset? CheckedAt => _checkedAt;

    public KnownIssuesRefreshKind? LastOutcome => _lastOutcome;

    public int ApplicableCount => KnownIssueNotice.CountApplicable(_issues, _running);

    public bool HasNotice => !_suppressed && !_selection.IsEmpty;

    public string NoticeText => !HasNotice
        ? string.Empty
        : _selection.Issues.Count == 1
            ? Loc.Format("MainWindow_KnownIssueNoticeOne", _selection.Issues[0].Title)
            : Loc.Format("MainWindow_KnownIssueNoticeMany", _selection.Issues.Count);

    public string MenuHeader => ApplicableCount == 0
        ? Loc.Get("MainWindow_KnownRobloxIssues")
        : Loc.Format("MainWindow_KnownRobloxIssuesCount", ApplicableCount);

    /// <summary>Any thread. Re-reads the running Roblox version each time: an update may have landed.</summary>
    public void Apply(KnownIssuesSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // The registry, a file version and a folder listing are read here -- not UI-thread work --
        // on the calling thread, before the hop to the UI thread, so the read is never mistaken for
        // needing to run on it.
        var running = _readRunningVersion();
        _ui.Invoke(() =>
        {
            _issues = snapshot.Issues;
            _source = snapshot.Source;
            _checkedAt = snapshot.CheckedAt;
            _lastOutcome = snapshot.LastOutcome;
            _running = running;
            Recompute();
        });
    }

    public void SetSuppressed(bool suppressed) => _ui.Invoke(() =>
    {
        if (_suppressed == suppressed)
        {
            return;
        }

        _suppressed = suppressed;
        Recompute();
    });

    /// <summary>Closes every entry the notice was showing. Only an id not seen before brings it back.</summary>
    public void Dismiss() => _ui.Invoke(() =>
    {
        if (_selection.IsEmpty)
        {
            return;
        }

        _dismissed = _dismissals.Dismiss(_selection.Ids, _issues.Select(i => i.Id));
        Recompute();
    });

    private void Recompute()
    {
        _selection = KnownIssueNotice.Select(_issues, _running, _dismissed);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
