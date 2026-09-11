using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ROROROblox.MetricSmoke;

/// <summary>
/// One POST as the app actually sent it — never parsed into a model here, because the callers
/// this exists for (design task 5) assert on the literal rendered text: a formatted metric value
/// ("0.79", not "0"), or which of two names showed up. <see cref="Path"/> is the request's
/// absolute path, which is how a caller tells <see cref="WebhookCatcher.MineUrl"/> apart from
/// <see cref="WebhookCatcher.ClanUrl"/> — the two share one listener and one port.
/// </summary>
public sealed record CapturedPost(string Path, string Body, DateTimeOffset ArrivedAt);

/// <summary>
/// A localhost stand-in for the two Discord webhooks <c>DiscordWebhookSender</c> posts alerts to.
/// <para>
/// This exists because the app's log records an alert's title but never its body (spec §0.4): the
/// log alone cannot prove a fractional observed value renders as "0.79" rather than "0"
/// (<c>WebhookPayload.ForAlert</c>'s <c>{v:0.##}</c> format, chosen after that exact bug), nor that
/// the personal channel gets the masked account name while the clan channel gets the real one
/// (<c>useRealNames</c> is true only for <see cref="ROROROblox.Core.Discord.AlertDestination.Clan"/>).
/// Only capturing the actual request body settles either question.
/// </para>
/// <para>
/// Binds port 0 and reads back what the OS assigned — see <see cref="ReserveFreeLoopbackPort"/> —
/// rather than a fixed port, so this harness cannot fail just because something else on the
/// machine already holds one. The prefix is the literal loopback address <c>127.0.0.1</c>, never
/// <c>+</c>, <c>*</c>, or a real adapter address, so the listener is unreachable from outside the
/// machine.
/// </para>
/// </summary>
public sealed class WebhookCatcher : IAsyncDisposable
{
    public const string MinePath = "/mine";
    public const string ClanPath = "/clan";

    /// <summary>
    /// How long a first arrival waits for a near-simultaneous companion before
    /// <see cref="DrainAsync"/> hands back what it has. A single trigger routinely posts to both
    /// Mine and Clan within milliseconds of each other (<c>AlertDispatcher.DispatchAsync</c> sends
    /// to whichever destinations are routed, back to back) — without this, a caller could drain
    /// right between the two and see only one. Never applied when nothing has arrived at all: a
    /// caller asserting "nothing posted" still waits out its own full <c>within</c> budget below,
    /// not this shorter one.
    /// </summary>
    private static readonly TimeSpan SettleGrace = TimeSpan.FromMilliseconds(150);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    private readonly HttpListener _listener;
    private readonly ConcurrentQueue<CapturedPost> _captured = new();
    private readonly Task _acceptLoop;

    public string MineUrl { get; }
    public string ClanUrl { get; }

    private WebhookCatcher(HttpListener listener, int port)
    {
        _listener = listener;
        MineUrl = $"http://127.0.0.1:{port}{MinePath}";
        ClanUrl = $"http://127.0.0.1:{port}{ClanPath}";
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>
    /// Starts listening on a free loopback port and returns the two URLs a scenario should hand
    /// to <c>DiscordConfigStore</c> in place of the real Mine/Clan webhooks.
    /// <para>
    /// <c>HttpListener</c> documents an exemption for a literal <c>localhost</c>/loopback host: no
    /// <c>netsh http add urlacl</c> reservation is needed to start one as a non-administrator,
    /// unlike a wildcard (<c>+</c>/<c>*</c>) or a real machine-name prefix. If URL reservation were
    /// ever denied anyway — a locked-down machine, a Group Policy override — <see cref="Start"/>
    /// throws <see cref="HttpListenerException"/> from this call, deliberately unhandled: a harness
    /// run should fail loudly at setup, not report a false green with no catcher actually
    /// listening.
    /// </para>
    /// </summary>
    public static WebhookCatcher Start()
    {
        var port = ReserveFreeLoopbackPort();

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        return new WebhookCatcher(listener, port);
    }

    /// <summary>
    /// Asks the OS for a free loopback port by binding a throwaway <see cref="TcpListener"/> to
    /// port 0, reading back whatever it was given, then releasing it immediately —
    /// <see cref="HttpListener"/> has no "pick any port" mode of its own, so this is the standard
    /// workaround (the same technique ASP.NET Core's own test host uses for the identical reason).
    /// Releasing before <see cref="HttpListener.Start"/> reuses the number leaves a narrow window
    /// where another process could grab the same port first; nothing in .NET closes that window,
    /// and it is accepted here as the cost of not hardcoding a port.
    /// </summary>
    private static int ReserveFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Thrown by Stop()/Close() tearing down a pending accept during DisposeAsync —
                // this is the listener's own shutdown signal reaching us, not a fault to surface.
                return;
            }

            await HandleAsync(context).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            using var reader = new StreamReader(
                context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);

            _captured.Enqueue(new CapturedPost(
                context.Request.Url?.AbsolutePath ?? string.Empty, body, DateTimeOffset.UtcNow));

            // 204 No Content: the exact status Discord's real webhook execute endpoint returns on
            // a fire-and-forget post (the app never passes ?wait=true), and — checked against
            // DiscordWebhookSender.SendAsync directly, not assumed — the branch that matters here
            // is `response.IsSuccessStatusCode`, true for any 2xx, checked BEFORE the 404 branch
            // that marks a webhook dead for the rest of the session. A wrong status here would
            // silently disable every later scenario's webhook, not just this one post's.
            context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            context.Response.ContentLength64 = 0;
        }
        finally
        {
            context.Response.Close();
        }
    }

    /// <summary>
    /// Hands back everything captured so far. If nothing has arrived yet, waits up to
    /// <paramref name="within"/> for a first arrival and returns empty rather than hanging past
    /// that budget. If something has already arrived — including if it arrives while this call is
    /// waiting — waits <see cref="SettleGrace"/> longer before draining, so a companion post
    /// landing milliseconds later (Mine and Clan firing from the same trigger) is not split across
    /// two calls.
    /// </summary>
    public async Task<IReadOnlyList<CapturedPost>> DrainAsync(TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (_captured.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        if (!_captured.IsEmpty)
        {
            var grace = SettleGrace < within ? SettleGrace : within;
            await Task.Delay(grace).ConfigureAwait(false);
        }

        var drained = new List<CapturedPost>();
        while (_captured.TryDequeue(out var post))
        {
            drained.Add(post);
        }
        return drained;
    }

    /// <summary>
    /// Stops the listener — which makes the pending <see cref="HttpListener.GetContextAsync"/> in
    /// <see cref="AcceptLoopAsync"/> throw and return — then closes it, releasing the port so a
    /// second <see cref="WebhookCatcher"/> can bind fresh in the same process. One harness process
    /// runs every scenario in a pass; a port that outlived its catcher would starve the next one.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch
        {
            // AcceptLoopAsync already swallows the Stop()-triggered exception internally; this
            // guards only against something else surfacing from awaiting the loop task itself.
        }
        _listener.Close();
    }
}
