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
        public List<string> Toasts { get; } = [];
        public void ShowToast(string title, string message) => Toasts.Add($"{title}|{message}");

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
    }

    private const string MineUrl = "https://discord.com/api/webhooks/1/mine";

    private static AlertTrigger Dropped(Guid id, string name) =>
        new(AlertKind.AccountDroppedOut, id, name, "Pet Simulator 99!", null, DateTimeOffset.UtcNow);

    private static (DiscordWebhookSender Sender, StubHttpHandler Handler) Sender(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(status));
        return (new DiscordWebhookSender(new HttpClient(handler), NullLogger.Instance), handler);
    }

    private static AlertDispatcher Build(DiscordWebhookSender sender, ITrayService tray, DiscordConfig config) =>
        new(sender, tray, () => config, new FakeTimeProvider(), NullLogger.Instance);

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
        var dispatcher = new AlertDispatcher(sender, tray, () => config, time, NullLogger.Instance);
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
}
