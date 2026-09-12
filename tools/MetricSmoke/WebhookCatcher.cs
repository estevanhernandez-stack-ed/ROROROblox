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
/// <para>
/// Accepts requests concurrently and contains a per-request failure to that one request (see
/// <see cref="AcceptLoopAsync"/> and <see cref="HandleSafelyAsync"/>) — a client reset, a body
/// shorter than its own promised Content-Length, anything mid-read must cost only the POST it
/// happened on. A version of this class that let such a failure escape the accept loop stopped
/// hearing every later request for the rest of the process, silently: every subsequent scenario in
/// a run would have read as "the app never posted" instead of "the catcher broke."
/// <see cref="RequestFaults"/> is how a caller can tell the two apart after the fact.
/// </para>
/// </summary>
public sealed class WebhookCatcher : IAsyncDisposable
{
    public const string MinePath = "/mine";
    public const string ClanPath = "/clan";

    /// <summary>
    /// How long <see cref="DrainAsync"/> waits, after the MOST RECENT arrival, for one more —
    /// resetting on every new arrival rather than counting from the first. A single trigger
    /// routinely posts to both Mine and Clan within milliseconds of each other
    /// (<c>AlertDispatcher.DispatchAsync</c> sends to whichever destinations are routed, back to
    /// back, nothing awaited in between) — a window that only ever counted from the first arrival
    /// could still split that pair across two calls if the first arrival happened to be noticed a
    /// little late. Resetting on every arrival makes any run of posts spaced closer together than
    /// this drain together, however many there are.
    /// <para>
    /// It cannot rescue two posts that are genuinely spaced further apart than this — a caller that
    /// needs both would have to drain again. Public (not just documented) so a test can pin both
    /// sides of the boundary against the real constant, not a guessed number:
    /// <c>WebhookCatcherTests</c> does exactly that.
    /// </para>
    /// </summary>
    public const int QuietPeriodMilliseconds = 150;

    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(QuietPeriodMilliseconds);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    private readonly HttpListener _listener;
    private readonly ConcurrentQueue<CapturedPost> _captured = new();
    private readonly ConcurrentQueue<Exception> _requestFaults = new();
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();
    private readonly Task _acceptLoop;
    private long _lastArrivalTicks;

    public string MineUrl { get; }
    public string ClanUrl { get; }

    /// <summary>
    /// Every exception a per-request handler raised and swallowed to keep the catcher listening
    /// for later posts — see the containment in <see cref="HandleSafelyAsync"/>. Empty on a clean
    /// run. Exists so a caller CAN find out a request was lost to a fault instead of only ever
    /// seeing an absence that reads identically to "the app never posted."
    /// </summary>
    public IReadOnlyList<Exception> RequestFaults => _requestFaults.ToArray();

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

    /// <summary>
    /// Accepts concurrently: each request is handed to <see cref="HandleSafelyAsync"/> without
    /// awaiting it before calling <see cref="HttpListener.GetContextAsync"/> again, so a slow or
    /// stuck request never delays accepting the next one — and, since <see cref="HandleSafelyAsync"/>
    /// never lets an exception escape, nothing thrown while handling one request can end this loop
    /// for the rest of the process. Only <see cref="HttpListener.GetContextAsync"/> itself failing
    /// — which is how <see cref="DisposeAsync"/>'s <c>Stop()</c> signals shutdown — ends it.
    /// </summary>
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

            var handling = HandleSafelyAsync(context);
            _inFlight[handling] = 0;
            _ = handling.ContinueWith(
                completed => _inFlight.TryRemove(completed, out _), TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Wraps <see cref="HandleAsync"/> so a per-request failure ends only this one request. Before
    /// this existed, that exception propagated out of the <c>while</c> loop in
    /// <see cref="AcceptLoopAsync"/> and ended it for the rest of the process: every later POST then
    /// hung against a listener nobody was calling <c>GetContextAsync</c> on anymore, and
    /// <see cref="DisposeAsync"/>'s own catch swallowed the fault with no trace. That is the worst
    /// shape a defect can take here — an unattended run reporting rows as not-fired when the app
    /// fired them correctly — so a caught fault is recorded into <see cref="RequestFaults"/> rather
    /// than dropped: contained enough that it can never take the loop down, visible enough that it
    /// is never mistaken for silence.
    /// </summary>
    private async Task HandleSafelyAsync(HttpListenerContext context)
    {
        try
        {
            await HandleAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _requestFaults.Enqueue(ex);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            using var reader = new StreamReader(
                context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);

            var arrivedAt = DateTimeOffset.UtcNow;
            _captured.Enqueue(new CapturedPost(context.Request.Url?.AbsolutePath ?? string.Empty, body, arrivedAt));
            // Read back by DrainAsync's quiet-period check. Interlocked, not a lock: the accept
            // loop can be handling several requests concurrently now (fix for the loop serialising
            // one request at a time), so more than one of these can run at once.
            Interlocked.Exchange(ref _lastArrivalTicks, arrivedAt.UtcTicks);

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
    /// that budget. Once something has arrived, keeps waiting — bounded by the same
    /// <paramref name="within"/> deadline — until <see cref="QuietPeriod"/> has passed since the
    /// MOST RECENT arrival (not the first), so a run of posts spaced closer together than that
    /// drains as one batch however many of them there are.
    /// </summary>
    public async Task<IReadOnlyList<CapturedPost>> DrainAsync(TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;

        while (DateTime.UtcNow < deadline)
        {
            if (!_captured.IsEmpty)
            {
                var lastArrival = new DateTimeOffset(Interlocked.Read(ref _lastArrivalTicks), TimeSpan.Zero);
                if (DateTimeOffset.UtcNow - lastArrival >= QuietPeriod)
                {
                    break;
                }
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
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
    /// <see cref="AcceptLoopAsync"/> throw and return — waits for every request already dispatched
    /// to a handler to finish, then closes the listener, releasing the port so a second
    /// <see cref="WebhookCatcher"/> can bind fresh in the same process. Waiting for in-flight
    /// handlers matters now that the accept loop is concurrent: without it, <c>Close()</c> could
    /// tear the listener down underneath a response that was still being written.
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
            // AcceptLoopAsync's loop only ever exits via the Stop()-triggered exception above --
            // per-request faults are contained in HandleSafelyAsync and never reach here. This
            // guards only against something else surfacing from awaiting the loop task itself.
        }

        // The accept loop has already stopped, so no new entries can appear here -- safe to
        // snapshot and wait out whatever it had already dispatched before Close() runs.
        var stillRunning = _inFlight.Keys.ToArray();
        if (stillRunning.Length > 0)
        {
            await Task.WhenAll(stillRunning).ConfigureAwait(false);
        }

        _listener.Close();
    }
}
