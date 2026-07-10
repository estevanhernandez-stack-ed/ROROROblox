using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.App.Friends;

/// <summary>
/// Per-account modal that lists the cookie owner's friends with live presence and lets the
/// user click "Follow" on any in-game friend. Roblox's <c>RequestFollowUser</c> launch path
/// handles the permission check server-side — works across public AND private servers when
/// your friend's privacy + the server's allowlist permit.
/// </summary>
internal partial class FriendFollowWindow : Window
{
    private readonly IRobloxApi _api;
    private readonly IAccountStore _accountStore;
    private readonly IReadOnlyList<FriendSource> _sources;
    private readonly Guid _launcherAccountId;
    private readonly IStreamerIdentityProvider? _streamerIdentity;
    private int _currentSourceIndex;

    // Cached last-fetched groupings so a streamer-mode toggle (provider.Changed) can re-render
    // the rows with fresh fake/real identities WITHOUT re-hitting the network. Populated by
    // RefreshAsync; consumed by RenderRows(). _hasData guards OnStreamerIdentityChanged from
    // rebuilding over a "Loading..."/error state that never produced real data.
    private List<(Friend Friend, UserPresence Presence)> _inGame = new();
    private List<(Friend Friend, UserPresence? Presence)> _online = new();
    private List<Friend> _offline = new();
    private bool _hasData;

    /// <summary>The friend the user picked — null if the user closed without following.</summary>
    public LaunchTarget.FollowFriend? SelectedTarget { get; private set; }

    /// <summary>
    /// The picked friend's presence snapshot at click time — carried so the caller
    /// (<c>OpenFriendFollowAsync</c>) re-runs the same <see cref="MainViewModel.EvaluateFollow"/>
    /// land-at-home guard before launching, instead of trusting the modal alone.
    /// </summary>
    public UserPresence? SelectedPresence { get; private set; }

    /// <summary>The picked friend's display name — for the caller's status messaging.</summary>
    public string? SelectedFriendName { get; private set; }

