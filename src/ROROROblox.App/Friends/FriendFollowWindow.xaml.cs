using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.Core;

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
    private readonly string _cookie;
    private readonly long _accountUserId;

    /// <summary>The friend the user picked — null if the user closed without following.</summary>
    public LaunchTarget.FollowFriend? SelectedTarget { get; private set; }

    public FriendFollowWindow(IRobloxApi api, string cookie, long accountUserId, string accountDisplayName)
    {
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }
        if (accountUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountUserId));
        }

        _api = api ?? throw new ArgumentNullException(nameof(api));
        _cookie = cookie;
        _accountUserId = accountUserId;
        InitializeComponent();
        Title = $"ROROROblox -- Friends -- {accountDisplayName}";
        AccountTitle.Text = accountDisplayName;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        StatusText.Text = "Loading friends...";
        FriendsList.Children.Clear();
        RefreshButton.IsEnabled = false;
        try
        {
            var friends = await _api.GetFriendsAsync(_cookie, _accountUserId);
            if (friends.Count == 0)
            {
                StatusText.Text = "No friends visible. Either this account has none, or its privacy filter is hiding them.";
                return;
            }

            var presences = await _api.GetPresenceAsync(_cookie, friends.Select(f => f.UserId));
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

            if (inGame.Count > 0)
            {
                AddSectionHeader("In game", inGame.Count, isAccent: true);
                foreach (var (f, p) in inGame)
                {
                    FriendsList.Children.Add(BuildFriendRow(f, p, isFollowable: true));
                }
            }
            if (online.Count > 0)
            {
                AddSectionHeader("Online", online.Count, isAccent: false);
                foreach (var (f, p) in online)
                {
                    FriendsList.Children.Add(BuildFriendRow(f, p, isFollowable: false));
                }
            }
            if (offline.Count > 0)
            {
                AddSectionHeader("Offline", offline.Count, isAccent: false);
                foreach (var f in offline)
                {
                    FriendsList.Children.Add(BuildFriendRow(f, null, isFollowable: false));
                }
            }

            StatusText.Text = $"{friends.Count} {(friends.Count == 1 ? "friend" : "friends")} · " +
                              $"{inGame.Count} in game · {online.Count} online · {offline.Count} offline";
        }
        catch (CookieExpiredException)
        {
            StatusText.Text = "Session expired — close this and re-authenticate the account first.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't load friends: {ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
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

        // Avatar (best-effort — leave blank circle if URL fetch fails).
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
        if (!string.IsNullOrEmpty(friend.AvatarUrl))
        {
            try
            {
                avatar.Background = new ImageBrush(new BitmapImage(new Uri(friend.AvatarUrl, UriKind.Absolute)))
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
            Text = string.IsNullOrEmpty(friend.DisplayName) ? friend.Username : friend.DisplayName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("WhiteBrush"),
        });
        var secondary = string.IsNullOrEmpty(friend.Username) ? string.Empty : $"@{friend.Username}";
        if (!string.IsNullOrEmpty(presence?.LastLocation))
        {
            secondary = string.IsNullOrEmpty(secondary)
                ? presence.LastLocation
                : $"{secondary}  ·  {presence.LastLocation}";
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
            followBtn.Click += (_, _) => OnFollowClick(friend.UserId);
            Grid.SetColumn(followBtn, 2);
            grid.Children.Add(followBtn);
        }

        row.Child = grid;
        return row;
    }

    private void OnFollowClick(long friendUserId)
    {
        SelectedTarget = new LaunchTarget.FollowFriend(friendUserId);
        DialogResult = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
