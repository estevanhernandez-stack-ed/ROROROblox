namespace ROROROblox.Core;

/// <summary>
/// What the account row knows about an account that is not in the cookie: a cache of three
/// numbers Roblox answers per account, plus when they were fetched and why the last try failed.
/// Lives inside the DPAPI blob beside the cookie (<see cref="Account.Facts"/>); never travels in
/// account export. Holds no cookie, by construction. Spec 2026-11-01 §2.1.
/// </summary>
/// <param name="Robux">Robux balance, or null when unknown.</param>
/// <param name="Membership">
/// <c>"plus"</c> | <c>"premium"</c> | <c>"none"</c> | null (unknown). Mapped by
/// <see cref="AccountFactsService"/> from the validate-membership probe; the endpoint is gated on
/// a measurement (spec §2.2), so nothing renders this value yet.
/// </param>
/// <param name="FriendsOnline">Count of friends online right now, or null when unknown.</param>
/// <param name="FetchedAtUtc">When the values last refreshed successfully; null when never.</param>
/// <param name="LastError">Short, non-secret reason the last try failed ("401", "403", "timeout"); null when it did not.</param>
public sealed record AccountFacts(
    long? Robux,
    string? Membership,
    int? FriendsOnline,
    DateTimeOffset? FetchedAtUtc,
    string? LastError);
