using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Discord;
using ROROROblox.App.Notify;
using ROROROblox.Core;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Notify;

namespace ROROROblox.Tests.Notify;

using StubHttpHandler = ROROROblox.Tests.Discord.StubHttpHandler;

/// <summary>
/// The phone-alert seam (spec 2026-09-04): a new <see cref="AlertDestination.Phone"/> on the
/// existing router, two provider senders cloning <c>DiscordWebhookSender</c>'s contract, and the
/// dispatcher's terminal-rejection handling. These pin the routing fallbacks and the
/// result-mapping tables — the live buzz itself is the spec's §4 smoke.
/// </summary>
public class PhoneRoutingTests
{
    private static AlertTrigger Dropped(Guid id, string name) =>
        new(AlertKind.AccountDroppedOut, id, name, name, null, null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Route_PhoneConfigured_RoutesToPhone()
    {
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Phone };

        var routed = AlertRouter.Route([Dropped(Guid.NewGuid(), "A")], config,
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(), DateTimeOffset.UnixEpoch,
            phoneConfigured: true);

        Assert.Equal(AlertDestination.Phone, Assert.Single(routed).Destination);
    }

    [Fact]
    public void Route_PhoneNotConfigured_FallsBackToDesktop()
    {
        // Same rule as a webhook destination with no URL: routed somewhere unconfigured falls
        // back to the desktop toast rather than dropping — a silently vanishing alert is the
        // worst outcome.
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Phone };

        var routed = AlertRouter.Route([Dropped(Guid.NewGuid(), "A")], config,
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(), DateTimeOffset.UnixEpoch,
            phoneConfigured: false);

        Assert.Equal(AlertDestination.Local, Assert.Single(routed).Destination);
    }

    [Fact]
    public void Route_DefaultPhoneConfiguredArgument_IsUnconfigured()
    {
        // The appended-optional keeps old call sites compiling; the safe reading of "caller
        // didn't say" is "not configured".
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Phone };

        var routed = AlertRouter.Route([Dropped(Guid.NewGuid(), "A")], config,
            new Dictionary<(Guid, AlertKind), DateTimeOffset>(), DateTimeOffset.UnixEpoch);

        Assert.Equal(AlertDestination.Local, Assert.Single(routed).Destination);
    }
}

public class PhoneNotifyConfigTests
{
    [Theory]
    [InlineData(PhoneProvider.None, null, null, null, false)]
    [InlineData(PhoneProvider.Pushover, "u23456789012345678901234567890", "a23456789012345678901234567890", null, true)]
    [InlineData(PhoneProvider.Pushover, "u23456789012345678901234567890", null, null, false)]
    [InlineData(PhoneProvider.Pushover, null, "a23456789012345678901234567890", null, false)]
    [InlineData(PhoneProvider.Ntfy, null, null, "rororo-abcdefghijklmnopqrstuvwxyz", true)]
    [InlineData(PhoneProvider.Ntfy, null, null, null, false)]
    public void IsConfigured_RequiresTheSelectedProvidersCredentials(
        PhoneProvider provider, string? userKey, string? appToken, string? topic, bool expected)
    {
        var config = new PhoneNotifyConfig
        {
            Provider = provider,
            PushoverUserKey = userKey,
            PushoverAppToken = appToken,
            NtfyTopic = topic,
        };

        Assert.Equal(expected, config.IsConfigured);
    }