    public FriendFollowWindow(
        IRobloxApi api,
        IAccountStore accountStore,
        IReadOnlyList<FriendSource> sources,
        int defaultSourceIndex,
        Guid launcherAccountId,
        IStreamerIdentityProvider? streamerIdentity = null)
    {
        if (sources is null || sources.Count == 0)
        {
            throw new ArgumentException("At least one friend source is required.", nameof(sources));
        }
        if (defaultSourceIndex < 0 || defaultSourceIndex >= sources.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultSourceIndex));
        }

        _api = api ?? throw new ArgumentNullException(nameof(api));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _sources = sources;
        _currentSourceIndex = defaultSourceIndex;
        _launcherAccountId = launcherAccountId;
        _streamerIdentity = streamerIdentity;

        InitializeComponent();

        if (_sources.Count > 1)
        {
            SourceSwitchButton.Visibility = Visibility.Visible;
        }
        UpdateSourceChrome();
        Loaded += async (_, _) => await RefreshAsync();

        // Streamer mode can be toggled (or rerolled) while this modal is open — re-render the
        // already-fetched rows with the new fake/real identities instead of forcing a Refresh
        // click. Unsubscribe on Closed so a discarded modal never keeps this instance rooted via
        // the provider's Changed event (same leak concern AccountSummary.DetachIdentityProvider
        // guards against for account rows — Task 7).
        if (_streamerIdentity is not null)
        {
            _streamerIdentity.Changed += OnStreamerIdentityChanged;
        }
        Closed += (_, _) =>
        {
            if (_streamerIdentity is not null)
            {
                _streamerIdentity.Changed -= OnStreamerIdentityChanged;
            }
        };
    }

    private void OnStreamerIdentityChanged(object? sender, EventArgs e)
    {
        if (_hasData)
        {
            RenderRows();
        }
    }

    private FriendSource CurrentSource => _sources[_currentSourceIndex];

    /// <summary>Refresh title, source-switch label, and the launcher hint for the current source.</summary>
    private void UpdateSourceChrome()
    {
        var current = CurrentSource;
        Title = $"ROROROblox -- Friends -- {current.DisplayName}";
        AccountTitle.Text = current.DisplayName;

        if (_sources.Count > 1)
        {
            var other = _sources[(_currentSourceIndex + 1) % _sources.Count];
            SourceSwitchButton.Content = $"View {other.DisplayName}'s friends";
        }

        // When you're browsing a list that isn't the launching account's own, name the launcher so
        // it's clear which account the Follow button will actually start. Action-first — the button
        // is the only trigger; nothing auto-joins.
        if (current.AccountId != _launcherAccountId)
        {
            var launcherName = _sources.First(s => s.AccountId == _launcherAccountId).DisplayName;
            LauncherHint.Text = $"Follow one to launch {launcherName} into their server.";
            LauncherHint.Visibility = Visibility.Visible;
        }
        else
        {
            LauncherHint.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnSourceSwitchClick(object sender, RoutedEventArgs e)
    {
        _currentSourceIndex = (_currentSourceIndex + 1) % _sources.Count;
        UpdateSourceChrome();
        await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        StatusText.Text = "Loading friends...";
        FriendsList.Children.Clear();
        RefreshButton.IsEnabled = false;
        SourceSwitchButton.IsEnabled = false;
        // Captured above the try so the catch blocks below name the account this fetch was
        // actually for, even if the user switches sources again while this call is in flight.
        var source = CurrentSource;
        try
        {
            // Fetch the plaintext cookie fresh per refresh into a local that falls out of scope
            // after the API calls below — we never retain it on the window for the modal's lifetime.
            var cookie = await _accountStore.RetrieveCookieAsync(source.AccountId);

            var friends = await _api.GetFriendsAsync(cookie, source.RobloxUserId);
            if (friends.Count == 0)
            {
                _hasData = false;
                StatusText.Text = "No friends visible. Either this account has none, or its privacy filter is hiding them.";
                return;
            }

            var presences = await _api.GetPresenceAsync(cookie, friends.Select(f => f.UserId));
            var presenceByUserId = presences.ToDictionary(p => p.UserId);

            var inGame = new List<(Friend Friend, UserPresence Presence)>();
            var online = new List<(Friend Friend, UserPresence? Presence)>();
            var offline = new List<Friend>();

            foreach (var friend in friends)
            {
                presenceByUserId.TryGetValue(friend.UserId, out var presence);
                switch (presence?.PresenceType)
                {
                    case UserPresenceType.InGame:
                        inGame.Add((friend, presence));
                        break;
                    case UserPresenceType.OnlineWebsite:
                    case UserPresenceType.InStudio:
                        online.Add((friend, presence));
                        break;
                    default:
                        offline.Add(friend);
                        break;
                }
            }

            // Sort each section alphabetically by display name.
            inGame.Sort((a, b) => string.Compare(a.Friend.DisplayName, b.Friend.DisplayName, StringComparison.OrdinalIgnoreCase));
            online.Sort((a, b) => string.Compare(a.Friend.DisplayName, b.Friend.DisplayName, StringComparison.OrdinalIgnoreCase));
            offline.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            _inGame = inGame;
            _online = online;
            _offline = offline;
            _hasData = true;
            RenderRows();

            StatusText.Text = $"{friends.Count} {(friends.Count == 1 ? "friend" : "friends")} · " +
                              $"{inGame.Count} in game · {online.Count} online · {offline.Count} offline";
        }
        catch (CookieExpiredException)
        {
            _hasData = false;
            StatusText.Text = _sources.Count > 1
                ? $"{source.DisplayName}'s session expired — re-authenticate it, or switch to the other account's friends."
                : $"{source.DisplayName}'s session expired — close this and re-authenticate the account first.";
        }
        catch (AccountStoreCorruptException)
        {
            _hasData = false;
            StatusText.Text = "Couldn't read this account's saved session — close this and re-add the account.";
        }
        catch (Exception ex)
        {
            _hasData = false;
            StatusText.Text = $"Couldn't load friends: {ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            SourceSwitchButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// (Re)build the visible rows from the cached <see cref="_inGame"/>/<see cref="_online"/>/
    /// <see cref="_offline"/> groupings — no network call. Called after a successful
    /// <see cref="RefreshAsync"/> fetch AND from <see cref="OnStreamerIdentityChanged"/> so a
    /// streamer-mode flip (or reroll) while this modal is open re-renders with the current
    /// fake/real identities instantly.
    /// </summary>
    private void RenderRows()
    {
        FriendsList.Children.Clear();

        if (_inGame.Count > 0)
        {
            AddSectionHeader("In game", _inGame.Count, isAccent: true);
            foreach (var (f, p) in _inGame)
            {
                // A friend can be InGame yet expose no joinable place (join/visibility privacy
                // off) — the same land-at-home guard FollowAltAsync uses gates the button here so
                // we never offer a Follow that silently bounces to the Roblox home page.
                var followable = MainViewModel.EvaluateFollow(p, FriendName(f)).CanFollow;
                FriendsList.Children.Add(BuildFriendRow(f, p, isFollowable: followable));
            }
        }
        if (_online.Count > 0)
        {
            AddSectionHeader("Online", _online.Count, isAccent: false);
            foreach (var (f, p) in _online)
            {
                FriendsList.Children.Add(BuildFriendRow(f, p, isFollowable: false));
            }
        }
        if (_offline.Count > 0)
        {
            AddSectionHeader("Offline", _offline.Count, isAccent: false);
            foreach (var f in _offline)
            {
                FriendsList.Children.Add(BuildFriendRow(f, null, isFollowable: false));
            }
        }
    }

    private void AddSectionHeader(string title, int count, bool isAccent)
    {
        FriendsList.Children.Add(new TextBlock
        {
            Text = $"{title.ToUpperInvariant()}  ·  {count}",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource(isAccent ? "CyanBrush" : "MutedTextBrush"),
            Margin = new Thickness(0, 12, 0, 6),
        });
    }

    private Border BuildFriendRow(Friend friend, UserPresence? presence, bool isFollowable)
    {
        // Streamer-mode-aware display identity (Task 11 — mirrors AccountSummary.RenderName /
        // AvatarDisplaySource, Task 7). ForFriend internally no-ops to the real values when the
        // provider is null/inactive, so this is a straight swap-in with no behavior change when
        // streamer mode is off. friend.DisplayName/AvatarUrl themselves are never mutated.
        // realName covers the empty-DisplayName fallback (uses friend.Username) — it's only ever
        // passed to ForFriend as the "real" value, which returns the FAKE name while active, so an
        // empty-display-name friend still shows a fake name (never the raw username) on screen.
        var active = _streamerIdentity?.IsActive == true;
        var realName = FriendName(friend);
        var display = _streamerIdentity?.ForFriend(friend.UserId, realName, friend.AvatarUrl)
                      ?? new DisplayIdentity(realName, friend.AvatarUrl);

        var row = new Border
        {
            Background = (Brush)FindResource("RowBgBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Avatar (best-effort — leave blank circle if the source fetch fails). display.AvatarSource
        // is either the real AvatarUrl (http) or a fake pack:// resource URI (streamer mode active) —
        // both are valid absolute Uris for BitmapImage.
        var avatar = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = (Brush)FindResource("NavyBrush"),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
        };
        if (!string.IsNullOrEmpty(display.AvatarSource))
        {
            try
            {
                avatar.Background = new ImageBrush(new BitmapImage(new Uri(display.AvatarSource, UriKind.Absolute)))
                {
                    Stretch = Stretch.UniformToFill,
                };
            }
            catch
            {
                // Bad URL — leave the navy fallback.
            }
        }
        Grid.SetColumn(avatar, 0);
        grid.Children.Add(avatar);

        // Name + secondary text.
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = display.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("WhiteBrush"),
        });
        // Secondary line = real @username (+ current location). A friend's @username is often MORE
        // identifying than their display name and there's no fake handle to substitute for it, so
        // while streamer mode is active the whole line is suppressed — friend.Username never enters
        // the visual tree. Re-evaluates on provider.Changed because the enclosing row is rebuilt by
        // RenderRows on every Changed, so toggling mid-session hides/shows it live.
        string secondary;
        if (active)
        {
            secondary = string.Empty;
        }
        else
        {
            secondary = string.IsNullOrEmpty(friend.Username) ? string.Empty : $"@{friend.Username}";
            if (!string.IsNullOrEmpty(presence?.LastLocation))
            {
                secondary = string.IsNullOrEmpty(secondary)
                    ? presence.LastLocation
                    : $"{secondary}  ·  {presence.LastLocation}";
            }
        }
        info.Children.Add(new TextBlock
        {
            Text = secondary,
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedTextBrush"),
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        if (isFollowable)
        {
            var followBtn = new Button
            {
                Content = "Follow",
                Padding = new Thickness(14, 6, 14, 6),
                Background = (Brush)FindResource("CyanBrush"),
                Foreground = (Brush)FindResource("NavyBrush"),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                ToolTip = "Launch this account into the server your friend is in.",
            };
            followBtn.Click += (_, _) => OnFollowClick(friend, presence, display.Name);
            Grid.SetColumn(followBtn, 2);
            grid.Children.Add(followBtn);
        }
        else if (presence?.PresenceType == UserPresenceType.InGame)
        {
            // InGame but not joinable — the friend's join/visibility privacy hides the server, so a
            // Follow would land at home. Say why instead of offering a button that silently bounces.
            var hint = new TextBlock
            {
                Text = "Join privacy off",
                FontSize = 10,
                Foreground = (Brush)FindResource("MutedTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "This friend is in a game, but their join privacy hides the server so RoRoRo can't follow them in.",
            };
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }

        row.Child = grid;
        return row;
    }

    private static string FriendName(Friend friend) =>
        string.IsNullOrEmpty(friend.DisplayName) ? friend.Username : friend.DisplayName;

    private void OnFollowClick(Friend friend, UserPresence? presence, string displayName)
    {
        SelectedTarget = new LaunchTarget.FollowFriend(friend.UserId);
        SelectedPresence = presence;
        // Caller (MainViewModel.OpenFriendFollowAsync) only uses this for its own StatusBanner
        // messaging (e.g. a blocked-follow reason) — never for the Roblox API call itself, which
        // goes by friend.UserId. Passing the streamer-mode-aware display name keeps that banner
        // from re-leaking the real name the picker just finished hiding.
        SelectedFriendName = displayName;
        DialogResult = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
