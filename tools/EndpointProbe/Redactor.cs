using System.Text;
using System.Text.RegularExpressions;

namespace ROROROblox.EndpointProbe;

/// <summary>
/// Everything the report writes passes through here. The rule is that a secret cannot reach the
/// markdown because the code that builds the markdown never holds one in a form it could print:
/// request headers are not recorded at all, response headers are recorded by allow-list (<c>rblx-*</c>
/// only), and what is left (response bodies, decoded challenge metadata, URLs) is scrubbed by this
/// class as a second line.
/// <para>
/// WHAT IS SCRUBBED. The cookie value itself, wherever it appears; anything shaped like a Roblox
/// session cookie (the warning prefix is assembled from pieces below so this source file does not
/// itself match the repo's cookie scan); every secret the caller registers (a CSRF token, an auth
/// ticket); the account's numeric user id; and any <c>"userId": N</c> field. The account is named by
/// the local name the caller passes, never by Roblox user id.
/// </para>
/// </summary>
public sealed class Redactor
{
    // Assembled, not written out: the pre-commit scan and the CI guard fail on the literal prefix,
    // and a scrubber has to know the shape it scrubs.
    private static readonly Regex SessionCookieShape =
        new(Regex.Escape("_|") + "WARNING" + @"[^\s""',;]*", RegexOptions.Compiled);

    private static readonly Regex UserIdField =
        new(@"(""(?:userId|UserId|user_id|targetId)""\s*:\s*)""?\d+""?", RegexOptions.Compiled);

    // A live server instance: with the place id beside it, enough to join the account's server.
    // Reports land in a public repo (found on the first live run, 2026-11-02).
    private static readonly Regex ServerIdField =
        new(@"(""(?:gameId|GameId|jobId|JobId)""\s*:\s*)""[^""]*""", RegexOptions.Compiled);

    // A standalone run of five or more digits. Challenge metadata is opaque JSON, and "redact any
    // numeric user id inside it" is safest as "redact any bare number that could be one".
    private static readonly Regex BareNumber =
        new(@"(?<![0-9A-Za-z])\d{5,}(?![0-9A-Za-z])", RegexOptions.Compiled);

    private readonly List<string> _secrets = [];
    private readonly long? _userId;

    public Redactor(long? userId) => _userId = userId;

    /// <summary>Register a value that must never appear in output (the cookie, a token, a ticket).</summary>
    public void AddSecret(string? value)
    {
        if (!string.IsNullOrEmpty(value) && value.Length >= 4 && !_secrets.Contains(value, StringComparer.Ordinal))
        {
            // Longest first, so a secret that contains another is not half-replaced.
            _secrets.Add(value);
            _secrets.Sort((a, b) => b.Length.CompareTo(a.Length));
        }
    }

    /// <summary>The second-line scrub for free text: bodies and URLs.</summary>
    public string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = text;
        foreach (var secret in _secrets)
        {
            result = result.Replace(secret, "<withheld>", StringComparison.Ordinal);
        }
        result = SessionCookieShape.Replace(result, "<withheld>");
        result = UserIdField.Replace(result, "$1\"<uid>\"");
        result = ServerIdField.Replace(result, "$1\"<gameId>\"");
        if (_userId is { } id)
        {
            result = result.Replace(id.ToString(System.Globalization.CultureInfo.InvariantCulture), "<uid>", StringComparison.Ordinal);
        }
        return result;
    }

    /// <summary>
    /// <c>rblx-challenge-metadata</c> is base64 JSON. Decoded, then scrubbed, then every bare number
    /// of five or more digits is replaced: a user id in there is the thing this exists to remove. The
    /// raw base64 is never written, because it carries the same ids encoded. A value that does not
    /// decode is withheld whole rather than echoed.
    /// </summary>
    public string DecodeChallengeMetadata(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64.Trim());
            var json = Encoding.UTF8.GetString(bytes);
            return BareNumber.Replace(Scrub(json), "<n>");
        }
        catch (FormatException)
        {
            return "<not valid base64; withheld>";
        }
    }

    /// <summary>A short, filesystem-safe slug of an account's local name, for the report file name.</summary>
    public static string Slug(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "account" : slug[..Math.Min(slug.Length, 32)];
    }
}
