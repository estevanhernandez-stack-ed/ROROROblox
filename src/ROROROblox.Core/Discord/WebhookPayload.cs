using System.Globalization;
using System.Text;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Core.Discord;

/// <summary>
/// What a webhook is allowed to say. Two strings, and no field that could carry a server link.
/// <para>
/// This is a security boundary expressed as a type. Presence join secrets reach people who can
/// see your Join button; a channel post reaches everyone who ever reads that channel, including
/// people who join it later. "We remember not to send it" is a rule that erodes; a type that
/// cannot represent it does not.
/// </para>
/// </summary>
public sealed record WebhookPayload(string Title, string Body)
{
    /// <summary>
    /// The longest title any destination takes: Pushover's documented title cap, the tightest of the
    /// three (Discord and ntfy count the title inside their one message instead).
    /// </summary>
    internal const int TitleLimit = 250;

    /// <summary>
    /// The longest body. Pushover's message cap is 1024, and its sender cuts anything past
    /// 1024 - 32 = 992 at a line break and appends its own "…and N more", which would count this
    /// payload's trailer as an account. At or under 992 that cut never runs. It is also the tightest
    /// envelope overall: with the title at most <see cref="TitleLimit"/>, a Discord message
    /// ("**{Title}**\n{Body}") is at most 1247 of its 2000 characters, and an ntfy body
    /// ("{Title}\n{Body}", at most three UTF-8 bytes per UTF-16 unit) at most 3727 of its 4096 bytes.
    /// So one capped payload fits every REMOTE destination (2026-09-15). The desktop toast is
    /// tighter still and gets its own envelope: see <see cref="PayloadLimits.Toast"/>.
    /// </summary>
    internal const int BodyLimit = 992;

    /// <summary>The tray balloon's title: 64 UTF-16 units less its terminator. See
    /// <see cref="PayloadLimits.Toast"/>.</summary>
    internal const int ToastTitleLimit = 63;

    /// <summary>The tray balloon's text: 256 UTF-16 units less its terminator.</summary>
    internal const int ToastBodyLimit = 255;

    /// <summary>
    /// <paramref name="useRealNames"/> is set only for the clan destination — see
    /// <see cref="AlertTrigger"/> for why that one room is exempt from streamer mode. Defaults to
    /// false so any future caller that forgets the question gets the masked names.
    /// <paramref name="limits"/> is the destination's envelope (<see cref="PayloadLimits.For"/>);
    /// null means <see cref="PayloadLimits.Remote"/>, today's 250/992.
    /// </summary>
    public static WebhookPayload ForAlert(
        AlertKind kind, IReadOnlyList<AlertTrigger> triggers, bool useRealNames = false,
        PayloadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        if (triggers.Count == 0) throw new ArgumentException("No triggers.", nameof(triggers));
        var envelope = limits ?? PayloadLimits.Remote;

        string Name(AlertTrigger t) => useRealNames ? t.RealName : t.DisplayName;

        var noun = triggers.Count == 1 ? Name(triggers[0]) : $"{triggers.Count} accounts";
        // Every title is a HEAD (the name or count, and for a metric the label) and a TAIL (the
        // wording: its verb and threshold), so CapTitle can shorten the part that grows without
        // cutting the part that says what happened (2026-09-15).
        var (head, tail) = kind switch
        {
            AlertKind.AccountDroppedOut => (noun, " dropped out"),
            AlertKind.MemoryWarning => (noun, " — memory warning"),
            AlertKind.Recycled => (noun, " — recycled"),
            // The uptime mark is one synthetic trigger: its DisplayName carries "4h up" and its
            // GameName carries "6 accounts in", composed by the tracker's caller. No identity in
            // either, so streamer mode has nothing to mask.
            AlertKind.UptimeMark => ($"{Name(triggers[0])} — {triggers[0].GameName}", ""),
            // Worded from the rule that fired (2026-09-15). A trigger without one — nothing in
            // production builds that since the coordinator attaches the rule — keeps 1.28's
            // "{noun} — {metric id}". GameName still carries the metric id for this kind.
            AlertKind.MetricBreach => MetricTitle(noun, triggers[0]),
            AlertKind.MetricRecovered => MetricRecoveredTitle(noun, triggers[0]),
            _ => (noun, ""),
        };

        var lines = triggers.Select(t => kind switch
        {
            AlertKind.MemoryWarning when t.PrivateBytes is { } b =>
                $"• {Name(t)} — {b / 1024 / 1024 / 1024.0:0.0} GB · Recycle suggested",
            AlertKind.Recycled when t.PrivateBytes is { } b =>
                $"• {Name(t)} — was {b / 1024 / 1024 / 1024.0:0.0} GB · back in its server",
            AlertKind.Recycled =>
                $"• {Name(t)} — back in its server",
            AlertKind.UptimeMark =>
                "• The scheduled all-good mark. A missing one is worth a look.",
            // Worded from the rule (2026-09-15): Rate gives the measured rate, its window and the
            // floor; Level and Event give the current value. The rule-less arm below it is 1.28's
            // line, kept for a trigger built without a rule. `0.##` there, invariant `#,0.##` here:
            // see FormatNumber.
            AlertKind.MetricBreach or AlertKind.MetricRecovered when t.Rule is { } rule =>
                MetricLine(Name(t), rule, t.MetricValue),
            AlertKind.MetricBreach or AlertKind.MetricRecovered when t.MetricValue is { } v =>
                $"• {Name(t)} — {t.GameName} at {v:0.##}",
            _ => $"• {Name(t)}{(t.GameName is null ? "" : $" — {t.GameName}")}",
        }).ToList();

        return new WebhookPayload(CapTitle(head, tail, envelope.Title), CapLines(lines, envelope.Body));
    }

