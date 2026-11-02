using System.Net;
using System.Text;

namespace ROROROblox.EndpointProbe;

/// <summary>One recorded request/response. Holds no request header and no secret.</summary>
public sealed record ProbeStep(
    string Label,
    string Method,
    string Url,
    int? Status,
    string? Error,
    IReadOnlyList<KeyValuePair<string, string>> RblxHeaders,
    bool CsrfTokenPresent,
    bool TicketHeaderPresent,
    string? Body);

/// <summary>
/// Makes the three requests C1 needs measured and records what Roblox said: status, every
/// <c>rblx-*</c> response header, and the body. The requests mirror what <c>RobloxApi</c> sends (same
/// endpoints, the same CSRF dance, the same <c>Cookie</c> attachment, <c>UseCookies=false</c> on the
/// handler the caller builds) but they are issued raw, because <c>RobloxApi</c> turns a 403 into an
/// exception and the headers this exists to read go with it.
/// <para>
/// THE COOKIE. It is passed in once, attached to each request's <c>Cookie</c> header, and held nowhere
/// else. Nothing on <see cref="ProbeStep"/> can carry it: request headers are not recorded, response
/// headers are recorded by allow-list. <see cref="Redactor"/> is the second line, not the first.
/// </para>
/// </summary>
public sealed class Prober
{
    public const string AuthTicketEndpoint = "https://auth.roblox.com/v1/authentication-ticket";
    public const string PresenceEndpoint = "https://presence.roblox.com/v1/presence/users";
    public const string MembershipEndpointFormat = "https://premiumfeatures.roblox.com/v1/users/{0}/validate-membership";
    private const string Referer = "https://www.roblox.com/";
    private const int MaxBodyChars = 4096;

    private readonly HttpClient _http;
    private readonly string _cookie;
    private readonly long? _userId;
    private readonly Redactor _redactor;
    private readonly TimeSpan _pace;
    private readonly Action<string> _log;

    public Prober(HttpClient http, string cookie, long? userId, Redactor redactor, TimeSpan pace, Action<string> log)
    {
        _http = http;
        _cookie = cookie;
        _userId = userId;
        _redactor = redactor;
        _pace = pace;
        _log = log;
        _redactor.AddSecret(cookie);
    }

    public async Task<IReadOnlyList<ProbeStep>> RunAsync(CancellationToken ct)
    {
        var steps = new List<ProbeStep>();

        // 1. The ticket POST, with the CSRF dance the client does: the first 403 carrying x-csrf-token
        //    is the handshake, the second POST is the real answer, and a rotated token earns exactly
        //    one more try (RobloxApi's rule; never more, a bot-challenge is not a thing to hammer).
        var first = await SendAsync("ticket POST 1 (CSRF handshake)", BuildTicketPost(null), steps, ct).ConfigureAwait(false);
        if (first is { Status: 403, CsrfToken: { Length: > 0 } token })
        {
            await PauseAsync(ct).ConfigureAwait(false);
            var second = await SendAsync("ticket POST 2 (with the CSRF token)", BuildTicketPost(token), steps, ct).ConfigureAwait(false);
            if (second is { Status: 403, CsrfToken: { Length: > 0 } rotated }
                && !string.Equals(rotated, token, StringComparison.Ordinal))
            {
                await PauseAsync(ct).ConfigureAwait(false);
                await SendAsync("ticket POST 3 (rotated CSRF token)", BuildTicketPost(rotated), steps, ct).ConfigureAwait(false);
            }
        }

        // 2 and 3 need the numeric id, which lives in the vault once RoRoRo has resolved it.
        if (_userId is not { } id)
        {
            steps.Add(Skipped("presence POST", "POST", PresenceEndpoint));
            steps.Add(Skipped("validate-membership GET", "GET", string.Format(MembershipEndpointFormat, "<uid>")));
            return steps;
        }

        await PauseAsync(ct).ConfigureAwait(false);
        var presence = new HttpRequestMessage(HttpMethod.Post, PresenceEndpoint)
        {
            Content = new StringContent($"{{\"userIds\":[{id}]}}", Encoding.UTF8, "application/json"),
        };
        await SendAsync("presence POST", presence, steps, ct).ConfigureAwait(false);

        await PauseAsync(ct).ConfigureAwait(false);
        var membership = new HttpRequestMessage(HttpMethod.Get, string.Format(MembershipEndpointFormat, id));
        await SendAsync("validate-membership GET", membership, steps, ct).ConfigureAwait(false);

        return steps;
    }

    private HttpRequestMessage BuildTicketPost(string? csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AuthTicketEndpoint);
        request.Headers.Add("Referer", Referer);
        if (!string.IsNullOrEmpty(csrfToken))
        {
            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        }
        request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
        return request;
    }

    private ProbeStep Skipped(string label, string method, string url) =>
        new(label, method, url, null, "skipped: no Roblox user id stored for this account yet "
            + "(RoRoRo's backfill resolves it; open RoRoRo once, then re-run)", [], false, false, null);

    private Task PauseAsync(CancellationToken ct) => _pace > TimeSpan.Zero ? Task.Delay(_pace, ct) : Task.CompletedTask;

    private sealed record Seen(int? Status, string? CsrfToken);

    private async Task<Seen> SendAsync(string label, HttpRequestMessage request, List<ProbeStep> steps, CancellationToken ct)
    {
        // The only place the cookie meets a request. Set here so BuildTicketPost and the others
        // cannot forget it, and so no recorded field ever sits next to it.
        request.Headers.Add("Cookie", $".ROBLOSECURITY={_cookie}");
        var url = _redactor.Scrub(request.RequestUri?.ToString());
        _log($"  {label} ...");

        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            var rblx = new List<KeyValuePair<string, string>>();
            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                if (!header.Key.StartsWith("rblx-", StringComparison.OrdinalIgnoreCase)) continue;
                var value = string.Join(", ", header.Value);
                rblx.Add(new KeyValuePair<string, string>(
                    header.Key.ToLowerInvariant(),
                    header.Key.Equals("rblx-challenge-metadata", StringComparison.OrdinalIgnoreCase)
                        ? _redactor.DecodeChallengeMetadata(value)
                        : _redactor.Scrub(value)));
            }

            string? csrf = null;
            if (response.Headers.TryGetValues("x-csrf-token", out var csrfValues))
            {
                csrf = csrfValues.FirstOrDefault();
                _redactor.AddSecret(csrf);
            }
            string? ticket = null;
            if (response.Headers.TryGetValues("RBX-Authentication-Ticket", out var ticketValues))
            {
                ticket = ticketValues.FirstOrDefault();
                _redactor.AddSecret(ticket);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (body.Length > MaxBodyChars) body = body[..MaxBodyChars] + "\n<truncated>";

            var status = (int)response.StatusCode;
            steps.Add(new ProbeStep(label, request.Method.Method, url, status, null, rblx,
                CsrfTokenPresent: !string.IsNullOrEmpty(csrf),
                TicketHeaderPresent: !string.IsNullOrEmpty(ticket),
                Body: _redactor.Scrub(body)));
            _log($"    -> {status}{(rblx.Count > 0 ? "  [" + string.Join(", ", rblx.Select(h => h.Key)) + "]" : string.Empty)}");
            return new Seen(status, csrf);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The exception text can name a host and a reason; it cannot name a header value.
            steps.Add(new ProbeStep(label, request.Method.Method, url, null, ex.GetType().Name + ": " + _redactor.Scrub(ex.Message),
                [], false, false, null));
            _log($"    -> failed ({ex.GetType().Name})");
            return new Seen(null, null);
        }
    }
}