    [Fact]
    public async Task Store_RoundTrips_ThroughDpapi()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-test-{Guid.NewGuid():N}", "notify.dat");
        try
        {
            var store = new PhoneNotifyConfigStore(path);
            var saved = new PhoneNotifyConfig
            {
                Provider = PhoneProvider.Ntfy,
                NtfyTopic = NtfyTopicGenerator.NewTopic(),
                NtfyServerUrl = "https://ntfy.example",
            };

            await store.SaveAsync(saved).WaitAsync(TimeSpan.FromSeconds(5));
            var loaded = await store.LoadAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(saved, loaded);

            // The file on disk must not be the plaintext topic — it is a bearer credential.
            var raw = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain(saved.NtfyTopic!, raw, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}

public class NtfyTopicGeneratorTests
{
    [Fact]
    public void NewTopic_IsPrefixedLongAndTopicSafe()
    {
        var topic = NtfyTopicGenerator.NewTopic();

        Assert.StartsWith("rororo-", topic, StringComparison.Ordinal);
        Assert.Equal("rororo-".Length + 26, topic.Length);
        Assert.Matches("^[a-z0-9-]+$", topic);
    }

    [Fact]
    public void NewTopic_DoesNotRepeat()
    {
        // 128 bits of CSPRNG: a collision here means the generator is broken, not unlucky.
        var topics = Enumerable.Range(0, 64).Select(_ => NtfyTopicGenerator.NewTopic()).ToList();
        Assert.Equal(topics.Count, topics.Distinct(StringComparer.Ordinal).Count());
    }
}

public class PhoneCredentialValidatorTests
{
    [Fact]
    public void InspectPushoverKey_AcceptsThirtyAlnum()
    {
        var verdict = PhoneCredentialValidator.InspectPushoverKey(" u2345678901234567890123456789A ", "user key");
        Assert.Equal(PhoneCredentialKind.Valid, verdict.Kind);
        Assert.Equal("u2345678901234567890123456789A", verdict.Normalized);
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/1/tok", PhoneCredentialKind.WebhookUrl)]
    [InlineData("este@example.com", PhoneCredentialKind.WrongShape)]
    [InlineData("tooshort", PhoneCredentialKind.WrongShape)]
    [InlineData("", PhoneCredentialKind.Empty)]
    public void InspectPushoverKey_NamesTheMistakeWithoutEchoingIt(string pasted, PhoneCredentialKind expected)
    {
        var verdict = PhoneCredentialValidator.InspectPushoverKey(pasted, "user key");

        Assert.Equal(expected, verdict.Kind);
        if (pasted.Length > 0)
        {
            // The contract inherited from WebhookUrlValidator: the message never repeats the
            // paste — these strings get screenshotted into clan channels.
            Assert.DoesNotContain(pasted, verdict.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("https://ntfy.sh", "https://ntfy.sh")]
    [InlineData("https://ntfy.example/ ", "https://ntfy.example")]
    public void InspectNtfyServer_AcceptsAbsoluteHttp(string pasted, string normalized)
    {
        var verdict = PhoneCredentialValidator.InspectNtfyServer(pasted);
        Assert.Equal(PhoneCredentialKind.Valid, verdict.Kind);
        Assert.Equal(normalized, verdict.Normalized);
    }

    [Fact]
    public void InspectNtfyServer_RejectsBareHostname()
    {
        Assert.Equal(PhoneCredentialKind.WrongShape, PhoneCredentialValidator.InspectNtfyServer("ntfy.sh").Kind);
    }
}

public class PushoverSenderTests
{
    private static readonly WebhookPayload Payload = new("BaronBloxwell dropped out", "• BaronBloxwell — Pet Sim");
    private const string UserKey = "u23456789012345678901234567890";
    private const string AppToken = "a23456789012345678901234567890";

    private static PushoverSender Build(StubHttpHandler handler) =>
        new(new HttpClient(handler), NullLogger<PushoverSender>.Instance);

    [Fact]
    public async Task SendAsync_Success_PostsFormWithDropPriorityOne()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var result = await Build(handler)
            .SendAsync(UserKey, AppToken, AlertKind.AccountDroppedOut, Payload)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PhoneSendResult.Sent, result);
        var body = Assert.Single(handler.Bodies);
        Assert.Contains("priority=1", body, StringComparison.Ordinal);
        Assert.Contains("BaronBloxwell", Uri.UnescapeDataString(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_MemoryWarning_IsPriorityZero()
    {
        // Only the drop bypasses quiet hours; a memory warning waking someone at 3am would get
        // the feature turned off.
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await Build(handler)
            .SendAsync(UserKey, AppToken, AlertKind.MemoryWarning, Payload)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("priority=0", Assert.Single(handler.Bodies), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, PhoneSendResult.EndpointRejected)]
    [InlineData(HttpStatusCode.Unauthorized, PhoneSendResult.EndpointRejected)]
    [InlineData(HttpStatusCode.TooManyRequests, PhoneSendResult.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, PhoneSendResult.Failed)]
    public async Task SendAsync_MapsStatusToResult(HttpStatusCode status, PhoneSendResult expected)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(status));
        var result = await Build(handler)
            .SendAsync(UserKey, AppToken, AlertKind.AccountDroppedOut, Payload)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expected, result);
    }
}

public class NtfySenderTests
{
    private static readonly WebhookPayload Payload = new("BaronBloxwell dropped out", "• BaronBloxwell — Pet Sim");

    private static NtfySender Build(StubHttpHandler handler) =>
        new(new HttpClient(handler), NullLogger<NtfySender>.Instance);

    [Fact]
    public async Task SendAsync_PostsToTopicWithStaticAsciiTitle()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHttpHandler(r => { seen = r; return new HttpResponseMessage(HttpStatusCode.OK); });

        var result = await Build(handler)
            .SendAsync("https://ntfy.sh/", "rororo-topic", AlertKind.AccountDroppedOut, Payload)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PhoneSendResult.Sent, result);
        Assert.Equal("https://ntfy.sh/rororo-topic", seen!.RequestUri!.ToString());
        // The Title header is the static app name: account names are not header-safe, and a name
        // must never be able to break — or inject into — the request envelope. The payload's own
        // title leads the body instead.
        Assert.Equal("RoRoRo", Assert.Single(seen.Headers.GetValues("Title")));
        Assert.Equal("high", Assert.Single(seen.Headers.GetValues("Priority")));
        Assert.StartsWith("BaronBloxwell dropped out", Assert.Single(handler.Bodies), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, PhoneSendResult.EndpointRejected)]
    [InlineData(HttpStatusCode.TooManyRequests, PhoneSendResult.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, PhoneSendResult.Failed)]
    public async Task SendAsync_MapsStatusToResult(HttpStatusCode status, PhoneSendResult expected)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(status));
        var result = await Build(handler)
            .SendAsync("https://ntfy.sh", "rororo-topic", AlertKind.MemoryWarning, Payload)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expected, result);
    }
}

