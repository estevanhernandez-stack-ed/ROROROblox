namespace ROROROblox.Core;

/// <summary>
/// Thrown when Roblox returns HTTP 403 on a cookie-authenticated request whose cookie is NOT
/// expired (a 401 throws <see cref="CookieExpiredException"/> instead). Signals a flagged /
/// soft-locked session — typically post bot-challenge. The cookie still authenticates; Roblox is
/// forbidding the action. Recovery is re-capture (re-login) or cooldown — NEVER auto-retry.
/// </summary>
public sealed class SessionLimitedException : Exception
{
    public SessionLimitedException() : this(null) { }

    public SessionLimitedException(string? challengeType)
        : base("Roblox returned 403 — session is rate-limited / flagged.")
    {
        ChallengeType = challengeType;
    }

    /// <summary>
    /// The <c>rblx-challenge-type</c> header on the 403 (for example <c>captcha</c>), or null when the
    /// 403 carried none. Present means Roblox wants a step-up; absent means today's plain limit.
    /// </summary>
    public string? ChallengeType { get; }
}
