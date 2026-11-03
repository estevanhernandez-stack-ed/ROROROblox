using System.Text;

namespace ROROROblox.EndpointProbe;

/// <summary>
/// Renders the redacted markdown report. Takes <see cref="ProbeStep"/>s, which cannot hold a secret,
/// and a display name, which is the account's LOCAL name. Nothing here sees a cookie, a token or a
/// ticket; <see cref="Redactor"/> has already scrubbed every free-text field.
/// </summary>
public static class ReportWriter
{
    public static string Render(string accountName, string mode, DateTimeOffset at, IReadOnlyList<ProbeStep> steps, Redactor redactor)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Endpoint probe: {redactor.Scrub(accountName)}");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {at.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- Mode: {mode}");
        sb.AppendLine("- Account: named by the first 8 characters of its local id, never its Roblox name (`--list` maps the two on");
        sb.AppendLine("  the machine that holds the vault); the Roblox user id is `<uid>` and a server id is `<gameId>` everywhere.");
        sb.AppendLine("- Redaction: the request `Cookie` header, every `Set-Cookie`, every `x-csrf-token` value and the");
        sb.AppendLine("  `RBX-Authentication-Ticket` value are never written. `rblx-*` headers are verbatim except");
        sb.AppendLine("  `rblx-challenge-metadata`, which is base64-decoded with every bare number of five or more digits");
        sb.AppendLine("  replaced by `<n>`.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Request | Status | `rblx-challenge-type` |");
        sb.AppendLine("|---|---|---|");
        foreach (var step in steps)
        {
            var type = step.RblxHeaders.FirstOrDefault(h => h.Key == "rblx-challenge-type").Value;
            sb.AppendLine($"| {step.Label} | {StatusText(step)} | {(string.IsNullOrEmpty(type) ? "none" : "`" + type + "`")} |");
        }

        foreach (var step in steps)
        {
            sb.AppendLine();
            sb.AppendLine($"## {step.Label}");
            sb.AppendLine();
            sb.AppendLine($"`{step.Method} {step.Url}`");
            sb.AppendLine();
            sb.AppendLine($"- Status: {StatusText(step)}");
            if (step.Error is { } error) sb.AppendLine($"- Note: {error}");
            if (step.Status is not null)
            {
                sb.AppendLine($"- `x-csrf-token`: {(step.CsrfTokenPresent ? "present, value withheld" : "absent")}");
                sb.AppendLine($"- `RBX-Authentication-Ticket`: {(step.TicketHeaderPresent ? "present, value withheld" : "absent")}");
                if (step.RblxHeaders.Count == 0)
                {
                    sb.AppendLine("- `rblx-*` headers: none");
                }
                else
                {
                    sb.AppendLine("- `rblx-*` headers:");
                    foreach (var (name, value) in step.RblxHeaders)
                    {
                        if (name == "rblx-challenge-metadata")
                        {
                            sb.AppendLine($"  - `{name}` (decoded, numbers redacted):");
                            sb.AppendLine("    ```json");
                            sb.AppendLine("    " + value.Replace("\n", "\n    "));
                            sb.AppendLine("    ```");
                        }
                        else
                        {
                            sb.AppendLine($"  - `{name}`: `{value}`");
                        }
                    }
                }
                sb.AppendLine();
                sb.AppendLine("Body:");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(string.IsNullOrEmpty(step.Body) ? "(empty)" : step.Body);
                sb.AppendLine("```");
            }
        }

        return sb.ToString();
    }

    private static string StatusText(ProbeStep step) => step.Status is { } s ? s.ToString() : "no response";
}
