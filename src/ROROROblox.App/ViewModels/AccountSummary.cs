using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
    private bool _sessionLimited;
    private bool _isLaunching;
    private string _statusText = string.Empty;
    private FavoriteGame? _selectedGame;
    private bool _isRunning;
    private int? _runningPid;
    private DateTimeOffset? _runningSinceUtc;
    private DateTimeOffset? _lastClosedAtUtc;
    private UserPresenceType _presenceState = UserPresenceType.Offline;
    private string? _currentGameName;
    private long? _currentPlaceId;
    private DateTimeOffset? _inGameSinceUtc;
    private bool _isSelected = true;
    private string? _captionColorHex;
    private int? _fpsCap;
    private bool _isDropTarget;
    private bool _isAddingTag;
    private bool _isFilteredOut;

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
        _localName = account.LocalName;
        RobloxUserId = account.RobloxUserId;
        // Seed tags from the persisted record BEFORE any CollectionChanged subscriber is attached
        // (MainViewModel subscribes after construction), so loading existing tags never triggers a
        // redundant persist back to the store.
        Tags = new ObservableCollection<string>(account.Tags ?? []);
        AddTagCommand = new RelayCommand(p => AddTag(p as string));
        RemoveTagCommand = new RelayCommand(p => RemoveTag(p as string));
    }

    public Guid Id { get; }
    public string DisplayName { get; }
    public string AvatarUrl { get; }
    public DateTimeOffset? LastLaunchedAt { get; private set; }

    private string? _localName;
    /// <summary>
    /// Per-user local nickname override. <see langword="null"/> = no override; UI falls back to
    /// <see cref="DisplayName"/> via <see cref="RenderName"/>. Persisted via
    /// <see cref="IAccountStore.UpdateLocalNameAsync"/>; the ViewModel keeps this in lockstep with
    /// the store so the UI doesn't have to re-list. Roblox-side <see cref="DisplayName"/> never
    /// touches this — see spec §9 decision 3. v1.3.x.
    /// </summary>
    public string? LocalName
    {
        get => _localName;
        set
        {
            if (SetField(ref _localName, value))
            {
                OnPropertyChanged(nameof(RenderName));
            }
        }
    }

    /// <summary>
    /// What the UI should show wherever it used to show <see cref="DisplayName"/>. v1.3.x.
    /// </summary>
    public string RenderName => _localName ?? DisplayName;

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
    /// Roblox userId for this account, read from the persisted <see cref="Account.RobloxUserId"/>
    /// at AccountSummary construction (post-backfill by <see cref="ROROROblox.Core.AccountUserIdBackfillService"/>).
    /// Friends modal can populate it on demand if the backfill hasn't run yet. Plugin gRPC adapters
    /// rely on this being non-null for v1.4+ plugins that target specific alts (rororo-ur-task etc.).
    /// Null = not yet resolved (brand-new account before first backfill, or cookie retrieval failed).
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

    /// <summary>
    /// True when Roblox returned HTTP 403 on this account's authenticated requests — a flagged /
    /// soft-locked session (post bot-challenge), distinct from <see cref="SessionExpired"/> (401 =
    /// dead cookie). Cleared by a successful presence poll (auto-heal) or a re-capture. Spec §5.
    /// </summary>
    public bool SessionLimited
    {
        get => _sessionLimited;
        set
        {
            if (SetField(ref _sessionLimited, value))
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
    /// Server-truth presence for this account, fed by <c>PresenceService</c> (v1.5.0). Authoritative
    /// for <em>display</em> — when this is <see cref="UserPresenceType.InGame"/> the row shows the
    /// game even if the local pid was lost (the ghost fix). Defaults to
    /// <see cref="UserPresenceType.Offline"/> until the first poll lands. Flipping <see cref="InGame"/>
    /// also re-renders the dot and secondary text. Spec §"Components > 2".
    /// </summary>
    public UserPresenceType PresenceState
    {
        get => _presenceState;
        set
        {
            var wasInGame = InGame;
            if (SetField(ref _presenceState, value))
            {
                if (InGame != wasInGame)
                {
                    OnPropertyChanged(nameof(InGame));
                }
                OnPropertyChanged(nameof(StatusDot));
                OnPropertyChanged(nameof(SecondaryStatusText));
            }
        }
    }

    /// <summary>
    /// Resolved game name for the current in-game presence, or null when not in a game / not yet
    /// resolved (presence reports <see cref="UserPresenceType.InGame"/> ~10-30 s before the name
    /// cache fills). Drives the "In {game}" secondary text. v1.5.0.
    /// </summary>
    public string? CurrentGameName
    {
        get => _currentGameName;
        set
        {
            if (SetField(ref _currentGameName, value))
            {
                OnPropertyChanged(nameof(SecondaryStatusText));
            }
        }
    }

    /// <summary>
    /// Place id of the current in-game presence, or null when not in a game. Used by the game-name
    /// cache and (later) per-row "join" affordances. v1.5.0.
    /// </summary>
    public long? CurrentPlaceId
    {
        get => _currentPlaceId;
        set => SetField(ref _currentPlaceId, value);
    }

    /// <summary>
    /// When we first observed <see cref="UserPresenceType.InGame"/> for this account — drives the
    /// "· {age}" duration tail on the in-game label. Set by the presence consumer (item 4) on the
    /// first in-game tick and cleared when the account leaves the game. v1.5.0.
    /// </summary>
    public DateTimeOffset? InGameSinceUtc
    {
        get => _inGameSinceUtc;
        set
        {
            if (SetField(ref _inGameSinceUtc, value))
            {
                OnPropertyChanged(nameof(SecondaryStatusText));
            }
        }
    }

    /// <summary>True when presence reports this account is currently in a game. v1.5.0.</summary>
    public bool InGame => _presenceState == UserPresenceType.InGame;

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
    /// True while this row's "add tag" affordance is engaged — the collapsed "+" pill swaps to an
    /// inline TextBox (auto-focused). Clicking "+" sets this true; Enter commits the tag and sets it
    /// false; Escape / losing focus with no commit sets it false. Pure UI state — NEVER persisted.
    /// Compact-mode rows keep tags read-only and never flip this. v1.6.0 — spec §"4. Tag UI > 4a".
    /// </summary>
    public bool IsAddingTag
    {
        get => _isAddingTag;
        set => SetField(ref _isAddingTag, value);
    }

    /// <summary>
    /// True when an active tag/name filter excludes this account (item 7b). The row container's
    /// Visibility binds to this (collapsed when filtered out) — the underlying
    /// <see cref="MainViewModel.Accounts"/> collection + order are UNCHANGED, which is what keeps
    /// drag-to-reorder index math valid (vs a CollectionViewSource filter). Recomputed by
    /// <see cref="MainViewModel"/> on every filter change via <see cref="MainViewModel.AccountMatchesFilter"/>.
    /// Pure UI state — NEVER persisted. v1.6.0 — spec §"4. Tag UI > 4b".
    /// </summary>
    public bool IsFilteredOut
    {
        get => _isFilteredOut;
        set => SetField(ref _isFilteredOut, value);
    }

    /// <summary>
    /// Free-text labels for this account (PS99, RCU, PLAZA…). Live-bound to the row's chips
    /// <c>ItemsControl</c> so add/remove shows instantly. <see cref="MainViewModel"/> subscribes to
    /// <see cref="ObservableCollection{T}.CollectionChanged"/> and persists via
    /// <see cref="IAccountStore.SetTagsAsync"/> — no Save button, the edit is the save. Seeded in the
    /// constructor (before MainViewModel subscribes) so loading existing tags doesn't re-persist.
    /// Mutate only through <see cref="AddTagCommand"/> / <see cref="RemoveTagCommand"/> so the
    /// normalization rules (trim, dedupe, length + count caps) always apply. v1.5.0 — spec §"Components > 4".
    /// </summary>
    public ObservableCollection<string> Tags { get; }

    /// <summary>Maximum number of tags per account. Keeps the row from overflowing.</summary>
    public const int MaxTags = 8;

    /// <summary>Maximum length of a single tag. Keeps one chip from blowing out the row width.</summary>
    public const int MaxTagLength = 24;

    /// <summary>
    /// Add a free-text tag (parameter = the new tag text). Normalizes: trims whitespace; ignores
    /// empty/whitespace; truncates to <see cref="MaxTagLength"/> chars; dedupes case-insensitively
    /// (a tag already present, ignoring case, is not re-added); ignores adds once
    /// <see cref="MaxTags"/> tags exist. Pure VM state — persistence is wired in MainViewModel.
    /// </summary>
    public ICommand AddTagCommand { get; }

    /// <summary>
    /// Remove the exact tag passed as the parameter (the chip's "×" binds the tag string here).
    /// </summary>
    public ICommand RemoveTagCommand { get; }

    private void AddTag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }
        if (Tags.Count >= MaxTags)
        {
            return;
        }
        var trimmed = raw.Trim();
        if (trimmed.Length > MaxTagLength)
        {
            trimmed = trimmed[..MaxTagLength];
        }
        // Case-insensitive dedupe — don't add a tag that already exists ignoring case.
        foreach (var existing in Tags)
        {
            if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        Tags.Add(trimmed);
    }

    private void RemoveTag(string? tag)
    {
        if (tag is null)
        {
            return;
        }
        Tags.Remove(tag);
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

    /// <summary>
    /// Three-state colored dot: <c>yellow</c> expired / <c>green</c> active (in-game, in Studio, OR
    /// pid alive) / <c>grey</c> idle. The augment rule (v1.5.0): a row is active if <see cref="InGame"/>
    /// OR <see cref="IsRunning"/>, so a lost local pid can no longer force the dot grey once presence
    /// reports in-game. Studio presence also counts as active. Spec §"Components > 2".
    /// </summary>
    public string StatusDot => _sessionExpired
        ? "yellow"
        : _sessionLimited
            ? "magenta"
            : (InGame || _presenceState == UserPresenceType.InStudio || _isRunning) ? "green" : "grey";

    /// <summary>
    /// Human-friendly secondary text shown under the display name. Precedence (v1.5.0 augment rule):
    /// session-expired ▸ "In {game} · {age}" (presence) ▸ "In Studio" ▸ "Connecting…" (pid alive,
    /// first ~60s, no in-game presence yet) ▸ "At Roblox home" (pid alive, not in a game — exited the
    /// game but stayed in the app) ▸ launch error (StatusText) ▸ "Closed {ago}" (both signals agree
    /// it's gone) ▸ "Last launched {ago}" ▸ "Ready". Refresh by calling
    /// <see cref="RefreshRelativeTimes"/> from a periodic tick. Spec §"Components > 2".
    /// </summary>
    public string SecondaryStatusText
    {
        get
        {
            // 1. Session expired wins over everything — the cookie is dead, nothing else matters.
            if (_sessionExpired)
            {
                return "Session expired";
            }
            // 1b. Limited by Roblox (403). Beats stale presence — this is the fix for the frozen
            //     "In game" dot masking a failed launch.
            if (_sessionLimited)
            {
                return "Limited by Roblox — re-capture or wait";
            }
            // 2. In a game (presence authoritative for display — this is the ghost fix).
            if (InGame)
            {
                if (string.IsNullOrEmpty(_currentGameName))
                {
                    return "In a game";
                }
                if (_inGameSinceUtc is DateTimeOffset since)
                {
                    return $"In {_currentGameName} · {RelativeAge(DateTimeOffset.UtcNow - since)}";
                }
                return $"In {_currentGameName}";
            }
            // 3. In Roblox Studio (presence) — a real activity, surfaced even if it wasn't our launch.
            if (_presenceState == UserPresenceType.InStudio)
            {
                return "In Studio";
            }
            // 4. Client alive but not in a game — sitting at the Roblox home screen / menus. Two ways
            //    to land here: presence explicitly reports online-not-in-game (OnlineWebsite), OR the
            //    client has been up past the connecting window without presence ever landing on
            //    in-game (self-presence settles; not-in-game with a live client means it's at home —
            //    e.g. exited the game but stayed in the app). The first ~60s still reads "Connecting…"
            //    so a fresh launch doesn't flash "At Roblox home" before the game loads.
            if (_isRunning)
            {
                if (_presenceState == UserPresenceType.OnlineWebsite)
                {
                    return "At Roblox home";
                }
                if (_runningSinceUtc is DateTimeOffset since &&
                    DateTimeOffset.UtcNow - since < TimeSpan.FromSeconds(60))
                {
                    return "Connecting…";
                }
                return "At Roblox home";
            }
            // 5. Launch error surfaced only when the row isn't active.
            if (!string.IsNullOrEmpty(_statusText))
            {
                return _statusText;
            }
            // 6. Both signals agree it's gone — presence-confirmed close.
            if (_lastClosedAtUtc is DateTimeOffset closed)
            {
                return $"Closed {RelativeAgo(closed)}";
            }
            // 7. Never been active this session, but launched before.
            if (LastLaunchedAt is DateTimeOffset last)
            {
                return $"Last launched {RelativeAgo(last)}";
            }
            // 8. Cold.
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