public class PhoneDispatchTests
{
    /// <summary>Every member a no-op: these cases never reach the tray.</summary>
    private sealed class NoopTrayService : ITrayService
    {
        public void Show() { }
        public void UpdateStatus(MultiInstanceState state) { }
        public void ShowToast(string title, string message) { }
        public void SetMemoryWarning(bool active) { }
        public void ShowMemoryWarning(string title, string message, Guid accountId) { }
        public void Dispose() { }
#pragma warning disable CS0067
        public event EventHandler<MultiInstanceState>? StatusChanged;
        public event EventHandler? RequestOpenMainWindow;
        public event EventHandler? RequestToggleMutex;
        public event EventHandler? RequestStopAllInstances;
        public event EventHandler? RequestQuit;
        public event EventHandler? RequestOpenDiagnostics;
        public event EventHandler? RequestOpenLogs;
        public event EventHandler? RequestOpenPreferences;
        public event EventHandler? RequestOpenHistory;
        public event EventHandler? RequestOpenPlugins;
        public event EventHandler? RequestActivateMain;
        public event EventHandler<Guid>? RequestFocusAccount;
#pragma warning restore CS0067
    }

    private static AlertTrigger Dropped(Guid id, string name) =>
        new(AlertKind.AccountDroppedOut, id, name, name, null, null, DateTimeOffset.UnixEpoch);

    private static (AlertDispatcher Dispatcher, StubHttpHandler PhoneHttp) Build(
        PhoneNotifyConfig phoneConfig, Func<HttpRequestMessage, HttpResponseMessage> phoneRespond)
    {
        var discordConfig = new DiscordConfig { DroppedOutDestination = AlertDestination.Phone };
        var webhookSender = new DiscordWebhookSender(
            new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent))),
            NullLogger<DiscordWebhookSender>.Instance);

        var phoneHttp = new StubHttpHandler(phoneRespond);
        var phoneSender = new PhoneAlertSender(
            new PushoverSender(new HttpClient(phoneHttp), NullLogger<PushoverSender>.Instance),
            new NtfySender(new HttpClient(phoneHttp), NullLogger<NtfySender>.Instance),
            NullLogger<PhoneAlertSender>.Instance);

        var dispatcher = new AlertDispatcher(
            webhookSender, new NoopTrayService(), () => discordConfig, new FakeTimeProvider(),
            NullLogger<AlertDispatcher>.Instance, phoneSender, () => phoneConfig);
        return (dispatcher, phoneHttp);
    }

    private static PhoneNotifyConfig NtfyConfig() => new()
    {
        Provider = PhoneProvider.Ntfy,
        NtfyTopic = "rororo-testtopic",
    };

    [Fact]
    public async Task Dispatch_PhoneDestination_SendsThroughTheProvider()
    {
        var (dispatcher, phoneHttp) = Build(NtfyConfig(), _ => new HttpResponseMessage(HttpStatusCode.OK));

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "A")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(phoneHttp.Bodies);
        Assert.False(dispatcher.PhoneRejected);
    }

    [Fact]
    public async Task Dispatch_RejectedCredentials_AreTerminalForTheSession()
    {
        // Same contract as a webhook 404: the provider named the credentials as bad, so re-POSTing
        // them on every alert forever helps nobody. The next dispatch routes to the desktop toast
        // via the router's phoneConfigured fallback; ResetPhoneRejection gives saved-again
        // credentials a fresh chance.
        var (dispatcher, phoneHttp) = Build(NtfyConfig(), _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await dispatcher.DispatchAsync([Dropped(a, "A")]).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(dispatcher.PhoneRejected);

        await dispatcher.DispatchAsync([Dropped(b, "B")]).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(phoneHttp.Bodies);

        dispatcher.ResetPhoneRejection();
        Assert.False(dispatcher.PhoneRejected);
    }
}

public class PhoneStatusLineTests
{
    [Fact]
    public void Compose_PhoneRoutedButUnconfigured_IsAFailure()
    {
        var line = AlertStatusLine.Compose(
            new DiscordConfig { DroppedOutDestination = AlertDestination.Phone },
            phoneConfigured: false);

        Assert.True(line.IsFailure);
        Assert.Contains("phone", line.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_PhoneRejected_OutranksTheHappyPath()
    {
        var line = AlertStatusLine.Compose(
            new DiscordConfig { DroppedOutDestination = AlertDestination.Phone },
            phoneRejected: true,
            phoneConfigured: true,
            phoneProviderName: "Pushover");

        Assert.True(line.IsFailure);
    }

    [Fact]
    public void Compose_PhoneConfigured_NamesTheProviderInTheHappyLine()
    {
        var line = AlertStatusLine.Compose(
            new DiscordConfig { DroppedOutDestination = AlertDestination.Phone },
            phoneConfigured: true,
            phoneProviderName: "Pushover");

        Assert.False(line.IsFailure);
        Assert.Contains("your phone (Pushover)", line.Text, StringComparison.Ordinal);
    }
}
