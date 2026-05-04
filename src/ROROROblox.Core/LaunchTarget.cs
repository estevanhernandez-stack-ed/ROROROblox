using System.Text.RegularExpressions;

namespace ROROROblox.Core;

/// <summary>
/// What an account is being asked to launch into. Discriminated union — each variant maps to
/// a distinct <c>request=</c> shape on Roblox's <c>PlaceLauncher.ashx</c> endpoint:
/// <list type="bullet">
///   <item><see cref="DefaultGame"/> -> resolved at launch time via favorites + settings.</item>
///   <item><see cref="Place"/> -> <c>request=RequestGame&amp;placeId={X}</c>.</item>
///   <item><see cref="PrivateServer"/> -> <c>request=RequestPrivateGame&amp;placeId={X}&amp;accessCode={Y}</c>.</item>
///   <item><see cref="FollowFriend"/> -> <c>request=RequestFollowUser&amp;userId={X}</c>. Roblox does the
///   permission check server-side; works for public AND private servers if your friend's privacy +
///   server allowlist permit it.</item>
/// </list>
/// </summary>
public abstract record LaunchTarget
{
    private LaunchTarget() { }

    /// <summary>Resolve from default favorite or app settings — original Launch As behavior.</summary>
    public sealed record DefaultGame() : LaunchTarget;

    /// <summary>Public server, specific place. Used by the Games dialog.</summary>
    public sealed record Place(long PlaceId) : LaunchTarget;

    /// <summary>VIP / private server. Squad Launch + per-row VIP picker.</summary>
    public sealed record PrivateServer(long PlaceId, string AccessCode) : LaunchTarget;

    /// <summary>Follow a friend into whatever server they're in.</summary>
    public sealed record FollowFriend(long UserId) : LaunchTarget;

    /// <summary>
    /// Best-effort parse of any user-pasted Roblox URL into a launch target. Recognizes:
    /// <list type="bullet">
    ///   <item>A private server share URL (<c>?privateServerLinkCode={code}</c>) -> <see cref="PrivateServer"/>.</item>
    ///   <item>A public game URL (<c>roblox.com/games/{id}</c>) -> <see cref="Place"/>.</item>
    ///   <item>A bare numeric place id -> <see cref="Place"/>.</item>
    ///   <item>An existing <c>PlaceLauncher.ashx</c> URL with <c>placeId</c> + <c>accessCode</c> -> <see cref="PrivateServer"/>.</item>
    ///   <item>An existing <c>PlaceLauncher.ashx</c> URL with just <c>placeId</c> -> <see cref="Place"/>.</item>
    /// </list>
    /// Returns <c>null</c> for unparseable input — callers should fall back to <see cref="DefaultGame"/>
    /// or surface an error.
    /// </summary>
    public static LaunchTarget? FromUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();

        // Bare numeric place id.
        if (long.TryParse(trimmed, out var bare) && bare > 0)
        {
            return new Place(bare);
        }

        var placeIdMatch = Regex.Match(trimmed, @"(?:placeId=|roblox\.com/games/)(\d+)", RegexOptions.IgnoreCase);
        if (!placeIdMatch.Success || !long.TryParse(placeIdMatch.Groups[1].Value, out var placeId) || placeId <= 0)
        {
            return null;
        }

        // Private servers carry either privateServerLinkCode (share URL) or accessCode (legacy launcher form).
        var codeMatch = Regex.Match(trimmed, @"(?:privateServerLinkCode|linkCode|accessCode)=([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
        if (codeMatch.Success && !string.IsNullOrEmpty(codeMatch.Groups[1].Value))
        {
            return new PrivateServer(placeId, codeMatch.Groups[1].Value);
        }

        return new Place(placeId);
    }
}
