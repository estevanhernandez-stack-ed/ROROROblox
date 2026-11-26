namespace ROROROblox.Core.Feeds;

/// <summary>What a feed read came to. Refused is HTTP 403 or 429.</summary>
public enum FeedOutcome
{
    Ok,
    Refused,
    HttpError,
    Timeout,
    Network,
    Shape,
}
