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

    /// <summary>
    /// VIP / private server. Squad Launch + per-row VIP picker.
    /// <para>
    /// Roblox's private servers are addressable by two distinct codes — and they are NOT
    /// interchangeable. <see cref="Kind"/> tells the launcher which form to emit on
    /// <c>PlaceLauncher.ashx</c>:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="PrivateServerCodeKind.LinkCode"/> — share bookmark from the in-game
    ///   "Configure server" UI (the <c>?privateServerLinkCode=</c> query param). Roblox resolves
    ///   it server-side at launch time. This is what users paste 95% of the time.</item>
    ///   <item><see cref="PrivateServerCodeKind.AccessCode"/> — the actual server credential
    ///   (<c>?accessCode=</c>). Only appears in already-resolved launcher URIs (e.g., what a
    ///   browser produces after clicking "Open in Roblox" on a share link).</item>
    /// </list>
    /// Sending the wrong code in the wrong slot returns permission-denied even when the user
    /// owns the server.
    /// </summary>
    public sealed record PrivateServer(long PlaceId, string Code, PrivateServerCodeKind Kind) : LaunchTarget;

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

        // Distinguish the two private-server code kinds — they go in DIFFERENT query slots and
        // are not interchangeable. Share URLs (the in-game "Configure server" UI) carry
        // privateServerLinkCode/linkCode; already-resolved launcher URIs carry accessCode.
        var linkMatch = Regex.Match(trimmed, @"(?:privateServerLinkCode|linkCode)=([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
        if (linkMatch.Success && !string.IsNullOrEmpty(linkMatch.Groups[1].Value))
        {
            return new PrivateServer(placeId, linkMatch.Groups[1].Value, PrivateServerCodeKind.LinkCode);
        }

        var accessMatch = Regex.Match(trimmed, @"accessCode=([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
        if (accessMatch.Success && !string.IsNullOrEmpty(accessMatch.Groups[1].Value))
        {
            return new PrivateServer(placeId, accessMatch.Groups[1].Value, PrivateServerCodeKind.AccessCode);
        }

        return new Place(placeId);
    }

    /// <summary>
    /// Detect Roblox's newer share-token URL form (<c>roblox.com/share?code=X&amp;type=Y</c>) —
    /// what the in-game "Configure server / Private Server Link" UI now copies. The token is
    /// opaque locally; only Roblox's <c>sharelinks/v1/resolve-link</c> API can turn it into a
    /// real placeId + linkCode pair. Returns the (code, linkType) tuple if the URL matches;
    /// callers feed both into <see cref="IRobloxApi.ResolveShareLinkAsync"/> and then build a
    /// <see cref="PrivateServer"/> target from the result.
    /// </summary>
    public static bool TryParseShareLink(string? input, out string code, out string linkType)
    {
        code = string.Empty;
        linkType = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (!trimmed.Contains("roblox.com/share", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var codeMatch = Regex.Match(trimmed, @"[?&]code=([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
        if (!codeMatch.Success || string.IsNullOrEmpty(codeMatch.Groups[1].Value))
        {
            return false;
        }

        var typeMatch = Regex.Match(trimmed, @"[?&]type=([A-Za-z]+)", RegexOptions.IgnoreCase);
        code = codeMatch.Groups[1].Value;
        linkType = typeMatch.Success ? typeMatch.Groups[1].Value : "Server";
        return true;
    }
}

/// <summary>
/// Which Roblox query-param slot a private server's opaque code goes in. <c>linkCode</c> for
/// share URLs Roblox resolves server-side at launch; <c>accessCode</c> for already-resolved
/// launcher URIs (what a browser produces after clicking "Open in Roblox").
/// </summary>
public enum PrivateServerCodeKind
{
    LinkCode,
    AccessCode,
}
