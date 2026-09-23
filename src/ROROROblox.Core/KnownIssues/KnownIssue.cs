namespace ROROROblox.Core.KnownIssues;

/// <summary>Where an issue is documented. <see cref="Url"/> is always absolute https — the parser refuses anything else.</summary>
public sealed record KnownIssueLink(string Label, string Url);

/// <summary>
/// What RoRoRo does about an issue: a feature key (see <see cref="KnownIssueFeatures"/>) and the
/// sentence shown beside it. An unknown key is valid — a newer feed must still load on an older app;
/// the App shows the sentence with no button.
/// </summary>
public sealed record KnownIssueHelp(string Feature, string Text);

/// <summary>Roblox version bounds. At least one is set. <see cref="From"/> is inclusive, <see cref="FixedIn"/> exclusive.</summary>
public sealed record KnownIssueVersions(Version? From, Version? FixedIn);

/// <summary>One known Roblox-side issue, exactly as a valid feed describes it. Text is English.</summary>
public sealed record KnownIssue(
    string Id,
    DateOnly PostedAt,
    bool Notify,
    string Title,
    string Symptom,
    string Workaround,
    KnownIssueHelp? RororoHelps,
    IReadOnlyList<KnownIssueLink> Links,
    KnownIssueVersions? RobloxVersions);

/// <summary>A document that passed <see cref="KnownIssuesParser"/>. An empty <see cref="Issues"/> list is valid: it retires every issue.</summary>
public sealed record KnownIssuesDocument(int SchemaVersion, IReadOnlyList<KnownIssue> Issues);
