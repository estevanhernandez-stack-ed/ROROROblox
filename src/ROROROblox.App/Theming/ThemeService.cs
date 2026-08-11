using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;
using ROROROblox.Core.Theming;

namespace ROROROblox.App.Theming;

/// <summary>
/// App-side theme orchestrator. Reads the saved theme id at startup, looks it up via
/// <see cref="IThemeStore"/>, and mutates the application-level brush <c>Color</c> properties
/// so every <c>{StaticResource}</c> reference re-renders with the new colors. SolidColorBrush
/// is unfrozen by default — assigning to <c>Color</c> triggers WPF's render invalidation.
/// </summary>
internal sealed class ThemeService : IThemeAppliedSource
{
    private readonly IThemeStore _store;
    private readonly IAppSettings _settings;
    private readonly ILogger<ThemeService> _log;

    public ThemeService(IThemeStore store, IAppSettings settings, ILogger<ThemeService>? log = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? NullLogger<ThemeService>.Instance;
    }

    public Theme? CurrentTheme { get; private set; }

    /// <summary>
    /// The active theme as <b>applied</b> — eleven slots including the derived interactive edge.
    /// Null only before the first apply, which on the real startup path happens at
    /// <c>App.OnStartup</c> long before anything can observe this.
    /// </summary>
    public ResolvedPalette? CurrentPalette { get; private set; }

    /// <summary>
    /// Raised after a theme has been applied and both <see cref="CurrentTheme"/> and
    /// <see cref="CurrentPalette"/> are set, so a handler can never catch this service
    /// mid-adoption.
    /// <para>
    /// A plain <c>Action</c> of a Core type, deliberately: this class knows nothing about plugins.
    /// The bridge that forwards this to the plugin event bus lives in <c>Plugins/Adapters</c> with
    /// every other bridge, so the dependency runs Plugins → Theming like all its siblings rather
    /// than inverting for this one case.
    /// </para>
    /// </summary>
    public event Action<ResolvedPalette>? ThemeApplied;

    /// <summary>
    /// Set when the active theme is one somebody wrote themselves, its interactive edge had to
    /// change to clear 3:1, and its author has not been asked about that yet. The derived edge is
    /// already on screen — the question is whether to keep it. <c>null</c> whenever there is
    /// nothing to ask. Cleared by <see cref="AnswerEdgeQuestionAsync"/>.
    /// </summary>
    public EdgeQuestion? PendingEdgeQuestion { get; private set; }

    /// <summary>
    /// Synchronous startup apply. Called from <c>App.OnStartup</c> before any window resolves
    /// resources — must NOT use <c>await</c> on a context-capturing call, otherwise the UI
    /// thread we're already on can deadlock. Walks file IO inline; the saved-id lookup +
    /// theme list are both small JSON reads.
    /// </summary>
    public void ApplyAtStartup()
    {
        Theme? theme = null;
        try
        {
            // GetAwaiter().GetResult() is safe HERE because the underlying AppSettings call
            // ends in ConfigureAwait(false) → no UI-thread continuation needed for the gate.
            // (We keep this self-contained rather than threading a sync API through Core.)
            var savedId = _settings.GetActiveThemeIdAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(savedId))
            {
                theme = _store.GetByIdAsync(savedId).ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Reading active theme id at startup failed; falling back to brand.");
        }

        if (theme is null)
        {
            try
            {
                theme = _store.GetByIdAsync("brand").ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Loading built-in brand theme failed; brushes stay at XAML defaults.");
                return;
            }
        }
        if (theme is not null)
        {
            ApplyToResources(theme, ReadEdgeAnswer(theme));
        }
    }

