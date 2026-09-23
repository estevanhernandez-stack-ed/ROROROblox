using System.Globalization;
using ROROROblox.App.Localization;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.App.KnownIssues;

internal sealed record KnownIssueLinkView(string Label, string Url, string AccessibleName);

/// <summary>One entry on the Known Roblox issues page, fully worded. Built fresh on every redraw and culture change.</summary>
internal sealed class KnownIssueView
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string PostedText { get; init; }
    public required string Symptom { get; init; }
    public required string Workaround { get; init; }
    public string? HelpText { get; init; }
    public string? HelpButtonText { get; init; }
    public KnownIssueFeatureRoute HelpRoute { get; init; }
    public string? VersionText { get; init; }
    public required IReadOnlyList<KnownIssueLinkView> Links { get; init; }

    public static KnownIssueView From(KnownIssue issue, Version? running, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(culture);

        var route = KnownIssueFeatureRoutes.For(issue.RororoHelps?.Feature);
        var buttonKey = issue.RororoHelps is null ? null : KnownIssueFeatureRoutes.ButtonLabelKey(route);

        return new KnownIssueView
        {
            Id = issue.Id,
            Title = issue.Title,
            PostedText = Loc.Format("KnownIssuesPage_Posted", issue.PostedAt.ToString("d", culture)),
            Symptom = issue.Symptom,
            Workaround = issue.Workaround,
            HelpText = issue.RororoHelps?.Text,
            HelpButtonText = buttonKey is null ? null : Loc.Get(buttonKey),
            HelpRoute = route,
            VersionText = VersionLine(issue, running),
            Links = issue.Links
                .Select(l => new KnownIssueLinkView(l.Label, l.Url, Loc.Format("KnownIssuesPage_OpenLink", l.Label)))
                .ToList(),
        };
    }

    private static string? VersionLine(KnownIssue issue, Version? running) =>
        KnownIssueApplicability.StatusFor(issue, running) switch
        {
            KnownIssueVersionStatus.NoVersions => null,
            KnownIssueVersionStatus.UnknownInstalled => Loc.Get("KnownIssuesPage_VersionUnknown"),
            KnownIssueVersionStatus.Affected => Loc.Format("KnownIssuesPage_VersionAffected", Short(running!)),
            KnownIssueVersionStatus.Fixed => Loc.Format("KnownIssuesPage_VersionFixed", issue.RobloxVersions!.FixedIn!.ToString(), Short(running!)),
            KnownIssueVersionStatus.NotYetAffected => Loc.Format("KnownIssuesPage_VersionNotYet", issue.RobloxVersions!.From!.ToString(), Short(running!)),
            _ => null,
        };

    /// <summary>"0.740", not "0.740.0.7400927": the build number means nothing to a reader.</summary>
    private static string Short(Version version) => $"{version.Major}.{version.Minor}";
}