    /// <summary>
    /// A metric breach's title, from the rule the FIRST trigger carries, as (head, tail). A batch
    /// handed to <see cref="ForAlert"/> shares one (metric id, rule) — <c>MetricBreachBatcher</c>
    /// raises one group per call and <c>AlertRouter</c> keeps a call together — so the first
    /// trigger's rule is the batch's rule. The noun is already "{n} accounts" or the one account's name.
    /// </summary>
    private static (string Head, string Tail) MetricTitle(string noun, AlertTrigger first)
    {
        // A metric that belongs to no account carries Guid.Empty as its subject — the documented
        // global carrier — and ResolveAlertNames answers an unmatched id with two empty strings.
        // So the noun is empty, and pasting it in front of an em dash produced " — Clan points
        // went above 9,000,000,000": a title that opens on a dash and names nobody. Found before
        // any user saw it (2026-09-20), on the first feature to report metrics without accounts.
        // With no noun the label IS the subject, so the dash has nothing to join and goes. Head is
        // shared with MetricRecoveredTitle, so a breach and its recovery can never disagree about
        // who they are about.
        if (first.Rule is not { } rule) return (Head(noun, first.GameName ?? ""), "");

        var head = Head(noun, rule.Label ?? rule.MetricId);
        return rule.Kind switch
        {
            MetricRuleKind.Rate => (head, " stopped climbing"),
            MetricRuleKind.Level when rule.AlertWhenBelow => (head, $" fell below {FormatThreshold(rule.Threshold)}"),
            MetricRuleKind.Level => (head, $" went above {FormatThreshold(rule.Threshold)}"),
            MetricRuleKind.Event => (head, " changed"),
            _ => (head, ""),
        };
    }

    /// <summary>
    /// A recovery's title: the same head as its breach, with the sentence turned around. Whatever
    /// the breach said went wrong, this says is no longer wrong — in the rule's own numbers, so the
    /// two read as a pair rather than as two unrelated alerts about the same thing.
    /// </summary>
    private static (string Head, string Tail) MetricRecoveredTitle(string noun, AlertTrigger first)
    {
        if (first.Rule is not { } rule) return (Head(noun, first.GameName ?? ""), " is back to normal");

        var head = Head(noun, rule.Label ?? rule.MetricId);
        return rule.Kind switch
        {
            MetricRuleKind.Rate => (head, " is climbing again"),
            MetricRuleKind.Level when rule.AlertWhenBelow => (head, $" is back above {FormatThreshold(rule.Threshold)}"),
            MetricRuleKind.Level => (head, $" is back below {FormatThreshold(rule.Threshold)}"),
            _ => (head, " is back to normal"),
        };
    }

    /// <summary>
    /// The subject of a metric title. With no account — the Guid.Empty carrier every metric that
    /// belongs to nobody uses — the label IS the subject, so the dash has nothing to join.
    /// </summary>
    private static string Head(string noun, string label) =>
        string.IsNullOrWhiteSpace(noun) ? label : $"{noun} — {label}";