    /// <summary>
    /// Applies a theme and reports what happened to the half that outlives the session.
    /// <para>
    /// IT USED TO RETURN <c>Task</c> AND SAY NOTHING. The write below has always been wrapped in a
    /// catch that logs and carries on — deliberately, because a failed write is no reason to leave
    /// somebody on a theme they just replaced. What it also did was end the story: the theme went on
    /// screen, the user saw exactly what a successful save looks like, and the old theme was back
    /// after a restart with nothing having ever said so (<c>prd.md &gt; Story 3.1</c>). The
    /// live-apply behaviour is unchanged here. Only the silence is.
    /// </para>
    /// <para>
    /// The message is not written here. This returns the outcome and
    /// <see cref="Preferences.ThemeStatusSummary"/> turns it into words, so the copy is testable
    /// without an <c>Application</c>, a <c>Dispatcher</c> or a <c>Window</c> — none of which the
    /// suite constructs.
    /// </para>
    /// </summary>
    public async Task<ThemeChange> SetActiveAsync(string themeId)
    {
        var theme = await _store.GetByIdAsync(themeId).ConfigureAwait(true);
        if (theme is null)
        {
            _log.LogWarning("Theme {Id} not found; ignoring.", themeId);
            return ThemeChange.Missing;
        }

        string? persistError = null;
        try
        {
            await _settings.SetActiveThemeIdAsync(themeId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Saving active theme id failed; applying live anyway.");
            persistError = ex.Message;
        }

        bool? answer = null;
        if (!theme.IsBuiltIn)
        {
            try
            {
                answer = await _settings.GetEdgeRemediationAnswerAsync(theme.Id).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Reading the edge answer for {Id} failed; treating it as unasked.", theme.Id);
            }
        }
        ApplyToResources(theme, answer);

        // AFTER the apply, on purpose. "It is on now but was not remembered" is only true once it
        // is actually on, and returning before the apply would let a caller paint that sentence
        // over a theme change that had not happened yet.
        return persistError is null ? ThemeChange.Saved : ThemeChange.AppliedButNotSaved(persistError);
    }

    /// <summary>
    /// Records the author's answer and re-applies immediately, so declining visibly puts their own
    /// edge back rather than taking effect on some later launch. A failed write is not fatal — the
    /// answer holds for this session and the question comes back next time, which is the honest
    /// failure: better to ask twice than to silently keep a change somebody refused.
    /// </summary>
    /// <param name="question">
    /// The question that was actually put, NOT whatever is pending now. <c>ShowDialog</c> runs a
    /// nested message pump, so a theme change can land while the dialog is open — the picker's
    /// handler is <c>async void</c> and two arrow-key presses genuinely overlap. Re-reading
    /// <see cref="PendingEdgeQuestion"/> here recorded the answer against the theme that arrived
    /// second, marking a theme declined whose author was never asked and permanently silencing its
    /// prompt. Found by the wave-5 review gate 2026-08-05.
    /// </param>
    public async Task AnswerEdgeQuestionAsync(EdgeQuestion question, bool accepted)
    {
        ArgumentNullException.ThrowIfNull(question);

        try
        {
            await _settings.SetEdgeRemediationAnswerAsync(question.ThemeId, accepted).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Saving the edge answer for {Id} failed; it will be asked again.", question.ThemeId);
        }

        // Only re-apply if the theme being answered about is still the one on screen. If it moved on
        // while the dialog was up, the answer is recorded and that is all — repainting a theme the
        // user has since navigated away from would undo their newer choice.
        var theme = CurrentTheme;
        if (theme is null || !string.Equals(theme.Id, question.ThemeId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PendingEdgeQuestion = null;
        ApplyToResources(theme, accepted);
    }

    /// <summary>
    /// Puts the edge question back on the table for the theme currently on screen, and reports
    /// whether there is one to put. Returns <see langword="false"/> — and changes nothing — for a
    /// built-in, for a theme whose own boundary already clears 3:1, and when no theme has been
    /// applied at all.
    /// <para>
    /// WHY THIS IS NEEDED AT ALL. <see cref="SetActiveAsync"/> reads the stored answer, so a theme
    /// that has been answered about resolves to <c>DeriveSilently</c> or <c>HonourDecline</c> and
    /// produces no question — which is exactly right for every automatic path and exactly wrong for
    /// somebody deliberately asking to see it again. This passes <c>alreadyAnswered: false</c> to
    /// reach <c>AskFirst</c>, which is the one decision that carries a question.
    /// </para>
    /// <para>
    /// <b>IT WRITES NOTHING.</b> There is one consent path and this is not a second one: the answer
    /// still goes through <see cref="AnswerEdgeQuestionAsync"/> to
    /// <c>IAppSettings.SetEdgeRemediationAnswerAsync</c>, the call
    /// <c>EdgeRemediationWiringTests.AnAnswerCanBeChangedLater</c> already pins as "not write-once"
    /// against exactly this affordance arriving. Setting the question without applying anything also
    /// means dismissing the re-asked dialog leaves the theme rendering precisely as it was.
    /// </para>
    /// </summary>
    internal bool ReopenEdgeQuestion()
    {
        if (CurrentTheme is not { } theme) return false;

        var decision = EdgeRemediation.Decide(
            theme.IsBuiltIn, theme.Navy, theme.Divider,
            alreadyAnswered: false, declined: false);

        PendingEdgeQuestion = QuestionFor(theme, decision);
        return PendingEdgeQuestion is not null;
    }

    /// <summary>
    /// Startup-path answer read. Sync-over-async for the same reason the theme-id read above is —
    /// see <see cref="ApplyAtStartup"/>. Built-ins skip the read entirely; they are never asked
    /// about, so the file has nothing to say about them.
    /// </summary>
    private bool? ReadEdgeAnswer(Theme theme)
    {
        if (theme.IsBuiltIn) return null;
        try
        {
            return _settings.GetEdgeRemediationAnswerAsync(theme.Id).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Reading the edge answer for {Id} failed; treating it as unasked.", theme.Id);
            return null;
        }
    }

    private void ApplyToResources(Theme theme, bool? edgeAnswer)
    {
        // Marshal to the UI thread so it's safe to call from any context — settings change
        // handlers, startup boot, file watcher in a future build, etc.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ApplyToResources(theme, edgeAnswer));
            return;
        }

        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var (decision, palette) = ApplyTo(resources, theme, edgeAnswer);

        PendingEdgeQuestion = QuestionFor(theme, decision);

        CurrentTheme = theme;
        CurrentPalette = palette;
        _log.LogInformation("Applied theme {Id} ({Name}).", theme.Id, theme.Name);

        // Last, and after both properties are set: a handler that reads CurrentPalette from inside
        // this callback must see the palette it was just handed, not the previous one.
        ThemeApplied?.Invoke(palette);
    }

    /// <summary>
    /// The question to put to a theme's author, or <c>null</c> when there is nothing to ask.
    /// Separate and static so the mapping is checkable without an <c>Application</c> — everything
    /// around it in this class needs a live Dispatcher and a resource dictionary.
    /// </summary>
    internal static EdgeQuestion? QuestionFor(Theme theme, EdgeRemediation.Decision decision) =>
        decision == EdgeRemediation.Decision.AskFirst
            ? new EdgeQuestion(
                theme.Id,
                theme.Name,
                Surface: theme.Navy,
                AuthoredEdge: theme.Divider,
                DerivedEdge: ContrastGuard.Ensure(theme.Navy, theme.Divider))
            : null;

    /// <summary>
    /// Writes one theme's slots into a resource dictionary, including the derived interactive edge.
    /// <para>
    /// Static and dictionary-taking so a theme can be resolved with no <see cref="Application"/> and
    /// no <see cref="ThemeService"/> instance. The contrast gate (ContrastPairGateTests) measures the
    /// brushes this produces rather than the raw <see cref="Theme"/> record, because
    /// <c>ApplySlot</c> returns early when a hex will not parse and leaves the previous brush in
    /// place - so the record can say one thing while the app shows another.
    /// </para>
    /// Returns the remediation decision so the caller can set <see cref="PendingEdgeQuestion"/>
    /// without recomputing it, and the <see cref="ResolvedPalette"/> that was actually written.
    /// <para>
    /// The palette is <b>read back out of the dictionary</b> after every write rather than
    /// accumulated as we go, and that is not a stylistic choice. <see cref="ApplySlot"/> returns
    /// early on a hex that will not parse and leaves the previous brush in place, so an
    /// accumulate-as-you-write palette would report a colour the app is not showing — the exact
    /// record-versus-screen gap this method's own remarks warn about. Read-back is the only version
    /// that is correct in that case, and being correct in that case is why anything downstream can
    /// trust the palette at all.
    /// </para>
    /// </summary>
    internal static (EdgeRemediation.Decision Decision, ResolvedPalette Palette) ApplyTo(
        ResourceDictionary resources, Theme theme, bool? edgeAnswer)
    {
        ApplySlot(resources, ThemeSlots.Bg, theme.Bg);
        ApplySlot(resources, ThemeSlots.Cyan, theme.Cyan);
        ApplySlot(resources, ThemeSlots.Magenta, theme.Magenta);
        ApplySlot(resources, ThemeSlots.White, theme.White);
        ApplySlot(resources, ThemeSlots.MutedText, theme.MutedText);
        ApplySlot(resources, ThemeSlots.Divider, theme.Divider);
        ApplySlot(resources, ThemeSlots.RowBg, theme.RowBg);
        ApplySlot(resources, ThemeSlots.RowExpiredBg, theme.RowExpiredBg);
        ApplySlot(resources, ThemeSlots.RowExpiredAccent, theme.RowExpiredAccent);
        ApplySlot(resources, ThemeSlots.Navy, theme.Navy);

        // Derived last, because it reads two slots that were just written. An interactive
        // control's edge has to clear WCAG 1.4.11's 3:1 against the surface behind it, and no
        // built-in theme manages it: Navy == Bg in all three, so a secondary button's fill
        // contributes nothing and its border alone measures ~1.2:1. Deriving rather than adding an
        // eleventh slot means every user theme already on disk is covered without its author
        // touching anything (invariant 6 — the contract does not grow).
        //
        // Whether we derive at all is EdgeRemediation's call, not ours: our own themes get fixed
        // silently, somebody else's gets asked about first. See EdgeRemediation for the rules.
        var decision = EdgeRemediation.Decide(
            theme.IsBuiltIn, theme.Navy, theme.Divider,
            alreadyAnswered: edgeAnswer.HasValue,
            declined: edgeAnswer == false);
        ApplySlot(resources, ThemeSlots.InteractiveEdge, EdgeRemediation.Resolve(decision, theme.Navy, theme.Divider));
        return (decision, ReadBack(resources));
    }

    /// <summary>
    /// The eleven slots as they now stand in the dictionary. Reads the brushes rather than the
    /// theme record — see <see cref="ApplyTo"/>'s remarks for why that difference is load-bearing.
    /// A slot that somehow holds something other than a <see cref="SolidColorBrush"/> reports
    /// <c>#000000</c>; that cannot happen through <see cref="ApplySlot"/>, and reporting a wrong
    /// colour beats inventing a plausible one.
    /// </summary>
    private static ResolvedPalette ReadBack(ResourceDictionary resources) => new(
        Bg: HexAt(resources, ThemeSlots.Bg),
        Cyan: HexAt(resources, ThemeSlots.Cyan),
        Magenta: HexAt(resources, ThemeSlots.Magenta),
        White: HexAt(resources, ThemeSlots.White),
        MutedText: HexAt(resources, ThemeSlots.MutedText),
        Divider: HexAt(resources, ThemeSlots.Divider),
        RowBg: HexAt(resources, ThemeSlots.RowBg),
        RowExpiredBg: HexAt(resources, ThemeSlots.RowExpiredBg),
        RowExpiredAccent: HexAt(resources, ThemeSlots.RowExpiredAccent),
        Navy: HexAt(resources, ThemeSlots.Navy),
        InteractiveEdge: HexAt(resources, ThemeSlots.InteractiveEdge));

    private static string HexAt(ResourceDictionary resources, string key)
    {
        var c = (resources[key] as SolidColorBrush)?.Color ?? Colors.Black;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    /// <summary>
    /// Always REPLACE the brush instance. The MainWindow consumes themed brushes via
    /// <c>{DynamicResource}</c> — DynamicResource subscribers re-bind when the dictionary
    /// entry changes, but ignore mutations to the held brush instance. Replacement is the
    /// only path that propagates to already-rendered visuals.
    /// </summary>
    private static void ApplySlot(ResourceDictionary resources, string key, string hex)
    {
        if (!TryParseHex(hex, out var color))
        {
            return;
        }
        resources[key] = new SolidColorBrush(color);
    }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrEmpty(hex)) return false;
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is Color c)
            {
                color = c;
                return true;
            }
        }
        catch
        {
        }
        return false;
    }
}

