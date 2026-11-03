using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ROROROblox.App.Localization;
using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;

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
    private bool _sessionNeedsVerification;
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
    private TimeSpan? _sinceActivity;
    private bool _idleWarn;
    private string? _memoryText;
    private bool _memoryWarning;
    private bool _joinViaFriend;
    private bool _autoRejoin;
    private bool _alertsMuted;
    private AccountFacts? _facts;
    private string? _note;
    private IStreamerIdentityProvider? _identity;

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
        _joinViaFriend = account.JoinViaFriend;
        _autoRejoin = account.AutoRejoin;
        _localName = account.LocalName;
        _facts = account.Facts;
        _note = account.Note;
        PersistedPanelExpanded = account.PanelExpanded;
        RobloxUserId = account.RobloxUserId;
        BrowserTrackerId = account.BrowserTrackerId;
        // Seed tags from the persisted record BEFORE any CollectionChanged subscriber is attached
        // (MainViewModel subscribes after construction), so loading existing tags never triggers a
        // redundant persist back to the store.
        Tags = new ObservableCollection<string>(account.Tags ?? []);
        AddTagCommand = new RelayCommand(p => AddTag(p as string));
        RemoveTagCommand = new RelayCommand(p => RemoveTag(p as string));
    }

    public Guid Id { get; }
    public string DisplayName { get; }

    /// <summary>
    /// The cached Robux / membership / friends-online facts for THIS account, seeded from the store
    /// and replaced only through <c>MainViewModel.ApplyFacts</c>, keyed by id (v1.36). Null until the
    /// first refresh. Nothing renders it yet; the row's badges arrive with the surface items.
    /// </summary>
    public AccountFacts? Facts
    {
        get => _facts;
        set
        {
            if (SetField(ref _facts, value))
            {
                RaiseFactProperties();
            }
        }
    }

    /// <summary>The panel's saved open/closed state, null until the user first toggles it (the panel resolves the default at load).</summary>
    internal bool? PersistedPanelExpanded { get; }

    /// <summary>This account's panel (recent games, favourites, private servers). Set once by <c>MainViewModel.WireAccountSummary</c>.</summary>
    public AccountPanelViewModel? Panel { get; private set; }

    internal void AttachPanel(AccountPanelViewModel panel)
    {
        Panel = panel;
        panel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPanelViewModel.LastPlayed))
            {
                OnPropertyChanged(nameof(LastPlayedText));
                OnPropertyChanged(nameof(LastPlayedUtc));
                RaiseFactProperties();
            }
            else if (e.PropertyName == nameof(AccountPanelViewModel.IsExpanded))
            {
                OnPropertyChanged(nameof(PanelExpanderGlyph));
            }
        };
        OnPropertyChanged(nameof(PanelExpanderGlyph));
    }

    /// <summary>The expander's glyph: ▾ while this row's panel is open, ▸ while closed. A bound property, not a style trigger, because a button's trigger style carries Visibility/IsEnabled setters only.</summary>
    public string PanelExpanderGlyph => Panel?.IsExpanded == true ? "\u25BE" : "\u25B8";

    /// <summary>When this account last launched a game, from the panel's own object so row and panel cannot disagree. Null until the panel has read history.</summary>
    public DateTimeOffset? LastPlayedUtc => Panel?.LastPlayed?.AtUtc;

    /// <summary>"{game} · {age}" for the row's last-played badge; empty when the panel has no history for this account (the view picks the dash).</summary>
    public string LastPlayedText
    {
        get
        {
            if (Panel?.LastPlayed is not { } lp) return string.Empty;
            var ago = RelativeAgo(lp.AtUtc);
            return string.IsNullOrEmpty(lp.Name) ? ago : $"{lp.Name} · {ago}";
        }
    }

    /// <summary>The account's free-text note (trimmed, at most <see cref="Account.MaxNoteLength"/> characters), or null.</summary>
    public string? Note
    {
        get => _note;
        set
        {
            if (SetField(ref _note, value))
            {
                RaiseFactProperties();
            }
        }
    }

    /// <summary>
    /// The note as the panel's <c>NoteBox</c> edits it: empty when there is none. The setter trims, turns
    /// empty into null and caps at <see cref="Account.MaxNoteLength"/> (the same rule the store applies), then
    /// sets <see cref="Note"/>, which <c>MainViewModel</c> persists. The box commits on LostFocus and Enter, so
    /// this is written once per edit, not per keystroke. Raised even when <see cref="Note"/> did not change so
    /// the box re-reads the trimmed text.
    /// </summary>
    public string NoteText
    {
        get => _note ?? string.Empty;
        set
        {
            Note = Account.NormalizeNote(value);
            OnPropertyChanged(nameof(NoteText));
        }
    }

    /// <summary>False under streamer mode: the note is hidden entirely, so the panel's NoteBox collapses with it (spec 2.5).</summary>
    public bool ShowNoteBox => !FactsMasked;

    /// <summary>Accessible name and tooltip of the expander: "Show recent games for {name}", streamer-aware through <see cref="RenderName"/>.</summary>
    public string PanelExpanderName => Loc.Format("Shell_Panel_Expander_Name", RenderName);

    /// <summary>Accessible name of the panel's note box.</summary>
    public string PanelNoteName => Loc.Format("Shell_Panel_Note_Name", RenderName);

    /// <summary>Accessible name of the panel's Open Roblox home button.</summary>
    public string PanelOpenHomeName => Loc.Format("Shell_Panel_OpenHome_Name", RenderName);

    // ---- v1.36 item 6: the facts line (spec 2.1, 2.4, 2.5) -----------------------------------

    /// <summary>The placeholder for a fact nobody has measured yet. Not prose, so not a resource.</summary>
    internal const string FactDash = "\u2014";

    private bool FactsMasked => _identity?.IsActive == true;

    private static System.Globalization.CultureInfo FactCulture => TranslationSource.Instance.CurrentCulture;

    private static bool IsPlus(string? membership) =>
        string.Equals(membership, "plus", StringComparison.OrdinalIgnoreCase)
        || string.Equals(membership, "premium", StringComparison.OrdinalIgnoreCase);

    /// <summary>Robux for the badge: the balance, the dash when unknown, "&#x2022;&#x2022;&#x2022;&#x2022;" under streamer mode.</summary>
    public string RobuxText =>
        FactsMasked ? Loc.Get("Shell_Fact_Masked")
        : _facts?.Robux is { } r ? "R$ " + r.ToString("N0", FactCulture)
        : FactDash;

    /// <summary>Membership for the badge: "Plus" for any membership, the dash when unknown, masked under streamer mode.</summary>
    public string MembershipText =>
        FactsMasked ? Loc.Get("Shell_Fact_Masked")
        : IsPlus(_facts?.Membership) ? "Plus"
        : FactDash;

    /// <summary>Friends online for the badge: "3 friends online", the dash when unknown, masked under streamer mode.</summary>
    public string FriendsOnlineText =>
        FactsMasked ? Loc.Get("Shell_Fact_Masked")
        : _facts?.FriendsOnline is { } f ? Loc.Plural("Shell_Fact_FriendsOnline", f)
        : FactDash;

    /// <summary>The note's text up to the first newline (the TextBlock ellipsises the rest). Empty under streamer mode: hidden entirely.</summary>
    public string NoteFirstLine
    {
        get
        {
            if (FactsMasked || string.IsNullOrWhiteSpace(_note)) return string.Empty;
            var text = _note.Trim();
            var cut = text.IndexOfAny(['\r', '\n']);
            return cut < 0 ? text : text[..cut].TrimEnd();
        }
    }

    public bool ShowRobuxBadge => _facts?.Robux is not null;

    /// <summary>Collapsed whenever membership is not a membership: the probes have not run, so today it is always null.</summary>
    public bool ShowMembershipBadge => IsPlus(_facts?.Membership);

    public bool ShowFriendsOnlineBadge => _facts?.FriendsOnline is not null;

    public bool ShowLastPlayedBadge => !string.IsNullOrEmpty(LastPlayedText);

    public bool ShowNoteBadge => NoteFirstLine.Length > 0;

    /// <summary>
    /// The facts line shows when any of Robux, membership, friends online, last played or the note's
    /// first line has something to say, or a health or Follow-marked badge shows (they lead the line since the
    /// item-13 ruling, 2026-11-03). The age hint alone never makes it visible (the A1 ruling,
    /// 2026-11-02): an account with no facts, no history and no note renders exactly as v1.35.
    /// </summary>
    public bool HasFactsLine =>
        ShowHealthBadge || ShowFollowMarkBadge
        || ShowRobuxBadge || ShowMembershipBadge || ShowFriendsOnlineBadge || ShowLastPlayedBadge || ShowNoteBadge;

    private string MaskedOrUnknownName(string key, bool known, string value) =>
        Loc.Format(key, FactsMasked ? Loc.Get("Shell_Fact_Hidden")
                      : known ? value
                      : Loc.Get("Shell_Fact_Unknown"));

    public string RobuxAccessibleName => MaskedOrUnknownName("Shell_Fact_Robux_Name", ShowRobuxBadge, RobuxText);

    public string MembershipAccessibleName => MaskedOrUnknownName("Shell_Fact_Membership_Name", ShowMembershipBadge, MembershipText);

    public string FriendsOnlineAccessibleName => MaskedOrUnknownName("Shell_Fact_Friends_Name", ShowFriendsOnlineBadge, FriendsOnlineText);

    /// <summary>Last played is local history, never masked.</summary>
    public string LastPlayedAccessibleName =>
        Loc.Format("Shell_Fact_LastPlayed_Name", ShowLastPlayedBadge ? LastPlayedText : Loc.Get("Shell_Fact_Unknown"));

    public string NoteAccessibleName => Loc.Format("Shell_Fact_Note_Name", NoteFirstLine);

    /// <summary>
    /// "Updated 3 min ago", or "Updated 2 hr ago, last try failed (403)" when the last request failed
    /// (the previous values stay standing beside the reason). Empty until a refresh has been
    /// attempted. Not masked: an age identifies nobody.
    /// </summary>
    public string FactsAgeText
    {
        get
        {
            if (_facts is not { } f) return string.Empty;
            if (f.FetchedAtUtc is { } at)
            {
                return string.IsNullOrEmpty(f.LastError)
                    ? Loc.Format("Shell_Fact_Updated", RelativeAgo(at))
                    : Loc.Format("Shell_Fact_UpdatedFailed", RelativeAgo(at), f.LastError);
            }
            return string.IsNullOrEmpty(f.LastError)
                ? string.Empty
                : Loc.Format("Shell_Fact_NeverUpdatedFailed", f.LastError);
        }
    }

    public bool ShowFactsAge => FactsAgeText.Length > 0;

    /// <summary>True when the last refresh failed; the age hint takes the warning brush.</summary>
    public bool FactsFailed => !string.IsNullOrEmpty(_facts?.LastError);

    private void RaiseFactProperties()
    {
        OnPropertyChanged(nameof(RobuxText));
        OnPropertyChanged(nameof(MembershipText));
        OnPropertyChanged(nameof(FriendsOnlineText));
        OnPropertyChanged(nameof(NoteFirstLine));
        // NoteText is deliberately NOT raised here: this runs on every facts refresh, and a re-read would
        // overwrite text the user is typing in the panel's NoteBox (it commits on LostFocus).
        OnPropertyChanged(nameof(ShowNoteBox));
        OnPropertyChanged(nameof(PanelExpanderName));
        OnPropertyChanged(nameof(PanelNoteName));
        OnPropertyChanged(nameof(PanelOpenHomeName));
        OnPropertyChanged(nameof(ShowRobuxBadge));
        OnPropertyChanged(nameof(ShowMembershipBadge));
        OnPropertyChanged(nameof(ShowFriendsOnlineBadge));
        OnPropertyChanged(nameof(ShowLastPlayedBadge));
        OnPropertyChanged(nameof(ShowNoteBadge));
        OnPropertyChanged(nameof(HasFactsLine));
        OnPropertyChanged(nameof(RobuxAccessibleName));
        OnPropertyChanged(nameof(MembershipAccessibleName));
        OnPropertyChanged(nameof(FriendsOnlineAccessibleName));
        OnPropertyChanged(nameof(LastPlayedAccessibleName));
        OnPropertyChanged(nameof(NoteAccessibleName));
        OnPropertyChanged(nameof(FactsAgeText));
        OnPropertyChanged(nameof(ShowFactsAge));
        OnPropertyChanged(nameof(FactsFailed));
    }
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
                OnPropertyChanged(nameof(PanelExpanderName));
                OnPropertyChanged(nameof(PanelNoteName));
                OnPropertyChanged(nameof(PanelOpenHomeName));
            }
        }
    }

    /// <summary>
    /// The real display label — <c>LocalName ?? DisplayName</c> — with NO streamer-mode
    /// substitution. This is what gets PERSISTED (history rows) and what gets handed to
    /// <c>RobloxWindowTitle.ResolveName</c>, which applies the streamer fake at render time.
    ///
    /// <para>F-123: three sites computed this inline and the third (the history writer) dropped
    /// the LocalName half, so History showed the stale Roblox name after a local rename — the
    /// same drift the v1.10 window-title fix closed for the decorator and scanner. Never persist
    /// <see cref="RenderName"/> instead: it is streamer-aware, and a row written during a stream
    /// would bake the fake name into history permanently.</para>
    /// </summary>
    public string RealRenderName => _localName ?? DisplayName;

    /// <summary>
    /// What the UI should show wherever it used to show <see cref="DisplayName"/>. v1.3.x.
    /// Streamer-mode-aware (v1.10 — see <see cref="AttachIdentityProvider"/>): when the attached
    /// <see cref="IStreamerIdentityProvider"/> is active, returns the fake per-account name instead
    /// of the real <see cref="LocalName"/>/<see cref="DisplayName"/>. Falls back to the real value
    /// when no provider is attached or the mode is inactive — <see cref="DisplayName"/> itself is
    /// never mutated, since the provider needs the real value to pass through.
    /// </summary>
    public string RenderName =>
        _identity is { } p ? p.ForAccount(Id, _localName ?? DisplayName, AvatarUrl).Name
                            : (_localName ?? DisplayName);

    /// <summary>
    /// What the UI should show wherever it used to show <see cref="AvatarUrl"/>. Streamer-mode-aware
    /// counterpart to <see cref="RenderName"/>: a fake (<c>pack://</c>) avatar source when the
    /// attached provider is active, else the real <see cref="AvatarUrl"/> verbatim. v1.10.
    /// </summary>
    public string AvatarDisplaySource =>
        _identity is { } p ? p.ForAccount(Id, _localName ?? DisplayName, AvatarUrl).AvatarSource
                            : AvatarUrl;

    /// <summary>
    /// Wire this row to the app-wide streamer-identity singleton so <see cref="RenderName"/> and
    /// <see cref="AvatarDisplaySource"/> flip to fake values while the mode is active. Called by
    /// <see cref="MainViewModel"/> right after construction (see <c>WireAccountSummary</c>).
    /// Idempotent against re-attach (e.g. accidental double-wire) — always unsubscribes any prior
    /// provider first so a row is never subscribed to two providers' <see cref="IStreamerIdentityProvider.Changed"/>
    /// at once.
    /// </summary>
    public void AttachIdentityProvider(IStreamerIdentityProvider provider)
    {
        if (_identity is not null)
        {
            _identity.Changed -= OnIdentityChanged;
        }
        _identity = provider;
        provider.Changed += OnIdentityChanged;
    }

    /// <summary>
    /// Unsubscribe from the streamer-identity provider's <see cref="IStreamerIdentityProvider.Changed"/>
    /// event. <see cref="IStreamerIdentityProvider"/> is a long-lived app singleton — without this,
    /// a discarded row (removed account, or a full reload's <c>Accounts.Clear()</c>) would stay
    /// rooted forever via the provider's event, leaking one <see cref="AccountSummary"/> per
    /// discarded row for the life of the process. <see cref="MainViewModel"/> calls this at every
    /// point a row leaves <c>Accounts</c>. Safe to call even when never attached.
    /// </summary>
    public void DetachIdentityProvider()
    {
        Panel?.Detach();
        if (_identity is not null)
        {
            _identity.Changed -= OnIdentityChanged;
            _identity = null;
        }
    }

    private void OnIdentityChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(RenderName));
        OnPropertyChanged(nameof(AvatarDisplaySource));
        // The five facts and their accessible names flip with the name (spec 2.5).
        RaiseFactProperties();
    }

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

    /// <summary>
    /// Stable per-account browserTrackerId (v1.8.1 trust hygiene — followups 2026-06-30 §6).
    /// Seeded from the persisted <see cref="Account.BrowserTrackerId"/>; null until the
    /// account's first launch generates and persists one (MainViewModel launch path). Not a
    /// UI-bound property — no INPC needed.
    /// </summary>
    public long? BrowserTrackerId { get; set; }

    public bool SessionExpired
    {
        get => _sessionExpired;
        set
        {
            if (SetField(ref _sessionExpired, value))
            {
                OnPropertyChanged(nameof(StatusDot));
                OnPropertyChanged(nameof(SecondaryStatusText));
                RaiseHealthChanged();
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
                RaiseHealthChanged();
            }
        }
    }

    /// <summary>
    /// True when Roblox answered this account's auth-ticket request with a 403 that carried
    /// <c>rblx-challenge-type</c>: a step-up challenge, not the plain limit (<see cref="SessionLimited"/>)
    /// and not a dead cookie (<see cref="SessionExpired"/>). Set to true from exactly one place,
    /// <c>MainViewModel.LaunchAccountAsync</c> on <c>LaunchResult.NeedsVerification</c>; a 403 without
    /// the header never reaches it. Cleared by a launch that starts or a presence poll that lands, like
    /// <see cref="SessionLimited"/>. Spec §1.2.
    /// </summary>
    public bool SessionNeedsVerification
    {
        get => _sessionNeedsVerification;
        set
        {
            if (SetField(ref _sessionNeedsVerification, value))
            {
                OnPropertyChanged(nameof(StatusDot));
                OnPropertyChanged(nameof(SecondaryStatusText));
                RaiseHealthChanged();
            }
        }
    }

    /// <summary>
    /// What the row says about this account's fitness to launch. First match wins: Expired, then
    /// NeedsVerification, then Limited, then FollowMarked, else Ok. Spec §1.1.
    /// </summary>
    internal AccountHealth Health =>
        _sessionExpired ? AccountHealth.Expired
        : _sessionNeedsVerification ? AccountHealth.NeedsVerification
        : _sessionLimited ? AccountHealth.Limited
        : _joinViaFriend ? AccountHealth.FollowMarked
        : AccountHealth.Ok;

    /// <summary>The colour/command key the row template switches on: expired, verify, limited, follow, ok.</summary>
    public string HealthKind => AccountHealthInfo.Kind(Health);

    public string HealthWord => AccountHealthInfo.Word(Health);
    public string HealthGlyph => AccountHealthInfo.Glyph(Health);

    /// <summary>"{glyph} {word}", the health badge's face.</summary>
    public string HealthBadgeText => $"{HealthGlyph} {HealthWord}";

    /// <summary>"{word}, {action}": what a screen reader says for the health badge.</summary>
    public string HealthBadgeName => AccountHealthInfo.BadgeName(Health);

    /// <summary>
    /// The health badge shows for the three states that stop a launch. Follow-marked has its own
    /// permanent badge on the name line (it is a preference, and it coexists with the other three),
    /// so it never shows here.
    /// </summary>
    public bool ShowHealthBadge => Health is AccountHealth.Expired or AccountHealth.NeedsVerification or AccountHealth.Limited;

    /// <summary>The Follow-marked badge on the name line: visible whenever the mark is set.</summary>
    public bool ShowFollowMarkBadge => _joinViaFriend;

    public string FollowMarkBadgeText =>
        $"{AccountHealthInfo.Glyph(AccountHealth.FollowMarked)} {AccountHealthInfo.Word(AccountHealth.FollowMarked)}";

    public string FollowMarkBadgeName => AccountHealthInfo.BadgeName(AccountHealth.FollowMarked);

    // ---- v1.36 item 9: the Library's Play as picker (spec 4.4) ---------------------------------

    /// <summary>
    /// False for an expired session: the picker disables the item rather than let a launch fail on a
    /// dead cookie. Limited and Needs-verification accounts stay pickable (they can recover on their own),
    /// and a follow-marked account is pickable too: the launch path, not the picker, decides it follows.
    /// </summary>
    public bool CanPlayAs => !_sessionExpired;

    /// <summary>
    /// The item's second line (the D2 pick): the health glyph and word for anything that is not Ok
    /// ("Session expired", "Limited by Roblox", "Joins by following"), else the row's own status text
    /// ("Ready", "In {game}"). The word is the short health one, not the row's long sentence, because
    /// the picker is 260 px wide.
    /// </summary>
    public string PlayAsStatusText
        => Health == AccountHealth.Ok ? SecondaryStatusText : HealthBadgeText;

    /// <summary>Tooltip of a disabled picker item: why it cannot be picked. Null when it can be, so no empty tooltip box appears.</summary>
    public string? PlayAsDisabledReason => CanPlayAs ? null : Loc.Get("Library_PlayAs_ExpiredReason");

    /// <summary>Accessible name of the picker item: "{name}, {status}", streamer-aware through <see cref="RenderName"/>.</summary>
    public string PlayAsItemName => Loc.Format("Library_PlayAs_Item_Name", RenderName,
        Health == AccountHealth.Ok ? SecondaryStatusText : HealthWord);

    private void RaiseHealthChanged()
    {
        OnPropertyChanged(nameof(Health));
        OnPropertyChanged(nameof(HealthKind));
        OnPropertyChanged(nameof(HealthWord));
        OnPropertyChanged(nameof(HealthGlyph));
        OnPropertyChanged(nameof(HealthBadgeText));
        OnPropertyChanged(nameof(HealthBadgeName));
        OnPropertyChanged(nameof(ShowHealthBadge));
        OnPropertyChanged(nameof(HasFactsLine));
    }

    public bool IsLaunching
    {
        get => _isLaunching;
        set => SetField(ref _isLaunching, value);
    }

    private int? _stoppingSecondsRemaining;

    /// <summary>
    /// Seconds left before Stop gives up waiting and force-closes, or null when no stop is in
    /// flight (F-111).
    /// <para>
    /// This exists because the wait is not ours and cannot be seen from here. Stop sends the client
    /// the same close its X sends, and an in-game Roblox answers by raising its OWN confirm inside
    /// the game surface — not an OS window, so nothing on this side can detect it, let alone show
    /// it. Without a countdown the row would simply sit there looking broken, which is exactly what
    /// the old Stop did.
    /// </para>
    /// </summary>
    public int? StoppingSecondsRemaining
    {
        get => _stoppingSecondsRemaining;
        set
        {
            if (SetField(ref _stoppingSecondsRemaining, value))
            {
                OnPropertyChanged(nameof(SecondaryStatusText));
            }
        }
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
                OnPropertyChanged(nameof(ShowIdleChip));
            }
        }
    }

    /// <summary>
    /// Time elapsed since the last observed activity for this account (input, focus, or other
    /// liveness signal tracked by the activity monitor), or <see langword="null"/> when unknown /
    /// not yet sampled. Fed by <c>ActivitySnapshotApplier</c> (Task 8) from the ActivityMonitor
    /// snapshot on each tick. Drives <see cref="IdleText"/> and <see cref="ShowIdleChip"/>.
    /// </summary>
    public TimeSpan? SinceActivity
    {
        get => _sinceActivity;
        set
        {
            if (SetField(ref _sinceActivity, value))
            {
                OnPropertyChanged(nameof(IdleText));
                OnPropertyChanged(nameof(ShowIdleChip));
            }
        }
    }

    /// <summary>
    /// True when this account's idle duration has crossed the warn threshold — drives the idle
    /// chip's foreground through a <c>DataTrigger</c> on its Style in <c>MainWindow.xaml</c>,
    /// which sets <c>RowExpiredAccentBrush</c> when true and leaves <c>MutedTextBrush</c> when
    /// false. Both resolve from the active theme, so the chip repaints with it. Set by
    /// <c>ActivitySnapshotApplier</c> (Task 8); pure display state, never persisted.
    /// <para>
    /// The setter raises <see cref="IdleText"/> as well as itself, because the warn state is
    /// carried in the TEXT too — the "▲ " prefix (spec §6.3), so the chip reads as a warning with
    /// colour taken away. <see cref="IdleText"/> was previously recomputed only on
    /// <see cref="SinceActivity"/> change, which on a row whose duration is not moving would land
    /// the glyph a whole tick late while looking perfectly fine on any row that is.
    /// </para>
    /// </summary>
    public bool IdleWarn
    {
        get => _idleWarn;
        set
        {
            if (SetField(ref _idleWarn, value))
            {
                OnPropertyChanged(nameof(IdleText));
            }
        }
    }

    /// <summary>
    /// True when the idle chip should render — only while the account is running AND idle for at
    /// least a minute. A fresh launch or an account that's simply closed never shows the chip.
    /// </summary>
    public bool ShowIdleChip =>
        _isRunning && _sinceActivity is TimeSpan t && t >= TimeSpan.FromMinutes(1);

    /// <summary>
    /// Human-friendly idle duration for the row chip: seconds under a minute, minutes under an
    /// hour, "{h}h{m}m" beyond. Empty when <see cref="SinceActivity"/> is unknown.
    /// <para>
    /// Prefixed "▲ " while <see cref="IdleWarn"/> is set, so the warn state survives with colour
    /// removed (spec §6.3). Not a new device: <see cref="MemoryChipFormatter.Format"/> already
    /// prefixes the same glyph on the memory chip beside this one, so both warning states read as
    /// one vocabulary. U+25B2 is a Segoe UI geometric glyph, Emoji_Presentation=No — not emoji, so
    /// it does not trip the register's invariant-5 rule. Ships in every theme; there is no
    /// theme test here and there must not be one (numbered non-goal 6).
    /// </para>
    /// </summary>
    public string IdleText
    {
        get
        {
            if (_sinceActivity is not TimeSpan t) return string.Empty;

            var warn = _idleWarn ? "▲ " : string.Empty;
            if (t < TimeSpan.FromMinutes(1)) return warn + Loc.Format("Shell_Idle_Seconds", (int)t.TotalSeconds);
            if (t < TimeSpan.FromHours(1)) return warn + Loc.Format("Shell_Idle_Minutes", (int)t.TotalMinutes);
            return warn + Loc.Format("Shell_Idle_HoursMinutes", (int)t.TotalHours, t.Minutes);
        }
    }

    /// <summary>
    /// Formatted per-account memory chip text ("2.3 GB", "▲ 2.3 GB · ~45 min"), or
    /// <see langword="null"/> to render nothing — either the watchdog has no reading yet this
    /// session, or the last sample's pid read failed (never show a stale/zero figure). Produced by
    /// <see cref="MemoryChipFormatter.Format"/> from <c>MainViewModel.ApplyMemory</c> (Task 7);
    /// pure display state, never persisted.
    /// </summary>
    public string? MemoryText
    {
        get => _memoryText;
        set => SetField(ref _memoryText, value);
    }

    /// <summary>
    /// True when this account is currently over its private-bytes cap OR the machine-wide
    /// projection is under its warn threshold — CONDITION-derived from the latest
    /// <see cref="ROROROblox.Core.Diagnostics.MemoryPressureSnapshot"/> every time
    /// <c>MainViewModel.ApplyMemory</c> runs (both on a fresh
    /// <see cref="ROROROblox.Core.Diagnostics.IMemoryWatchdog.PressureCrossed"/> AND on the
    /// passive 30s ticker refresh), NOT latched to "did a crossing just fire" — a stale
    /// edge-derived version of this flag was CRITICAL 1 in the final-branch review (2026-08-01):
    /// it painted true only from <c>PressureCrossed</c> and got wiped back to false by the very
    /// next passive tick even while the account was still over cap.
    /// <para>
    /// Drives the amber (vs muted) color of the memory chip, matching <see cref="IdleWarn"/>'s
    /// pattern — AND is the Recycle button's SOLE visibility trigger
    /// (<c>MainWindow.xaml</c>'s <c>MemoryWarning == True</c> DataTrigger). That second role is
    /// what made the CRITICAL 1 bug a Critical rather than a cosmetic display glitch: the button
    /// disappeared along with the chip color, taking the one-click remedy with it. Pure display
    /// state, never persisted.
    /// </para>
    /// </summary>
    public bool MemoryWarning
    {
        get => _memoryWarning;
        set => SetField(ref _memoryWarning, value);
    }

    public int? RunningPid
    {
        get => _runningPid;
        set => SetField(ref _runningPid, value);
    }

    private bool _isFocused;

    /// <summary>
    /// True for the ONE row currently highlighted after the user clicks a tray memory-warning
    /// balloon (Task 8's <c>ITrayService.RequestFocusAccount</c>, via
    /// <c>MainViewModel.SetFocusedAccount</c>). Pure display state, never persisted.
    /// </summary>
    public bool IsFocused
    {
        get => _isFocused;
        set => SetField(ref _isFocused, value);
    }

    /// <summary>
    /// The <see cref="LaunchTarget"/> the CURRENTLY-launched (or most recently launched) session
    /// used. Set on every successful <see cref="LaunchResult.Started"/> (Task 8's recycle needs
    /// to relaunch into the SAME target, not a re-resolved one — the row's <c>SelectedGame</c>
    /// picker may have changed since the account was launched). Pure runtime state, never persisted.
    /// </summary>
    public LaunchTarget? LastLaunchTarget { get; set; }

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

    private DateTimeOffset? _presenceUpdatedAtUtc;

    /// <summary>
    /// When the presence reading behind <see cref="PresenceState"/> / <see cref="CurrentServer"/>
    /// was taken. Landing verification (v1.14) needs it to tell a fresh confirmation from the
    /// reading it already had before a relaunch — see <c>ServerLandingGate</c>.
    /// </summary>
    public DateTimeOffset? PresenceUpdatedAtUtc
    {
        get => _presenceUpdatedAtUtc;
        set => SetField(ref _presenceUpdatedAtUtc, value);
    }

    private ServerInstance? _currentServer;

    /// <summary>
    /// WHICH server of <see cref="CurrentPlaceId"/> this account is in right now — the whole
    /// (place, job id) pair straight off one presence reading, or null when offline, when privacy
    /// withholds the job id, or before the first poll. <see cref="CurrentPlaceId"/> identifies the
    /// GAME; this identifies the SERVER, and only the pair is a usable address (v1.14).
    /// <para>
    /// Cleared the moment presence says not-in-game: a job id outlives the session it names, and a
    /// stale one would aim the next Recycle at a server this account is not in.
    /// </para>
    /// </summary>
    public ServerInstance? CurrentServer
    {
        get => _currentServer;
        set => SetField(ref _currentServer, value);
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
    /// Whether this account is included in batch launches (Launch multiple / Squad Launch).
    /// Defaults to true; the user toggles via the small dot next to the status text.
    /// Persisted to <c>accounts.dat</c> so unticked alts stay unticked across restarts.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>
    /// Whether this account joins a batch launch's game via friend-follow (join-via-friend) instead
    /// of a direct launch — trust-aware squad launch (v1.9.0). Defaults to false (direct). Read by
    /// <see cref="SquadLaunchPlan.Build"/> to split a batch into its direct and flagged sub-batches.
    /// Seeded from <see cref="Account.JoinViaFriend"/> at construction; flipped + persisted via
    /// <see cref="MainViewModel.ToggleJoinViaFriendCommand"/> (the account row's context menu).
    /// </summary>
    public bool JoinViaFriend
    {
        get => _joinViaFriend;
        set
        {
            if (SetField(ref _joinViaFriend, value))
            {
                OnPropertyChanged(nameof(SecondaryStatusText));
                OnPropertyChanged(nameof(StatusDot));
                OnPropertyChanged(nameof(ShowFollowMarkBadge));
                OnPropertyChanged(nameof(HasFactsLine));
                RaiseHealthChanged();
            }
        }
    }

    /// <summary>
    /// Whether this account's client gets stopped and relaunched when it drops out of the game it
    /// was in — Part B auto-rejoin. Defaults to false. Never available for the main — the row's
    /// context menu hides the toggle for it, and <see cref="MainViewModel.ToggleAutoRejoinAsync"/>
    /// refuses to flip it even if called directly. Seeded from <see cref="Account.AutoRejoin"/> at
    /// construction; flipped + persisted via <see cref="MainViewModel.ToggleAutoRejoinCommand"/>
    /// (the account row's context menu). Task 5, 2026-09-29.
    /// </summary>
    public bool AutoRejoin
    {
        get => _autoRejoin;
        set => SetField(ref _autoRejoin, value);
    }

    /// <summary>
    /// Whether Discord alerts for this account are suppressed — the per-account half of the
    /// two-control design (routing is per-trigger, muting is per-account). Persisted in the
    /// Discord config rather than <c>accounts.dat</c>: it is Discord state, not account state, and
    /// an exported account should not carry someone else's notification preferences. Flipped via
    /// <see cref="MainViewModel.SetAlertsMutedAsync"/>.
    /// </summary>
    public bool AlertsMuted
    {
        get => _alertsMuted;
        set => SetField(ref _alertsMuted, value);
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
    /// Four-state colored dot: <c>yellow</c> expired / <c>magenta</c> rate-limited
    /// (<see cref="SessionLimited"/> — beats active/idle, loses to expired) / <c>green</c> active
    /// (in-game, in Studio, OR pid alive) / <c>grey</c> idle. The augment rule (v1.5.0): a row is
    /// active if <see cref="InGame"/> OR <see cref="IsRunning"/>, so a lost local pid can no longer
    /// force the dot grey once presence reports in-game. Studio presence also counts as active.
    /// Spec §"Components > 2".
    /// </summary>
    public string StatusDot => _sessionExpired
        ? "yellow"
        : _sessionNeedsVerification
            ? "verify"
            : _sessionLimited
                ? "magenta"
                : (InGame || _presenceState == UserPresenceType.InStudio || _isRunning)
                    ? "green"
                    : _joinViaFriend ? "cyan" : "grey";

    /// <summary>
    /// Human-friendly secondary text shown under the display name. Precedence (v1.5.0 augment rule):
    /// session-expired ▸ "Limited by Roblox — re-capture or wait" (<see cref="SessionLimited"/>) ▸
    /// "In {game} · {age}" (presence) ▸ "In Studio" ▸ "Connecting…" (pid alive, first ~60s, no
    /// in-game presence yet) ▸ "At Roblox home" (pid alive, not in a game — exited the game but
    /// stayed in the app) ▸ launch error (StatusText) ▸ "Closed {ago}" (both signals agree it's gone)
    /// ▸ "Last launched {ago}" ▸ "Ready". Refresh by calling
    /// <see cref="RefreshRelativeTimes"/> from a periodic tick. Spec §"Components > 2".
    /// </summary>
    public string SecondaryStatusText
    {
        get
        {
            // 0. A stop the user just asked for outranks everything below, all of which describe
            //    ambient state. This is feedback for an action taken half a second ago, and its
            //    absence is what made the old Stop button feel dead.
            if (_stoppingSecondsRemaining is int stopping)
            {
                return stopping > 0 ? Loc.Format("Shell_Status_Closing", stopping) : Loc.Get("Shell_Status_ClosingNoCount");
            }
            // 1. Session expired wins over everything — the cookie is dead, nothing else matters.
            if (_sessionExpired)
            {
                return Loc.Get("Shell_Status_SessionExpired");
            }
            // 1a. Needs verification: a 403 that named a challenge. Beats presence like Expired does.
            if (_sessionNeedsVerification)
            {
                return Loc.Get("Shell_Status_NeedsVerification");
            }
            // 1b. Limited by Roblox (403). Beats stale presence — this is the fix for the frozen
            //     "In game" dot masking a failed launch.
            if (_sessionLimited)
            {
                return Loc.Get("Shell_Status_Limited");
            }
            // 2. In a game (presence authoritative for display — this is the ghost fix).
            if (InGame)
            {
                if (string.IsNullOrEmpty(_currentGameName))
                {
                    return Loc.Get("Shell_Status_InGame");
                }
                if (_inGameSinceUtc is DateTimeOffset since)
                {
                    return Loc.Format("Shell_Status_InGameNamedAge", _currentGameName, RelativeAge(DateTimeOffset.UtcNow - since));
                }
                return Loc.Format("Shell_Status_InGameNamed", _currentGameName);
            }
            // 3. In Roblox Studio (presence) — a real activity, surfaced even if it wasn't our launch.
            if (_presenceState == UserPresenceType.InStudio)
            {
                return Loc.Get("Shell_Status_InStudio");
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
                    return Loc.Get("Shell_Status_AtHome");
                }
                if (_runningSinceUtc is DateTimeOffset since &&
                    DateTimeOffset.UtcNow - since < TimeSpan.FromSeconds(60))
                {
                    return Loc.Get("Shell_Status_Connecting");
                }
                return Loc.Get("Shell_Status_AtHome");
            }
            // 5. Launch error surfaced only when the row isn't active.
            if (!string.IsNullOrEmpty(_statusText))
            {
                return _statusText;
            }
            // 6. Both signals agree it's gone — presence-confirmed close.
            if (_lastClosedAtUtc is DateTimeOffset closed)
            {
                return Loc.Format("Shell_Status_Closed", RelativeAgo(closed));
            }
            // 7. Never been active this session, but launched before.
            if (LastLaunchedAt is DateTimeOffset last)
            {
                return Loc.Format("Shell_Status_LastLaunched", RelativeAgo(last));
            }
            // 8. Cold. A follow-marked account is the one health state that does NOT beat presence
            //    (it shows as a permanent badge on the name line); it takes this line only at rest.
            return _joinViaFriend
                ? Loc.Get("Shell_Status_FollowMarkedReady")
                : Loc.Get("Shell_Status_Ready");
        }
    }

    /// <summary>
    /// Force the secondary text to recompute (relative timestamps drift). Called on a tick from
    /// MainViewModel's clock.
    /// </summary>
    public void RefreshRelativeTimes()
    {
        Panel?.RefreshRelativeTimes();
        OnPropertyChanged(nameof(SecondaryStatusText));
        OnPropertyChanged(nameof(LastPlayedText));
        OnPropertyChanged(nameof(LastPlayedAccessibleName));
        OnPropertyChanged(nameof(FactsAgeText));
    }

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

    /// <summary>
    /// Re-raise the composed getters after a UI-culture change so bound text re-pulls in the new
    /// language (localization Phase D step 3). Called by MainViewModel's CultureChanged fan-out.
    /// <c>_statusText</c> (momentary, set at event time) and <c>MemoryText</c> (repainted by
    /// MainViewModel.RefreshMemoryChips) are refreshed by the parent, not here.
    /// </summary>
    public void NotifyCultureChanged()
    {
        OnPropertyChanged(nameof(SecondaryStatusText));
        OnPropertyChanged(nameof(IdleText));
    }

    private static string RelativeAge(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return Loc.Get("Shell_Age_JustNow");
        if (span < TimeSpan.FromHours(1)) return Loc.Format("Shell_Age_Minutes", (int)span.TotalMinutes);
        if (span < TimeSpan.FromDays(1)) return Loc.Format("Shell_Age_HoursMinutes", (int)span.TotalHours, span.Minutes);
        return Loc.Format("Shell_Age_Days", (int)span.TotalDays);
    }

    /// <summary>Test seam: a clock for relative-age wording, per async flow, so a test can advance time without touching other tests.</summary>
    internal static readonly AsyncLocal<Func<DateTimeOffset>?> ClockOverride = new();

    internal static DateTimeOffset UtcNow => ClockOverride.Value?.Invoke() ?? DateTimeOffset.UtcNow;

    internal static string RelativeAgo(DateTimeOffset when)
    {
        var span = UtcNow - when;
        if (span < TimeSpan.Zero) return Loc.Get("Shell_Ago_InFuture"); // clock skew safety
        if (span < TimeSpan.FromSeconds(30)) return Loc.Get("Shell_Ago_JustNow");
        if (span < TimeSpan.FromMinutes(1)) return Loc.Get("Shell_Ago_LessThanMinute");
        if (span < TimeSpan.FromHours(1)) return Loc.Format("Shell_Ago_Minutes", (int)span.TotalMinutes);
        if (span < TimeSpan.FromDays(1)) return Loc.Format("Shell_Ago_Hours", (int)span.TotalHours);
        if (span < TimeSpan.FromDays(7)) return Loc.Plural("Shell_Ago_Days", (int)span.TotalDays);
        return when.ToLocalTime().ToString("MMM d");
    }
}
