using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Discord;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class AlertDispatcherTests
{
    /// <summary>
    /// Records the toasts. Every other <see cref="ITrayService"/> member is a no-op — this suite
    /// only exercises the notification path, and a throw-on-unused double would fail on the
    /// event wire-ups rather than on anything the tests care about.
    /// </summary>
    private sealed class SpyTrayService : ITrayService
    {
        private readonly List<string> _toasts = [];

        /// <summary>
        /// Locked because the concurrency test below toasts from sixteen threads at once. A racing
        /// <c>List.Add</c> throws an IndexOutOfRangeException that the dispatcher swallows, which
        /// would abort a dispatch BEFORE it reached the cooldown map — the test would then fail, or
        /// report a short toast count, for a reason that has nothing to do with the map it is about.
        /// </summary>
        public void ShowToast(string title, string message)
        {
            lock (_toasts) { _toasts.Add($"{title}|{message}"); }
        }

        /// <summary>A snapshot, so an assertion never enumerates the list while a producer appends.</summary>
        public IReadOnlyList<string> Toasts
        {
            get { lock (_toasts) { return _toasts.ToArray(); } }
        }

        public void Show() { }
        public void UpdateStatus(MultiInstanceState state) { }
        public void SetMemoryWarning(bool active) { }
        public void ShowMemoryWarning(string title, string message, Guid accountId) { }
        public void Dispose() { }
        public event EventHandler? RequestOpenMainWindow { add { } remove { } }
        public event EventHandler? RequestToggleMutex { add { } remove { } }
        public event EventHandler? RequestStopAllInstances { add { } remove { } }
        public event EventHandler? RequestQuit { add { } remove { } }
        public event EventHandler? RequestOpenDiagnostics { add { } remove { } }
        public event EventHandler? RequestOpenLogs { add { } remove { } }
        public event EventHandler? RequestOpenPreferences { add { } remove { } }
        public event EventHandler? RequestOpenHistory { add { } remove { } }
        public event EventHandler? RequestOpenPlugins { add { } remove { } }
        public event EventHandler? RequestActivateMain { add { } remove { } }
        public event EventHandler<Guid>? RequestFocusAccount { add { } remove { } }
        public event EventHandler<MultiInstanceState>? StatusChanged { add { } remove { } }
    }

    private const string MineUrl = "https://discord.com/api/webhooks/1/mine";

    private static AlertTrigger Dropped(Guid id, string name) =>
        new(AlertKind.AccountDroppedOut, id, name, $"real_{name}", "Pet Simulator 99!", null, DateTimeOffset.UtcNow);

    /// <summary>Any kind, for the suites that care about the kind rather than the wording.</summary>
    private static AlertTrigger Of(AlertKind kind, Guid id) =>
        new(kind, id, "BaronBloxwell", "real_BaronBloxwell", "Pet Simulator 99!", null, DateTimeOffset.UtcNow);

    private static AlertTrigger Metric(Guid id) => Of(AlertKind.MetricBreach, id);

    private static (DiscordWebhookSender Sender, StubHttpHandler Handler) Sender(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(status));
        return (new DiscordWebhookSender(new HttpClient(handler), NullLogger<DiscordWebhookSender>.Instance), handler);
    }

    private static AlertDispatcher Build(DiscordWebhookSender sender, ITrayService tray, DiscordConfig config) =>
        new(sender, tray, () => config, new FakeTimeProvider(), NullLogger<AlertDispatcher>.Instance);

    [Fact]
    public async Task DispatchAsync_LocalDestination_RaisesATrayToastAndPostsNothing()
    {
        var (sender, handler) = Sender(HttpStatusCode.NoContent);
        var tray = new SpyTrayService();
        var dispatcher = Build(sender, tray, new DiscordConfig { DroppedOutDestination = AlertDestination.Local });

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "BaronBloxwell")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("BaronBloxwell", Assert.Single(tray.Toasts), StringComparison.Ordinal);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task DispatchAsync_MineDestination_PostsToTheWebhookAndNotTheTray()
    {
        var (sender, handler) = Sender(HttpStatusCode.NoContent);
        var tray = new SpyTrayService();
        var dispatcher = Build(sender, tray, new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MineWebhookUrl = MineUrl,
        });

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "BaronBloxwell")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("BaronBloxwell", Assert.Single(handler.Bodies), StringComparison.Ordinal);
        Assert.Empty(tray.Toasts);
    }

    [Fact]
    public async Task DispatchAsync_ClanDestination_UsesTheRealNameNotTheStreamerAlias()
    {
        // The one destination exempt from streamer mode. A clan channel is a room the user
        // deliberately joined, full of people who already know which accounts are theirs — a board
        // of invented names there is worse than useless, because nobody can act on it.
        var (sender, handler) = Sender(HttpStatusCode.NoContent);
        var dispatcher = Build(sender, new SpyTrayService(), new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Clan,
            ClanWebhookUrl = "https://discord.com/api/webhooks/2/clan",
        });

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "CaptainNoodle")]).WaitAsync(TimeSpan.FromSeconds(5));

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("real_CaptainNoodle", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchAsync_PersonalChannel_StillMasksTheName()
    {
        // The exemption is scoped to the clan room and nowhere else. The personal channel can be
        // in a server with other people in it, and the desktop toast renders on a screen that may
        // be on stream — both keep the alias.
        var (sender, handler) = Sender(HttpStatusCode.NoContent);
        var dispatcher = Build(sender, new SpyTrayService(), new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MineWebhookUrl = MineUrl,
        });

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "CaptainNoodle")]).WaitAsync(TimeSpan.FromSeconds(5));

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("CaptainNoodle", body, StringComparison.Ordinal);
        Assert.DoesNotContain("real_CaptainNoodle", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchAsync_DesktopToast_StillMasksTheName()
    {
        var (sender, _) = Sender(HttpStatusCode.NoContent);
        var tray = new SpyTrayService();
        var dispatcher = Build(sender, tray, new DiscordConfig { DroppedOutDestination = AlertDestination.Local });

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "CaptainNoodle")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain("real_CaptainNoodle", Assert.Single(tray.Toasts), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchAsync_SameAccountTwice_SecondIsSuppressedByCooldown()
    {
        var (sender, _) = Sender(HttpStatusCode.NoContent);
        var tray = new SpyTrayService();
        var dispatcher = Build(sender, tray, new DiscordConfig { DroppedOutDestination = AlertDestination.Local });
        var id = Guid.NewGuid();

        await dispatcher.DispatchAsync([Dropped(id, "A")]).WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DispatchAsync([Dropped(id, "A")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(tray.Toasts);
    }

    [Fact]
    public async Task DispatchAsync_AfterTheCooldownElapses_TheSameAccountAlertsAgain()
    {
        // Companion to the suppression test: the cooldown has to actually expire, or a client that
        // drops once at 9am is silent for the rest of the day.
        var (sender, _) = Sender(HttpStatusCode.NoContent);
        var tray = new SpyTrayService();
        var time = new FakeTimeProvider();
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Local };
        var dispatcher = new AlertDispatcher(sender, tray, () => config, time, NullLogger<AlertDispatcher>.Instance);
        var id = Guid.NewGuid();

        await dispatcher.DispatchAsync([Dropped(id, "A")]).WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(AlertRouter.Cooldown.Add(TimeSpan.FromSeconds(1)));
        await dispatcher.DispatchAsync([Dropped(id, "A")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, tray.Toasts.Count);
    }

    [Fact]
    public async Task DispatchAsync_WebhookGone_MarksTheDestinationRejected()
    {
        var (sender, _) = Sender(HttpStatusCode.NotFound);
        var dispatcher = Build(sender, new SpyTrayService(), new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MineWebhookUrl = MineUrl,
        });

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "A")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(dispatcher.MineWebhookRejected);
    }

    [Fact]
    public async Task DispatchAsync_AfterAWebhookIsGone_StopsPostingAndFallsBackToTheTray()
    {
        // THE test for the 404-is-terminal claim. Marking a flag is not the same as acting on it:
        // without this, a deleted webhook is re-POSTed on every single alert forever, and the
        // alerts themselves vanish — the user is told nothing while we retry a dead URL all day.
        var (sender, handler) = Sender(HttpStatusCode.NotFound);
        var tray = new SpyTrayService();
        var dispatcher = Build(sender, tray, new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MineWebhookUrl = MineUrl,
        });

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "First")]).WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "Second")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(handler.Bodies);                                  // the dead URL is not tried twice
        Assert.Contains("Second", Assert.Single(tray.Toasts), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchAsync_ARateLimitedPost_DoesNotKillTheDestination()
    {
        // 429 is transient. Treating it like a 404 would silently downgrade a working webhook to
        // desktop-only for the rest of the session because Discord was briefly busy.
        var (sender, _) = Sender(HttpStatusCode.TooManyRequests);
        var dispatcher = Build(sender, new SpyTrayService(), new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MineWebhookUrl = MineUrl,
        });

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "A")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(dispatcher.MineWebhookRejected);
    }

    [Fact]
    public async Task DispatchAsync_NoTriggers_DoesNothing()
    {
        var (sender, handler) = Sender(HttpStatusCode.NoContent);
        var tray = new SpyTrayService();
        var dispatcher = Build(sender, tray, new DiscordConfig { DroppedOutDestination = AlertDestination.Local });

        await dispatcher.DispatchAsync([]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(tray.Toasts);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task DispatchAsync_EverythingOff_SendsNothingAnywhere()
    {
        // The shipped default. Nothing leaves the machine, and nothing pops on screen, until the
        // user picks a destination.
        var (sender, handler) = Sender(HttpStatusCode.NoContent);
        var tray = new SpyTrayService();
        var dispatcher = Build(sender, tray, new DiscordConfig());

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "A")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(tray.Toasts);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task DispatchAsync_ManyProducersAtOnce_DoesNotCorruptTheCooldownMap()
    {
        // The dispatcher has TWO fire-and-forget producers wired in App.xaml.cs: the view model,
        // raising on the UI thread, and the metric sink, raising on whatever gRPC handler thread
        // served a plugin's report. While the view model was the only one, an unsynchronised
        // Dictionary was safe. It stopped being safe the moment MetricBreach got a destination.
        //
        // Sixteen producers rather than two: two threads reproduce the corruption only by luck,
        // and this test has to fail against the unfixed field, not merely be pointed at it.
        const int Producers = 16;
        const int RoundsEach = 200;
        const int TriggersPerDispatch = 8;

        var (sender, handler) = Sender(HttpStatusCode.NoContent);
        var tray = new SpyTrayService();
        var dispatcher = Build(sender, tray, new DiscordConfig
        {
            // Every kind has to route SOMEWHERE, or DispatchAsync returns at the "routed nowhere"
            // guard and never reaches the map write this test exists to stress. Local keeps the
            // stress on the map instead of on an HTTP double.
            DroppedOutDestinations = [AlertDestination.Local],
            MemoryWarningDestinations = [AlertDestination.Local],
            RecycledDestinations = [AlertDestination.Local],
            UptimeMarkDestinations = [AlertDestination.Local],
            // MetricBreachDestinations already defaults to Local.
        });

        // Prove the routing before trusting anything below: one dispatch, one toast. Without this
        // the whole test would pass while exercising nothing at all.
        await dispatcher.DispatchAsync([Metric(Guid.NewGuid())]).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(tray.Toasts);

        var kinds = Enum.GetValues<AlertKind>();
        var work = Enumerable.Range(0, Producers).Select(producer => Task.Run(async () =>
        {
            for (var round = 0; round < RoundsEach; round++)
            {
                // Fresh ids every round, so nothing is suppressed by the cooldown and the map only
                // ever grows — growth is when a Dictionary rehashes, and rehashing under a
                // concurrent read is what corrupts the bucket chain.
                var kind = kinds[(producer + round) % kinds.Length];
                var triggers = Enumerable.Range(0, TriggersPerDispatch)
                    .Select(_ => Of(kind, Guid.NewGuid()))
                    .ToArray();
                await dispatcher.DispatchAsync(triggers).ConfigureAwait(false);
            }
        })).ToArray();

        // The timeout IS the assertion, as much as the toast count is. DispatchAsync swallows every
        // exception by design, so a corrupted Dictionary that THROWS is invisible here — what it
        // does visibly is spin forever inside a bucket chain that points at itself.
        var all = Task.WhenAll(work);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(60)));
        Assert.Same(all, finished);
        await all;

        // Each dispatch coalesces its triggers into exactly one RoutedAlert, so one toast each.
        // A short count is the swallowed-throw half of the same corruption.
        Assert.Equal((Producers * RoundsEach) + 1, tray.Toasts.Count);
        Assert.Empty(handler.Bodies);
    }
}
