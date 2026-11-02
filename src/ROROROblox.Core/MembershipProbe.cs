namespace ROROROblox.Core;

/// <summary>
/// The raw answer from the validate-membership endpoint: the status and the (truncated) body, and
/// nothing interpreted. The endpoint is undocumented and marked for deprecation, so the client
/// returns a measurement and <see cref="AccountFactsService"/> owns the mapping to a fact.
/// </summary>
/// <param name="Status">HTTP status code.</param>
/// <param name="Body">Response body, capped at <see cref="AccountFactsApi.MaxProbeBodyChars"/> characters; null when empty or unreadable.</param>
public sealed record MembershipProbe(int Status, string? Body);
