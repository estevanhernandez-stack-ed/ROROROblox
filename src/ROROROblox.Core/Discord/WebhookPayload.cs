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
    public static WebhookPayload ForAlert(AlertKind kind, IReadOnlyList<AlertTrigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        if (triggers.Count == 0) throw new ArgumentException("No triggers.", nameof(triggers));

        var noun = triggers.Count == 1 ? triggers[0].DisplayName : $"{triggers.Count} accounts";
        var title = kind switch
        {
            AlertKind.AccountDroppedOut => $"{noun} dropped out",
            AlertKind.MemoryWarning => $"{noun} — memory warning",
            _ => noun,
        };

        var lines = triggers.Select(t => kind switch
        {
            AlertKind.MemoryWarning when t.PrivateBytes is { } b =>
                $"• {t.DisplayName} — {b / 1024 / 1024 / 1024.0:0.0} GB · Recycle suggested",
            _ => $"• {t.DisplayName}{(t.GameName is null ? "" : $" — {t.GameName}")}",
        });

        return new WebhookPayload(title, string.Join("\n", lines));
    }
}