/// <summary>
/// The outcome of <see cref="ThemeService.SetActiveAsync"/>, split into the two things that can
/// independently fail: whether the theme was found at all, and whether the choice survived being
/// written down.
/// <para>
/// THREE STATES, NOT A BOOL, because they need three different sentences. A theme that was never
/// found is not on screen and must not be described as though it were — that is the state a user
/// reaches by deleting a theme file while Settings is open. A theme that applied but was not saved
/// IS on screen and comes back wrong tomorrow. A theme that saved says nothing at all.
/// </para>
/// <para>
/// <paramref name="PersistError"/> carries the exception's own message rather than a rewritten one.
/// The memory section one page over already shows the raw text for a failed settings write
/// (<c>PreferencesWindow.xaml.cs</c>, "Couldn't save that: {ex.Message}"), and a user who has run
/// out of disk or hit an ACL is better served by what Windows said than by a paraphrase of it.
/// </para>
/// </summary>
internal readonly record struct ThemeChange(bool Found, bool Persisted, string? PersistError)
{
    /// <summary>The id had no theme behind it. Nothing was applied and nothing was written.</summary>
    internal static ThemeChange Missing => new(false, false, null);

    /// <summary>Applied and written down. The silent case.</summary>
    internal static ThemeChange Saved => new(true, true, null);

    /// <summary>On screen for this session only.</summary>
    internal static ThemeChange AppliedButNotSaved(string error) => new(true, false, error);
}