    /// <summary>
    /// One account's line. No value means no number, never zero. A Rate breach's value is the
    /// measured rate per minute, and it is never negative: <c>MetricHistory.RatePerMinute</c>
    /// returns null on any decrease or zero span, and <c>MetricEvaluator</c> treats null as no breach.
    /// </summary>
    private static string MetricLine(string name, MetricRule rule, double? value)
    {
        // Same empty subject as MetricTitle: with no account there is no name to lead the line,
        // and "•  — now 9,040,000,000" is what that used to render as.
        var lead = string.IsNullOrWhiteSpace(name) ? "• " : $"• {name} — ";
        return (rule.Kind, value) switch
        {
            (_, null) => string.IsNullOrWhiteSpace(name) ? $"• {rule.Label ?? rule.MetricId}" : $"• {name}",
            (MetricRuleKind.Rate, { } rate) =>
                $"{lead}{FormatNumber(rate)} a minute over {FormatNumber(rule.Window.TotalMinutes)} min (alert under {FormatThreshold(rule.Threshold)})",
            (_, { } v) => $"{lead}now {FormatNumber(v)}",
        };
    }

    /// <summary>
    /// A reading: thousands separators from 1,000 up, at most two decimals below that ("50", "0.79",
    /// "2,974,993"). INVARIANT culture on purpose: the sentence around the number is English, and a
    /// German machine's "2.974.993" inside "now ..." reads as a decimal.
    /// </summary>
    private static string FormatNumber(double value) =>
        value.ToString("#,0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// A threshold, as the rule wrote it: every decimal the user typed survives (0.125 stays 0.125,
    /// where <see cref="FormatNumber"/> would round it to 0.13), with the same separators and culture.
    /// </summary>
    private static string FormatThreshold(double value) =>
        value.ToString("#,0.##########", CultureInfo.InvariantCulture);

    /// <summary>
    /// Fit a title into <paramref name="limit"/>. A grouped title uses the account COUNT, so it does
    /// not grow with accounts, but a label (up to 40), a long masked name, and above all a rule
    /// without a label — which puts the plugin-supplied, unbounded metric id in it — can overflow,
    /// and the toast's 63 overflows easily (2026-09-15).
    /// <para>
    /// The rule: the <paramref name="tail"/> (the wording's verb and threshold, "went above
    /// 1,000,000") stays whole, and the <paramref name="head"/> is cut from its END with an ellipsis,
    /// so the label shortens first and the name only once the label is gone. "Diamonds went above
    /// 1,000,000" losing its verb would say nothing; "Diamonds colle… went above 1,000,000" still
    /// does. The tail keeps its place only while it takes at most half the title; a tail longer than
    /// that (a threshold typed with dozens of digits) would squeeze out what fired and for whom, so
    /// then the whole title keeps its front instead, as before.
    /// </para>
    /// </summary>
    private static string CapTitle(string head, string tail, int limit)
    {
        var title = head + tail;
        if (title.Length <= limit) return title;

        // Over the limit with the tail at most half of it means the head is longer than the room
        // left, so the cut below always removes at least one unit of it.
        return tail.Length <= limit / 2
            ? Front(head, limit - tail.Length - 1) + "…" + tail
            : Front(title, limit - 1) + "…";
    }

    /// <summary>
    /// Join the account lines, keeping as many whole lines as fit <paramref name="limit"/>, then say
    /// how many went unnamed ("and 12 more"). Every alert kind, not only metric groups: one line per
    /// account is unbounded, and through 1.28 a mass drop-out could overflow a Discord post while the
    /// toast and the phone still arrived (controller ruling C2, 2026-09-15). The limit is the
    /// destination's, so the desktop toast names fewer accounts than a webhook post of the same
    /// group, and says how many more (final review, 2026-09-15). A first line too long to fit whole
    /// keeps its front rather than leaving the body empty.
    /// </summary>
    private static string CapLines(IReadOnlyList<string> lines, int limit)
    {
        var whole = string.Join("\n", lines);
        if (whole.Length <= limit) return whole;

        // Room for the longest trailer this call could need, so the trailer never pushes a kept
        // line over the limit.
        var budget = limit - $"\nand {lines.Count} more".Length;
        var body = new StringBuilder();
        var shown = 0;
        foreach (var line in lines)
        {
            if (body.Length + (shown == 0 ? 0 : 1) + line.Length > budget) break;
            if (shown > 0) body.Append('\n');
            body.Append(line);
            shown++;
        }

        if (shown == 0)
        {
            body.Append(Front(lines[0], budget - 1)).Append('…');
            shown = 1;
        }

        if (shown < lines.Count) body.Append("\nand ").Append(lines.Count - shown).Append(" more");
        return body.ToString();
    }

    /// <summary>The first <paramref name="length"/> UTF-16 units, one fewer when the cut would
    /// split a surrogate pair (an emoji in a name or a label).</summary>
    private static string Front(string text, int length) =>
        text[..(char.IsHighSurrogate(text[length - 1]) ? length - 1 : length)];
}
