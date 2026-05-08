using System.ComponentModel;
using System.Runtime.CompilerServices;
using ROROROblox.Core;

namespace ROROROblox.App.ViewModels;

/// <summary>
/// Per-account view model. Wraps a Core <see cref="Account"/> with mutable UI state
/// (session-expired badge, in-flight launch flag, live process attachment) so the row can
/// flip without a re-fetch.
/// </summary>
public sealed class AccountSummary : INotifyPropertyChanged
{
    private bool _sessionExpired;
    private bool _isLaunching;
    private string _statusText = string.Empty;
    private FavoriteGame? _selectedGame;
    private bool _isRunning;
    private int? _runningPid;
    private DateTimeOffset? _runningSinceUtc;
    private DateTimeOffset? _lastClosedAtUtc;
    private bool _isSelected = true;
    private string? _captionColorHex;
    private int? _fpsCap;
    private bool _isDropTarget;

    public AccountSummary(Account account)
    {
        Id = account.Id;
        DisplayName = account.DisplayName;
        AvatarUrl = account.AvatarUrl;
        LastLaunchedAt = account.LastLaunchedAt;
        _isMain = account.IsMain;
        _isSelected = account.IsSelected;
        _captionColorHex = account.CaptionColorHex;
        _fpsCap = account.FpsCap;
    }

    public Guid Id { get; }
    public string DisplayName { get; }
    public string AvatarUrl { get; }
    public DateTimeOffset? LastLaunchedAt { get; private set; }

    private bool _isMain;
    /// <summary>
    /// True if this account is the user's designated "main." Drives compact-mode CTAs ("Start
    /// [MainName]"), tray double-click behavior, and the MAIN pill on the row. Persisted via
    /// <see cref="IAccountStore.SetMainAsync"/>; the ViewModel updates this in lockstep with
    /// the store so the UI doesn't have to re-list.
    /// </summary>
    public bool IsMain
    {
        get => _isMain;
        set => SetField(ref _isMain, value);
    }

    /// <summary>
    /// Cached Roblox userId for this account — populated lazily on first need (Friends modal).
    /// Not persisted; gets re-fetched after app restart. Null = not yet resolved.
    /// </summary>
    public long? RobloxUserId { get; set; }

    public bool SessionExpired
    {
        get => _sessionExpired;
        set
        {
            if (SetField(ref _sessionExpired, value))
            {
                OnPropertyChanged(nameof(StatusDot));
                OnPropertyChanged(nameof(SecondaryStatusText));
            }
        }
    }

    public bool IsLaunching
    {
        get => _isLaunching;
        set => SetField(ref _isLaunching, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public FavoriteGame? SelectedGame
    {
        get => _selectedGame;
        set => SetField(ref _selectedGame, value);
    }

    /// <summary>True when a tracked <c>RobloxPlayerBeta.exe</c> is currently alive for this account.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetField(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StatusDot));
                OnPropertyChanged(nameof(SecondaryStatusText));
            }
        }
    }

    public int? RunningPid
    {
        get => _runningPid;
        set => SetField(ref _runningPid, value);
    }

    public DateTimeOffset? RunningSinceUtc
    {
        get => _runningSinceUtc;
        set
        {
            if (SetField(ref _runningSinceUtc, value))
            {
                OnPropertyChanged(nameof(SecondaryStatusText));
            }
        }
    }

    public DateTimeOffset? LastClosedAtUtc
    {
        get => _lastClosedAtUtc;
        set
        {
            if (SetField(ref _lastClosedAtUtc, value))
            {
                OnPropertyChanged(nameof(SecondaryStatusText));
            }
        }
    }

    /// <summary>
    /// Whether this account is included in batch launches (Launch multiple / Private server).
    /// Defaults to true; the user toggles via the small dot next to the status text.
    /// Persisted to <c>accounts.dat</c> so unticked alts stay unticked across restarts.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>
    /// True while this row is the active drop target during a drag-to-reorder gesture.
    /// MainWindow's DragEnter / DragLeave / Drop handlers flip it; the row template binds a
    /// cyan insertion-line above the row to this. Pure UI state — never persisted.
    /// </summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetField(ref _isDropTarget, value);
    }

    /// <summary>
    /// User-picked hex color (<c>#rrggbb</c>) for this account's Roblox window title bar.
    /// Null = auto-derive from <see cref="Id"/> hash (the default 8-palette behavior).
    /// MainViewModel persists changes to <see cref="IAccountStore.SetCaptionColorAsync"/>;
    /// the running window picks it up within ~1.5 s via the decorator's re-apply timer.
    /// </summary>
    public string? CaptionColorHex
    {
        get => _captionColorHex;
        set => SetField(ref _captionColorHex, value);
    }

    /// <summary>
    /// Per-account FPS cap, or null for "don't write" (= leave Roblox's default).
    /// Bound to the ComboBox on each row. Set values fall in the
    /// <see cref="ROROROblox.Core.FpsPresets"/> range (10..9999); null clears the FFlag.
    /// MainViewModel persists changes via <see cref="ROROROblox.Core.IAccountStore.SetFpsCapAsync"/>.
    /// </summary>
    public int? FpsCap
    {
        get => _fpsCap;
        set => SetField(ref _fpsCap, value);
    }

    /// <summary>Three-state colored dot: <c>green</c> running / <c>yellow</c> expired / <c>grey</c> idle.</summary>
    public string StatusDot => _sessionExpired
        ? "yellow"
        : _isRunning ? "green" : "grey";

    /// <summary>
    /// Human-friendly secondary text shown under the display name. Order of precedence:
    /// session-expired ▸ explicit StatusText ▸ "Running for X" ▸ "Closed X ago" ▸ "Last launched X ago" ▸ "Ready".
    /// Refresh by calling <see cref="RefreshRelativeTimes"/> from a periodic tick.
    /// </summary>
    public string SecondaryStatusText
    {
        get
        {
            if (_sessionExpired)
            {
                return "Session expired";
            }
            if (!string.IsNullOrEmpty(_statusText) && !_isRunning)
            {
                return _statusText;
            }
            if (_isRunning && _runningSinceUtc is DateTimeOffset since)
            {
                return $"Running — {RelativeAge(DateTimeOffset.UtcNow - since)}";
            }
            if (_lastClosedAtUtc is DateTimeOffset closed)
            {
                return $"Closed {RelativeAgo(closed)}";
            }
            if (LastLaunchedAt is DateTimeOffset last)
            {
                return $"Last launched {RelativeAgo(last)}";
            }
            return "Ready";
        }
    }

    /// <summary>
    /// Force the secondary text to recompute (relative timestamps drift). Called on a tick from
    /// MainViewModel's clock.
    /// </summary>
    public void RefreshRelativeTimes() => OnPropertyChanged(nameof(SecondaryStatusText));

    public void StampLaunched(DateTimeOffset at)
    {
        LastLaunchedAt = at;
        OnPropertyChanged(nameof(LastLaunchedAt));
        OnPropertyChanged(nameof(SecondaryStatusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private static string RelativeAge(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{(int)span.TotalDays}d";
    }

    private static string RelativeAgo(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span < TimeSpan.Zero) return "in the future"; // clock skew safety
        if (span < TimeSpan.FromSeconds(30)) return "just now";
        if (span < TimeSpan.FromMinutes(1)) return "<1 min ago";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} hr ago";
        if (span < TimeSpan.FromDays(7)) return $"{(int)span.TotalDays} days ago";
        return when.ToLocalTime().ToString("MMM d");
    }
}
