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
internal sealed class ThemeService
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

    public async Task SetActiveAsync(string themeId)
    {
        var theme = await _store.GetByIdAsync(themeId).ConfigureAwait(true);
        if (theme is null)
        {
            _log.LogWarning("Theme {Id} not found; ignoring.", themeId);
            return;
        }
        try
        {
            await _settings.SetActiveThemeIdAsync(themeId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Saving active theme id failed; applying live anyway.");
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

        PendingEdgeQuestion = QuestionFor(theme, decision);

        CurrentTheme = theme;
        _log.LogInformation("Applied theme {Id} ({Name}).", theme.Id, theme.Name);
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
