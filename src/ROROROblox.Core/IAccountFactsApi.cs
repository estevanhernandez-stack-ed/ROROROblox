namespace ROROROblox.Core;

/// <summary>
/// The three per-account fact lookups. Its own interface rather than members on
/// <see cref="IRobloxApi"/> so the cost of a new fake lands in one test file instead of nine
/// (spec 2026-11-01 §2.2). Every method takes the plaintext cookie for ONE account and attaches it
/// per request; none of them logs, throws with, or returns it.
/// <para>
/// A non-success HTTP status from <see cref="GetRobuxAsync"/> or <see cref="GetFriendsOnlineCountAsync"/>
/// throws <see cref="AccountFactsRequestException"/> carrying the status. A 200 whose body cannot
/// be read returns null. <see cref="GetMembershipAsync"/> never throws on status: it returns the
/// measurement.
/// </para>
/// </summary>
public interface IAccountFactsApi
{
    /// <summary>GET economy.roblox.com/v1/user/currency, the authenticated user's balance.</summary>
    Task<long?> GetRobuxAsync(string cookie, CancellationToken ct);

    /// <summary>GET friends.roblox.com/v1/users/{userId}/friends/online, the count is <c>data.Length</c>.</summary>
    Task<int?> GetFriendsOnlineCountAsync(string cookie, long userId, CancellationToken ct);

    /// <summary>GET premiumfeatures.roblox.com/v1/users/{userId}/validate-membership, as a raw probe.</summary>
    Task<MembershipProbe> GetMembershipAsync(string cookie, long userId, CancellationToken ct);
}

/// <summary>
/// A fact request got an HTTP answer that was not a success. Carries the status code and nothing
/// else: no URL, no headers, no cookie.
/// </summary>
public sealed class AccountFactsRequestException : Exception
{
    public AccountFactsRequestException(int statusCode)
        : base($"Roblox answered HTTP {statusCode} to an account-facts request.")
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
