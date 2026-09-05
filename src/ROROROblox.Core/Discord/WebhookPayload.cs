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
    /// <paramref name="useRealNames"/> is set only for the clan destination — see
    /// <see cref="AlertTrigger"/> for why that one room is exempt from streamer mode. Defaults to
    /// false so any future caller that forgets the question gets the masked names.
    /// </summary>
    public static WebhookPayload ForAlert(
        AlertKind kind, IReadOnlyList<AlertTrigger> triggers, bool useRealNames = false)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        if (triggers.Count == 0) throw new ArgumentException("No triggers.", nameof(triggers));

        string Name(AlertTrigger t) => useRealNames ? t.RealName : t.DisplayName;

        var noun = triggers.Count == 1 ? Name(triggers[0]) : $"{triggers.Count} accounts";
        var title = kind switch
        {
            AlertKind.AccountDroppedOut => $"{noun} dropped out",
            AlertKind.MemoryWarning => $"{noun} — memory warning",
            AlertKind.Recycled => $"{noun} — recycled",
            // The uptime mark is one synthetic trigger: its DisplayName carries "4h up" and its
            // GameName carries "6 accounts in", composed by the tracker's caller. No identity in
            // either, so streamer mode has nothing to mask.
            AlertKind.UptimeMark => $"{Name(triggers[0])} — {triggers[0].GameName}",
            _ => noun,
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
            _ => $"• {Name(t)}{(t.GameName is null ? "" : $" — {t.GameName}")}",
        });

        return new WebhookPayload(title, string.Join("\n", lines));
    }
}
