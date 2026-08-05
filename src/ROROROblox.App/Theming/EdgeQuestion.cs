namespace ROROROblox.App.Theming;

/// <summary>
/// Everything the remediation dialog needs to put one question to one theme's author: we raised
/// the edge on this theme's buttons so they meet the 3:1 contrast floor — keep it, or keep the
/// theme exactly as you wrote it?
/// <para>
/// Carries both colours because the dialog shows them side by side. A dialog that describes a
/// colour change in words is asking somebody to decide about something they cannot see.
/// </para>
/// </summary>
/// <param name="ThemeId">Persistence key — the answer is stored against this, not against the app.</param>
/// <param name="ThemeName">What the author called it, for the dialog's copy.</param>
/// <param name="Surface">The theme's Navy: the fill an interactive control's edge sits against.</param>
/// <param name="AuthoredEdge">The theme's own Divider, as written.</param>
/// <param name="DerivedEdge">What ContrastGuard raised it to — what is on screen while the question is open.</param>
internal sealed record EdgeQuestion(
    string ThemeId,
    string ThemeName,
    string Surface,
    string AuthoredEdge,
    string DerivedEdge);
