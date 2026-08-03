# Discord Per-Account Alerts + Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two alerts — account dropped out, memory warning — reach the user when they are away from the PC, and the setup survives someone who has never made a Discord webhook.

**Architecture:** Pure routing in `ROROROblox.Core/Discord/` (`AlertRouter`, `WebhookPayload`, `WebhookUrlValidator`); the world-facing shell in `ROROROblox.App/Discord/` (`DiscordWebhookSender`, `AlertDispatcher`). Triggers come from signals that already exist: `IMemoryWatchdog.PressureCrossed` and the process/presence pair that detects a dropped client. Routing is per-trigger, muting is per-account.

**Tech Stack:** .NET 10, C# 14, WPF, `HttpClient` + `System.Net.Http.Json`, DPAPI, xUnit.

**Spec:** [`../specs/2026-08-03-discord-presence-alerts-design.md`](../specs/2026-08-03-discord-presence-alerts-design.md)
**Prerequisite:** [`2026-08-03-discord-presence-join.md`](2026-08-03-discord-presence-join.md) Task 1 (`DiscordConfig`, `DiscordConfigStore`, `AlertDestination`).

## Global Constraints

- **Build:** `dotnet build ROROROblox.slnx`. **Test:** `dotnet test ROROROblox.slnx`. Close `ROROROblox.App` first — it locks `ROROROblox.Core.dll`.
- **No test may sleep in real time.** Cooldown and coalescing take an injected `TimeProvider` or an explicit "now" parameter — never `DateTime.UtcNow` read inside the unit under test. Beware the `FakeTimeProvider` trap documented in `FpsCapSettlerTests`: advancing the clock in one jump can arm a timer against a stopped clock.
- **No mocking library.** Hand-rolled fakes, following `MainViewModelTests`.
- **Defaults are off.** Every destination starts at `AlertDestination.None`.
- **Streamer mode is honored outbound** — alert text renders account names through `IStreamerIdentityProvider`.
- **A webhook payload can never carry a private-server link.** This is a type constraint, not a code review rule.
- **No telemetry.** Setup friction is invisible to us by design; the substitute is copy that explains itself.
- **Conventional commits.** `secret-scan` and `local-path-guard` hooks must pass.

---

### Task 1: `AlertTrigger` + `WebhookPayload`

The payload type is the security boundary: it is shaped so a private-server link has nowhere to go.

**Files:**
- Create: `src/ROROROblox.Core/Discord/AlertTrigger.cs`
- Create: `src/ROROROblox.Core/Discord/WebhookPayload.cs`
- Test: `src/ROROROblox.Tests/Discord/WebhookPayloadTests.cs`

**Interfaces:**
- Consumes: `AlertDestination` (presence plan, Task 1).
- Produces: `AlertKind` enum (`AccountDroppedOut`, `MemoryWarning`), `AlertTrigger(AlertKind Kind, Guid AccountId, string DisplayName, string? GameName, long? PrivateBytes, DateTimeOffset OccurredAtUtc)`, `WebhookPayload(string Title, string Body)` with `static WebhookPayload ForAlert(AlertKind, IReadOnlyList<AlertTrigger>)`.

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class WebhookPayloadTests
{
    private static AlertTrigger Dropped(string name) =>
        new(AlertKind.AccountDroppedOut, Guid.NewGuid(), name, "Pet Simulator 99!", null,
            new DateTimeOffset(2026, 8, 3, 3, 14, 0, TimeSpan.Zero));

    [Fact]
    public void ForAlert_SingleDroppedAccount_NamesItAndTheGame()
    {
        var payload = WebhookPayload.ForAlert(AlertKind.AccountDroppedOut, [Dropped("BaronBloxwell")]);

        Assert.Contains("BaronBloxwell", payload.Body, StringComparison.Ordinal);
        Assert.Contains("Pet Simulator 99!", payload.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ForAlert_ManyAccountsAtOnce_IsOneMessageNotSeveral()
    {
        // Eight accounts crossing a threshold in one watchdog sweep is one buzz, not eight.
        var payload = WebhookPayload.ForAlert(AlertKind.MemoryWarning,
            [Dropped("A"), Dropped("B"), Dropped("C")]);

        Assert.Contains("3 accounts", payload.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A", payload.Body, StringComparison.Ordinal);
        Assert.Contains("C", payload.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void WebhookPayload_HasNoFieldThatCouldCarryAServerLink()
    {
        // THE test for this task, and it is a design assertion rather than a behavior one: the
        // type is the boundary. A presence Join secret reaches people who can see your Join
        // button; a channel post reaches everyone who ever reads that channel, including people
        // who join it next year. Adding a Url/Link/Code property here makes this fail.
        var properties = typeof(WebhookPayload).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(["Title", "Body"], properties.Order().ToArray());
        Assert.All(properties, p =>
        {
            Assert.DoesNotContain("url", p, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("link", p, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("code", p, StringComparison.OrdinalIgnoreCase);
        });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~WebhookPayloadTests"`
Expected: FAIL — `AlertTrigger` not found.

- [ ] **Step 3: Write minimal implementation**

`AlertTrigger.cs`:

```csharp
namespace ROROROblox.Core.Discord;

/// <summary>The two things worth waking someone up for. Deliberately not extensible without a
/// design decision — session-expired and landed-elsewhere were considered and cut (spec §11).</summary>
public enum AlertKind
{
    AccountDroppedOut,
    MemoryWarning,
}

/// <summary>One alert-worthy event. <paramref name="DisplayName"/> is already rendered through
/// streamer mode by the caller.</summary>
public sealed record AlertTrigger(
    AlertKind Kind,
    Guid AccountId,
    string DisplayName,
    string? GameName,
    long? PrivateBytes,
    DateTimeOffset OccurredAtUtc);
```

`WebhookPayload.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~WebhookPayloadTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Discord/ src/ROROROblox.Tests/Discord/WebhookPayloadTests.cs
git commit -m "feat(discord): alert triggers + a payload type that cannot carry a link

The payload is a security boundary expressed as a type: two strings, no field a
private-server link could live in. 'We remember not to send it' is a rule that
erodes; a type that cannot represent it does not. A reflection test fails if
anyone adds a Url/Link/Code property later."
```

---

### Task 2: `AlertRouter` — routing, muting, coalescing, cooldown

**Files:**
- Create: `src/ROROROblox.Core/Discord/AlertRouter.cs`
- Test: `src/ROROROblox.Tests/Discord/AlertRouterTests.cs`

**Interfaces:**
- Consumes: `AlertTrigger`, `AlertKind` (Task 1), `DiscordConfig`, `AlertDestination` (presence plan Task 1).
- Produces: `AlertRouter.Route(IReadOnlyList<AlertTrigger> pending, DiscordConfig config, IReadOnlyDictionary<Guid, DateTimeOffset> lastSentPerAccount, DateTimeOffset nowUtc) → IReadOnlyList<RoutedAlert>`, `RoutedAlert(AlertDestination Destination, AlertKind Kind, IReadOnlyList<AlertTrigger> Triggers)`, `AlertRouter.Cooldown` (5 minutes).

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class AlertRouterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 3, 14, 0, TimeSpan.Zero);
    private static readonly Guid AccountA = Guid.NewGuid();
    private static readonly Guid AccountB = Guid.NewGuid();

    private static AlertTrigger Trigger(AlertKind kind, Guid id, string name, DateTimeOffset? at = null) =>
        new(kind, id, name, "Pet Simulator 99!", 4_000_000_000, at ?? Now);

    private static readonly Dictionary<Guid, DateTimeOffset> NothingSentYet = new();

    [Fact]
    public void Route_TriggerSetToNone_ProducesNothing()
    {
        // The default. Nothing outbound until the user configures it.
        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")],
            new DiscordConfig(), NothingSentYet, Now);

        Assert.Empty(routed);
    }

    [Fact]
    public void Route_SendsEachTriggerToItsOwnConfiguredDestination()
    {
        // The whole point of per-trigger routing: health to me, other things elsewhere.
        var config = new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MemoryWarningDestination = AlertDestination.Local,
        };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A"), Trigger(AlertKind.MemoryWarning, AccountB, "B")],
            config, NothingSentYet, Now);

        Assert.Equal(2, routed.Count);
        Assert.Equal(AlertDestination.Mine, routed.Single(r => r.Kind == AlertKind.AccountDroppedOut).Destination);
        Assert.Equal(AlertDestination.Local, routed.Single(r => r.Kind == AlertKind.MemoryWarning).Destination);
    }

    [Fact]
    public void Route_MutedAccount_ProducesNothingForThatAccountOnly()
    {
        var config = new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MutedAccountIds = [AccountA],
        };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "Muted"),
             Trigger(AlertKind.AccountDroppedOut, AccountB, "Loud")],
            config, NothingSentYet, Now);

        var alert = Assert.Single(routed);
        Assert.Equal("Loud", Assert.Single(alert.Triggers).DisplayName);
    }

    [Fact]
    public void Route_ManyAccountsSameKind_CoalescesIntoOneAlert()
    {
        // Eight accounts crossing the memory threshold in one sweep is one message.
        var config = new DiscordConfig { MemoryWarningDestination = AlertDestination.Mine };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.MemoryWarning, AccountA, "A"), Trigger(AlertKind.MemoryWarning, AccountB, "B")],
            config, NothingSentYet, Now);

        Assert.Equal(2, Assert.Single(routed).Triggers.Count);
    }

    [Fact]
    public void Route_AccountAlertedInsideTheCooldown_IsSuppressed()
    {
        // A flapping client must not page someone every thirty seconds.
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Mine };
        var lastSent = new Dictionary<Guid, DateTimeOffset> { [AccountA] = Now.AddMinutes(-1) };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")], config, lastSent, Now);

        Assert.Empty(routed);
    }

    [Fact]
    public void Route_AccountAlertedBeforeTheCooldownExpired_SendsAgain()
    {
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Mine };
        var lastSent = new Dictionary<Guid, DateTimeOffset> { [AccountA] = Now - AlertRouter.Cooldown.Add(TimeSpan.FromSeconds(1)) };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")], config, lastSent, Now);

        Assert.Single(routed);
    }

    [Fact]
    public void Route_MineDestinationWithNoWebhookConfigured_FallsBackToLocal()
    {
        // The silliest failure mode: routed to "my channel", no webhook pasted, alert vanishes.
        // Falling back to the desktop notification means the user still finds out.
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Mine, MineWebhookUrl = null };

        var routed = AlertRouter.Route(
            [Trigger(AlertKind.AccountDroppedOut, AccountA, "A")], config, NothingSentYet, Now);

        Assert.Equal(AlertDestination.Local, Assert.Single(routed).Destination);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~AlertRouterTests"`
Expected: FAIL — `AlertRouter` not found.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace ROROROblox.Core.Discord;

/// <summary>One alert ready to send: where, what kind, and which accounts it covers.</summary>
public sealed record RoutedAlert(
    AlertDestination Destination,
    AlertKind Kind,
    IReadOnlyList<AlertTrigger> Triggers);

/// <summary>
/// Decides what actually gets sent. Pure — the caller supplies "now" and the per-account
/// last-sent map, so cooldown behavior is a table of cases rather than a test that sleeps.
/// <para>
/// Routing is per-trigger and muting is per-account, which keeps the configuration surface at two
/// controls. The full matrix (8 accounts x 2 triggers x 3 destinations) is 48 switches nobody
/// finishes setting up.
/// </para>
/// </summary>
public static class AlertRouter
{
    /// <summary>Per-account quiet period. A client that flaps must not page someone repeatedly.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<RoutedAlert> Route(
        IReadOnlyList<AlertTrigger> pending,
        DiscordConfig config,
        IReadOnlyDictionary<Guid, DateTimeOffset> lastSentPerAccount,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(config);

        var muted = config.MutedAccountIds.ToHashSet();

        return pending
            .Where(t => !muted.Contains(t.AccountId))
            .Where(t => !lastSentPerAccount.TryGetValue(t.AccountId, out var last) || nowUtc - last > Cooldown)
            .GroupBy(t => t.Kind)
            .Select(group => new { group.Key, Triggers = group.ToList(), Destination = Resolve(group.Key, config) })
            .Where(x => x.Destination != AlertDestination.None)
            .Select(x => new RoutedAlert(x.Destination, x.Key, x.Triggers))
            .ToList();
    }

    private static AlertDestination Resolve(AlertKind kind, DiscordConfig config)
    {
        var wanted = kind switch
        {
            AlertKind.AccountDroppedOut => config.DroppedOutDestination,
            AlertKind.MemoryWarning => config.MemoryWarningDestination,
            _ => AlertDestination.None,
        };

        // Routed somewhere that isn't configured yet -> fall back to the desktop notification
        // rather than dropping it. A silently vanishing alert is the worst outcome here.
        return wanted switch
        {
            AlertDestination.Mine when string.IsNullOrWhiteSpace(config.MineWebhookUrl) => AlertDestination.Local,
            AlertDestination.Clan when string.IsNullOrWhiteSpace(config.ClanWebhookUrl) => AlertDestination.Local,
            _ => wanted,
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~AlertRouterTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Discord/AlertRouter.cs src/ROROROblox.Tests/Discord/AlertRouterTests.cs
git commit -m "feat(discord): pure alert routing with coalescing and cooldown

Per-trigger routing plus per-account mute — two controls instead of a 48-switch
matrix nobody finishes. Coalesces a whole watchdog sweep into one message, and a
destination with no webhook configured falls back to the desktop notification
rather than dropping the alert, which is the silliest way to lose one.

Pure: 'now' and the last-sent map are parameters, so cooldown is a table of cases
rather than a test that sleeps."
```

---

### Task 3: `WebhookUrlValidator` — the paste field that names what it got

**Files:**
- Create: `src/ROROROblox.Core/Discord/WebhookUrlValidator.cs`
- Test: `src/ROROROblox.Tests/Discord/WebhookUrlValidatorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `WebhookUrlValidator.Inspect(string? pasted) → WebhookUrlVerdict`, `WebhookUrlVerdict(WebhookUrlKind Kind, string? NormalizedUrl, string Message)`, `WebhookUrlKind` enum (`Valid`, `Empty`, `ServerInvite`, `ChannelLink`, `BotToken`, `Unrecognized`).

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class WebhookUrlValidatorTests
{
    [Fact]
    public void Inspect_ValidWebhook_IsAcceptedAndNormalized()
    {
        var verdict = WebhookUrlValidator.Inspect("https://discord.com/api/webhooks/000000000000000000/abcDEF123");

        Assert.Equal(WebhookUrlKind.Valid, verdict.Kind);
        Assert.Equal("https://discord.com/api/webhooks/000000000000000000/abcDEF123", verdict.NormalizedUrl);
    }

    [Fact]
    public void Inspect_WebhookPastedInsideOtherText_IsExtracted()
    {
        // People paste with surrounding chat text. Rejecting that is a support ticket.
        var verdict = WebhookUrlValidator.Inspect(
            "here you go: https://discord.com/api/webhooks/123/tok  (from #alerts)");

        Assert.Equal(WebhookUrlKind.Valid, verdict.Kind);
        Assert.Equal("https://discord.com/api/webhooks/123/tok", verdict.NormalizedUrl);
    }

    [Fact]
    public void Inspect_ServerInvite_SaysWhatItIsAndWhereToGetTheRealThing()
    {
        var verdict = WebhookUrlValidator.Inspect("https://discord.gg/abc123");

        Assert.Equal(WebhookUrlKind.ServerInvite, verdict.Kind);
        Assert.Contains("invite", verdict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Integrations", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_ChannelLink_IsDistinguishedFromAWebhook()
    {
        var verdict = WebhookUrlValidator.Inspect("https://discord.com/channels/123456/789012");

        Assert.Equal(WebhookUrlKind.ChannelLink, verdict.Kind);
        Assert.Contains("not a webhook", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_BotToken_WarnsRatherThanJustRejecting()
    {
        // A bot token in a paste field is a credential the user is about to leak. Say so.
        var verdict = WebhookUrlValidator.Inspect("EXAMPLE-NOT-A-REAL-TOKEN-00000.EXAMPL.EXAMPLE-NOT-A-REAL-TOKEN-00000");

        Assert.Equal(WebhookUrlKind.BotToken, verdict.Kind);
        Assert.Contains("don't share", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Inspect_Empty_IsItsOwnQuietCase(string? input)
    {
        Assert.Equal(WebhookUrlKind.Empty, WebhookUrlValidator.Inspect(input).Kind);
    }

    [Fact]
    public void Inspect_Nonsense_IsUnrecognizedNotValid()
    {
        Assert.Equal(WebhookUrlKind.Unrecognized, WebhookUrlValidator.Inspect("banana").Kind);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~WebhookUrlValidatorTests"`
Expected: FAIL — `WebhookUrlValidator` not found.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text.RegularExpressions;

namespace ROROROblox.Core.Discord;

public enum WebhookUrlKind
{
    Valid,
    Empty,
    ServerInvite,
    ChannelLink,
    BotToken,
    Unrecognized,
}

public sealed record WebhookUrlVerdict(WebhookUrlKind Kind, string? NormalizedUrl, string Message);

/// <summary>
/// Names what the user actually pasted. Nobody gets a webhook URL right the first time, and
/// "invalid URL" teaches them nothing — the four wrong things people paste are each recognisable,
/// so each gets told what it is and where the real one lives.
/// </summary>
public static partial class WebhookUrlValidator
{
    [GeneratedRegex(@"https://(?:\w+\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w\-]+", RegexOptions.IgnoreCase)]
    private static partial Regex WebhookRegex();

    [GeneratedRegex(@"^[\w\-]{20,}\.[\w\-]{5,}\.[\w\-]{20,}$")]
    private static partial Regex BotTokenRegex();

    public static WebhookUrlVerdict Inspect(string? pasted)
    {
        if (string.IsNullOrWhiteSpace(pasted))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.Empty, null, "");
        }

        var text = pasted.Trim();

        var match = WebhookRegex().Match(text);
        if (match.Success)
        {
            return new WebhookUrlVerdict(WebhookUrlKind.Valid, match.Value, "");
        }

        if (text.Contains("discord.gg/", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.ServerInvite, null,
                "That's a server invite. You need a webhook — in Discord: Server Settings → Integrations → Webhooks → New Webhook, then Copy Webhook URL.");
        }

        if (text.Contains("/channels/", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.ChannelLink, null,
                "That's a link to the channel, not a webhook. Same channel, different button: Server Settings → Integrations → Webhooks → New Webhook.");
        }

        if (BotTokenRegex().IsMatch(text))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.BotToken, null,
                "That looks like a bot token — don't share that anywhere, and reset it if you pasted it somewhere public. A webhook URL starts with discord.com/api/webhooks/.");
        }

        return new WebhookUrlVerdict(WebhookUrlKind.Unrecognized, null,
            "That doesn't look like a webhook URL. It should start with discord.com/api/webhooks/ — Server Settings → Integrations → Webhooks → Copy Webhook URL.");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~WebhookUrlValidatorTests"`
Expected: PASS, 9 tests (7 facts + 3 theory cases, minus overlap).

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Discord/WebhookUrlValidator.cs src/ROROROblox.Tests/Discord/WebhookUrlValidatorTests.cs
git commit -m "feat(discord): paste-field validator that names what the user actually pasted

'Invalid URL' teaches nobody anything. The four wrong things people paste — a
server invite, a channel link, a bot token, or surrounding chat text with the URL
buried in it — are each recognisable, so each gets told what it is and where the
real one lives. A pasted bot token gets a warning, not a rejection: that is a
credential the user is one paste away from leaking."
```

---

### Task 4: `DiscordWebhookSender`

**Files:**
- Create: `src/ROROROblox.App/Discord/DiscordWebhookSender.cs`
- Test: `src/ROROROblox.Tests/Discord/DiscordWebhookSenderTests.cs`

**Interfaces:**
- Consumes: `WebhookPayload` (Task 1).
- Produces: `DiscordWebhookSender(HttpClient client, ILogger log)` with `Task<WebhookSendResult> SendAsync(string url, WebhookPayload payload, CancellationToken ct = default)`, `WebhookSendResult` enum (`Sent`, `WebhookGone`, `RateLimited`, `Failed`).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class DiscordWebhookSenderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null) Bodies.Add(await request.Content.ReadAsStringAsync(ct));
            return respond(request);
        }
    }

    private static readonly WebhookPayload Payload = new("BaronBloxwell dropped out", "• BaronBloxwell — Pet Simulator 99!");
    private const string Url = "https://discord.com/api/webhooks/1/tok";

    [Fact]
    public async Task SendAsync_Success_ReportsSentAndPostsTheText()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sender = new DiscordWebhookSender(new HttpClient(handler), NullLogger.Instance);

        var result = await sender.SendAsync(Url, Payload).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WebhookSendResult.Sent, result);
        Assert.Contains("BaronBloxwell", Assert.Single(handler.Bodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_404_ReportsWebhookGoneAndDoesNotRetry()
    {
        // A deleted webhook never comes back. Retrying it forever is how a background loop
        // outlives the reason for it.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sender = new DiscordWebhookSender(new HttpClient(handler), NullLogger.Instance);

        var result = await sender.SendAsync(Url, Payload).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WebhookSendResult.WebhookGone, result);
        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task SendAsync_429_ReportsRateLimited()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var sender = new DiscordWebhookSender(new HttpClient(handler), NullLogger.Instance);

        var result = await sender.SendAsync(Url, Payload).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WebhookSendResult.RateLimited, result);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_ReportsFailedRatherThanThrowing()
    {
        // No Discord failure may affect the app. An alert is a passenger too.
        var handler = new StubHandler(_ => throw new HttpRequestException("no network"));
        var sender = new DiscordWebhookSender(new HttpClient(handler), NullLogger.Instance);

        var result = await sender.SendAsync(Url, Payload).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WebhookSendResult.Failed, result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~DiscordWebhookSenderTests"`
Expected: FAIL — `DiscordWebhookSender` not found.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

public enum WebhookSendResult
{
    Sent,
    WebhookGone,
    RateLimited,
    Failed,
}

/// <summary>
/// Posts an alert to a Discord webhook. Accepts only <see cref="WebhookPayload"/>, which by
/// construction cannot carry a private-server link.
/// <para>
/// A 404 is terminal — a deleted webhook does not come back, and the caller disables that
/// destination rather than retrying forever.
/// </para>
/// </summary>
public sealed class DiscordWebhookSender(HttpClient client, ILogger log)
{
    public async Task<WebhookSendResult> SendAsync(string url, WebhookPayload payload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            var body = new { content = $"**{payload.Title}**\n{payload.Body}" };
            using var response = await client.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return WebhookSendResult.Sent;
            if (response.StatusCode == HttpStatusCode.NotFound) return WebhookSendResult.WebhookGone;
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return WebhookSendResult.RateLimited;

            log.LogDebug("Webhook post returned {Status}.", (int)response.StatusCode);
            return WebhookSendResult.Failed;
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Webhook post failed.");
            return WebhookSendResult.Failed;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~DiscordWebhookSenderTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Discord/DiscordWebhookSender.cs src/ROROROblox.Tests/Discord/DiscordWebhookSenderTests.cs
git commit -m "feat(discord): webhook sender with terminal 404 handling

Accepts only WebhookPayload, so a private-server link has no way in. A 404 is
terminal — a deleted webhook does not come back, and the caller disables that
destination instead of retrying forever. Network failures return Failed rather
than throwing: an alert is a passenger, same as presence."
```

---

### Task 5: `AlertDispatcher` — wiring the two triggers

**Files:**
- Create: `src/ROROROblox.App/Discord/AlertDispatcher.cs`
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs` (raise triggers from the memory + dropped-out paths)
- Test: `src/ROROROblox.Tests/Discord/AlertDispatcherTests.cs`

**Interfaces:**
- Consumes: `AlertRouter`, `AlertTrigger` (Tasks 1-2), `DiscordWebhookSender` (Task 4), `ITrayService` (App).
- Produces: `AlertDispatcher(DiscordWebhookSender sender, ITrayService tray, Func<DiscordConfig> config, TimeProvider time, ILogger log)` with `Task DispatchAsync(IReadOnlyList<AlertTrigger> triggers)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Discord;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class AlertDispatcherTests
{
    private sealed class SpyTray : ITrayService
    {
        public List<string> Balloons { get; } = [];
        public void ShowBalloon(string title, string body) => Balloons.Add($"{title}|{body}");
        // Remaining ITrayService members: throw NotImplementedException — this suite only
        // exercises the balloon path. Mirror the shape used by MainViewModelTests.FakeTrayService.
    }

    private static AlertTrigger Dropped(Guid id, string name) =>
        new(AlertKind.AccountDroppedOut, id, name, "Pet Simulator 99!", null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task DispatchAsync_LocalDestination_RaisesATrayBalloonAndPostsNothing()
    {
        var handlerCalls = 0;
        var sender = TestSender(() => { handlerCalls++; return WebhookSendResult.Sent; });
        var tray = new SpyTray();
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Local };
        var dispatcher = new AlertDispatcher(sender, tray, () => config, new FakeTimeProvider(), NullLogger.Instance);

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "BaronBloxwell")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("BaronBloxwell", Assert.Single(tray.Balloons), StringComparison.Ordinal);
        Assert.Equal(0, handlerCalls);
    }

    [Fact]
    public async Task DispatchAsync_SameAccountTwice_SecondIsSuppressedByCooldown()
    {
        var tray = new SpyTray();
        var config = new DiscordConfig { DroppedOutDestination = AlertDestination.Local };
        var time = new FakeTimeProvider();
        var dispatcher = new AlertDispatcher(TestSender(() => WebhookSendResult.Sent), tray, () => config, time, NullLogger.Instance);
        var id = Guid.NewGuid();

        await dispatcher.DispatchAsync([Dropped(id, "A")]).WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DispatchAsync([Dropped(id, "A")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(tray.Balloons);
    }

    [Fact]
    public async Task DispatchAsync_WebhookGone_DisablesThatDestinationInsteadOfRetrying()
    {
        var config = new DiscordConfig
        {
            DroppedOutDestination = AlertDestination.Mine,
            MineWebhookUrl = "https://discord.com/api/webhooks/1/tok",
        };
        var dispatcher = new AlertDispatcher(
            TestSender(() => WebhookSendResult.WebhookGone), new SpyTray(), () => config,
            new FakeTimeProvider(), NullLogger.Instance);

        await dispatcher.DispatchAsync([Dropped(Guid.NewGuid(), "A")]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(dispatcher.MineWebhookRejected);
    }
}
```

Build `TestSender` with the `StubHandler` from Task 4's suite (copy it into a shared
`src/ROROROblox.Tests/Discord/StubHttpHandler.cs` when writing this task, and update Task 4's
test to use the shared copy).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~AlertDispatcherTests"`
Expected: FAIL — `AlertDispatcher` not found.

- [ ] **Step 3: Write minimal implementation**

```csharp
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// Turns triggers into delivered alerts: route (mute, coalesce, cooldown), then send.
/// <para>
/// Holds the per-account last-sent map that <see cref="AlertRouter"/> reads, so the router itself
/// stays pure. A destination whose webhook 404s is marked rejected and surfaced in Settings rather
/// than retried — a deleted webhook does not come back.
/// </para>
/// </summary>
public sealed class AlertDispatcher(
    DiscordWebhookSender sender,
    ITrayService tray,
    Func<DiscordConfig> config,
    TimeProvider time,
    ILogger log)
{
    private readonly Dictionary<Guid, DateTimeOffset> _lastSent = [];

    public bool MineWebhookRejected { get; private set; }
    public bool ClanWebhookRejected { get; private set; }

    public async Task DispatchAsync(IReadOnlyList<AlertTrigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        if (triggers.Count == 0) return;

        var current = config();
        var now = time.GetUtcNow();
        var routed = AlertRouter.Route(triggers, current, _lastSent, now);

        foreach (var alert in routed)
        {
            var payload = WebhookPayload.ForAlert(alert.Kind, alert.Triggers);

            switch (alert.Destination)
            {
                case AlertDestination.Local:
                    tray.ShowBalloon(payload.Title, payload.Body);
                    break;
                case AlertDestination.Mine when current.MineWebhookUrl is { } mine:
                    MineWebhookRejected |= await SendAsync(mine, payload).ConfigureAwait(false);
                    break;
                case AlertDestination.Clan when current.ClanWebhookUrl is { } clan:
                    ClanWebhookRejected |= await SendAsync(clan, payload).ConfigureAwait(false);
                    break;
            }

            foreach (var t in alert.Triggers) { _lastSent[t.AccountId] = now; }
        }
    }

    /// <summary>Returns true when the destination should be treated as dead.</summary>
    private async Task<bool> SendAsync(string url, WebhookPayload payload)
    {
        var result = await sender.SendAsync(url, payload).ConfigureAwait(false);
        if (result == WebhookSendResult.WebhookGone)
        {
            log.LogInformation("A Discord webhook returned 404; disabling that destination.");
            return true;
        }
        return false;
    }
}
```

Then in `MainViewModel`, raise triggers where the signals already exist:

- **Dropped out** — in `ApplyPresence`, at the point that already stamps `LastClosedAtUtc` (`wasActive && !summary.IsRunning`), build an `AlertTrigger(AlertKind.AccountDroppedOut, …, summary.RenderName, …)` and hand it to the dispatcher.
- **Memory warning** — in the existing `PressureCrossed` handler, project `MemoryPressureSnapshot.Accounts` entries over their threshold into `AlertKind.MemoryWarning` triggers with `PrivateBytes` populated.

Both use `RenderName`, never `DisplayName`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~AlertDispatcherTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/ src/ROROROblox.Tests/Discord/
git commit -m "feat(discord): alert dispatcher wired to the memory + dropped-out signals

Both triggers come from signals that already exist — the watchdog's PressureCrossed
and the presence-confirmed close — so nothing new has to detect them. The dispatcher
holds the last-sent map so AlertRouter stays pure, and a 404'd webhook is marked
dead and surfaced in Settings rather than retried.

Alert text renders account names through streamer mode, same rule as presence."
```

---

### Task 6: Per-account mute on the row

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/AccountSummary.cs` (`AlertsMuted`)
- Modify: `src/ROROROblox.App/MainWindow.xaml` (mute affordance on the row)
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs` (persist through `DiscordConfigStore`)
- Test: `src/ROROROblox.Tests/Discord/AccountMuteTests.cs`

**Interfaces:**
- Consumes: `DiscordConfig.MutedAccountIds` (presence plan Task 1).
- Produces: `AccountSummary.AlertsMuted` (bool, `INotifyPropertyChanged`).

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class AccountMuteTests
{
    [Fact]
    public async Task MutingARow_PersistsIntoTheDiscordConfig()
    {
        var (vm, row, configStore) = DiscordTestHarness.VmWithConfigStore();

        await vm.SetAlertsMutedAsync(row, muted: true);

        Assert.Contains(row.Id, (await configStore.LoadAsync()).MutedAccountIds);
    }

    [Fact]
    public async Task UnmutingARow_RemovesItFromTheConfig()
    {
        var (vm, row, configStore) = DiscordTestHarness.VmWithConfigStore();
        await vm.SetAlertsMutedAsync(row, muted: true);

        await vm.SetAlertsMutedAsync(row, muted: false);

        Assert.Empty((await configStore.LoadAsync()).MutedAccountIds);
    }

    [Fact]
    public async Task MutingIsIdempotent_NoDuplicateIds()
    {
        var (vm, row, configStore) = DiscordTestHarness.VmWithConfigStore();

        await vm.SetAlertsMutedAsync(row, muted: true);
        await vm.SetAlertsMutedAsync(row, muted: true);

        Assert.Single((await configStore.LoadAsync()).MutedAccountIds);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~AccountMuteTests"`
Expected: FAIL — `SetAlertsMutedAsync` not found.

- [ ] **Step 3: Write minimal implementation**

In `AccountSummary`:

```csharp
    private bool _alertsMuted;

    /// <summary>
    /// Whether Discord alerts for this account are suppressed. The per-account half of the
    /// two-control design: routing is per-trigger, muting is per-account. Persisted in the
    /// Discord config rather than accounts.dat, since it is Discord state, not account state.
    /// </summary>
    public bool AlertsMuted
    {
        get => _alertsMuted;
        set => SetField(ref _alertsMuted, value);
    }
```

In `MainViewModel`:

```csharp
    /// <summary>Mute or unmute Discord alerts for one account, persisting through the config store.</summary>
    internal async Task SetAlertsMutedAsync(AccountSummary summary, bool muted)
    {
        ArgumentNullException.ThrowIfNull(summary);
        summary.AlertsMuted = muted;

        var config = await _discordConfigStore.LoadAsync().ConfigureAwait(true);
        var ids = config.MutedAccountIds.ToHashSet();
        if (muted) { ids.Add(summary.Id); } else { ids.Remove(summary.Id); }
        await _discordConfigStore.SaveAsync(config with { MutedAccountIds = ids.ToList() }).ConfigureAwait(true);
    }
```

Row affordance in `MainWindow.xaml`: a small bell toggle in the row's action group, visible only when any alert destination is configured — no point offering a mute for alerts that are switched off. Follow the `MultiDataTrigger` pattern used by the always-show-Recycle binding.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~AccountMuteTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/ src/ROROROblox.Tests/Discord/AccountMuteTests.cs
git commit -m "feat(discord): per-account alert mute

The per-account half of the two-control design. Lives in the Discord config, not
accounts.dat — it is Discord state, not account state — and the row affordance
only appears once some destination is actually configured."
```

---

### Task 7: Settings — routing, webhook fields, Send test, "do you have a server?"

The accessibility work. Presence already needs no setup; this is the only wall in the feature, and it opens one step earlier than a webhook field.

**Files:**
- Modify: `src/ROROROblox.App/Preferences/PreferencesWindow.xaml` (alerts block)
- Modify: `src/ROROROblox.App/Preferences/PreferencesWindow.xaml.cs`
- Create: `src/ROROROblox.App/Discord/WebhookProbe.cs`
- Test: `src/ROROROblox.Tests/Discord/WebhookProbeTests.cs`

**Interfaces:**
- Consumes: `WebhookUrlValidator` (Task 3), `DiscordWebhookSender` (Task 4).
- Produces: `WebhookProbe(HttpClient client)` with `Task<WebhookIdentity?> DescribeAsync(string url, CancellationToken ct = default)`, `WebhookIdentity(string ChannelName, string GuildName)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using ROROROblox.App.Discord;

namespace ROROROblox.Tests.Discord;

public class WebhookProbeTests
{
    [Fact]
    public async Task DescribeAsync_ValidWebhook_ReturnsTheChannelAndServerNames()
    {
        // So a clan webhook pasted into the personal slot is visible BEFORE it matters,
        // not after the first alert lands in the wrong channel.
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"name":"rororo","channel_id":"1","guild_id":"2","source_channel":{"name":"rororo-alerts"},"source_guild":{"name":"Este's Server"}}"""),
        });

        var identity = await new WebhookProbe(new HttpClient(handler))
            .DescribeAsync("https://discord.com/api/webhooks/1/tok").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("rororo-alerts", identity!.ChannelName);
        Assert.Equal("Este's Server", identity.GuildName);
    }

    [Fact]
    public async Task DescribeAsync_DeletedWebhook_ReturnsNullRatherThanThrowing()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await new WebhookProbe(new HttpClient(handler))
            .DescribeAsync("https://discord.com/api/webhooks/1/tok").WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task DescribeAsync_NetworkFailure_ReturnsNull()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("offline"));

        Assert.Null(await new WebhookProbe(new HttpClient(handler))
            .DescribeAsync("https://discord.com/api/webhooks/1/tok").WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~WebhookProbeTests"`
Expected: FAIL — `WebhookProbe` not found.

- [ ] **Step 3: Write the probe**

```csharp
using System.Text.Json;

namespace ROROROblox.App.Discord;

/// <summary>Where a webhook actually posts.</summary>
public sealed record WebhookIdentity(string ChannelName, string GuildName);

/// <summary>
/// GETs a webhook to find out which channel and server it belongs to, so Settings can show
/// "Posts to #rororo-alerts in Este's Server" before the first alert is sent. Catching a clan
/// webhook pasted into the personal slot at setup time is the whole point.
/// </summary>
public sealed class WebhookProbe(HttpClient client)
{
    public async Task<WebhookIdentity?> DescribeAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            var channel = doc.RootElement.TryGetProperty("source_channel", out var c) && c.TryGetProperty("name", out var cn)
                ? cn.GetString() : null;
            var guild = doc.RootElement.TryGetProperty("source_guild", out var g) && g.TryGetProperty("name", out var gn)
                ? gn.GetString() : null;

            return channel is null && guild is null ? null : new WebhookIdentity(channel ?? "unknown", guild ?? "your server");
        }
        catch
        {
            return null;   // setup help is best-effort; never block the user on it
        }
    }
}
```

- [ ] **Step 4: Build the Settings block**

Add below the presence block from the presence plan's Task 9:

```xml
<Border Background="{DynamicResource RowBgBrush}" CornerRadius="8" Padding="14" Margin="0,0,0,10">
    <StackPanel>
        <TextBlock Text="Alerts" FontSize="13" FontWeight="SemiBold"
                   Foreground="{DynamicResource WhiteBrush}" Margin="0,0,0,6" />
        <TextBlock FontSize="11" Foreground="{DynamicResource MutedTextBrush}" TextWrapping="Wrap"
                   Margin="0,0,0,10"
                   Text="Tell me when something happens to an account while I'm not watching. Desktop alerts work right away. Phone alerts need a Discord webhook — there's a walkthrough below if you've never made one." />

        <Grid Margin="0,0,0,6">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="An account drops out" FontSize="12" VerticalAlignment="Center"
                       Foreground="{DynamicResource WhiteBrush}" Width="180" />
            <ComboBox Grid.Column="1" x:Name="DroppedOutDestination" Width="160" HorizontalAlignment="Left"
                      SelectionChanged="OnAlertRoutingChanged">
                <ComboBoxItem Content="Off" Tag="None" />
                <ComboBoxItem Content="Desktop only" Tag="Local" />
                <ComboBoxItem Content="My channel" Tag="Mine" />
                <ComboBoxItem Content="Clan channel" Tag="Clan" />
            </ComboBox>
        </Grid>
        <!-- Memory-warning row: identical Grid, x:Name="MemoryWarningDestination". -->

        <TextBlock x:Name="WebhookSetupPrompt" FontSize="11" TextWrapping="Wrap" Margin="0,10,0,4"
                   Foreground="{DynamicResource CyanBrush}"
                   Text="Sending to a channel? Paste a webhook URL below." />
        <TextBox x:Name="MineWebhookInput" Padding="8,6" FontSize="11"
                 Background="{DynamicResource NavyBrush}" Foreground="{DynamicResource WhiteBrush}"
                 BorderBrush="{DynamicResource DividerBrush}" BorderThickness="1"
                 TextChanged="OnWebhookTextChanged" />
        <TextBlock x:Name="MineWebhookVerdict" FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0"
                   Foreground="{DynamicResource MutedTextBrush}" />
        <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
            <Button x:Name="SendTestButton" Content="Send test" Padding="12,5" Click="OnSendTestClick"
                    Background="{DynamicResource MagentaBrush}" Foreground="{DynamicResource WhiteBrush}"
                    BorderThickness="0" FontSize="11" FontWeight="SemiBold" />
            <Button x:Name="NoServerHelpButton" Content="I don't have a Discord server" Padding="12,5" Margin="8,0,0,0"
                    Background="{DynamicResource NavyBrush}" Foreground="{DynamicResource WhiteBrush}"
                    BorderBrush="{DynamicResource DividerBrush}" BorderThickness="1" FontSize="11"
                    Click="OnNoServerHelpClick" />
        </StackPanel>
        <TextBlock x:Name="AlertsStatusLine" FontSize="11" Margin="0,10,0,0" TextWrapping="Wrap"
                   Foreground="{DynamicResource CyanBrush}" />
    </StackPanel>
</Border>
```

Handlers:

- `OnWebhookTextChanged` → `WebhookUrlValidator.Inspect`; show `Message` for anything not `Valid`; on `Valid`, call `WebhookProbe.DescribeAsync` and set the verdict line to **"Posts to #{channel} in {server}"**.
- `OnSendTestClick` → send a real `WebhookPayload("RoRoRo test", "If you can read this, alerts work.")` through the real sender; report sent / gone / rate-limited / failed in `AlertsStatusLine`.
- `OnNoServerHelpClick` → a modal with the three-click path: **`+` in Discord's left sidebar → Create My Own → Skip the questions**, then Server Settings → Integrations → Webhooks → New Webhook → Copy Webhook URL.
- `AlertsStatusLine` states the honest situation: *"No alerts routed anywhere yet."* when every destination is `None`; *"Desktop alerts on. No webhook, so nothing reaches your phone."*; *"Sending to #rororo-alerts."*

- [ ] **Step 5: Run tests and build**

Run: `dotnet test ROROROblox.slnx`
Expected: all pass, including the 3 probe tests.

- [ ] **Step 6: Manual smoke**

Paste in order and confirm each response: an invite link, a channel link, a bot-token-shaped string, a real webhook (expect the channel/server line), then Send test and confirm the message arrives in Discord. Then set a destination to "My channel" with the field empty and confirm the status line says nothing reaches your phone.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/ src/ROROROblox.Tests/Discord/WebhookProbeTests.cs
git commit -m "feat(discord): alert routing UI, webhook validation, and a setup path for people without a server

The only wall in this feature is the webhook, and it starts one step earlier than
a paste field: 'Server Settings -> Integrations' assumes you own a server, and
plenty of clan members have only ever joined one. So there is a three-click guide
to making one, and desktop alerts work with no setup at all for everyone who never
walks that way.

The field names what you actually pasted, a valid webhook reports which channel it
posts to before the first alert lands there, and Send test uses the real path so
'it says connected but nothing arrives' surfaces during setup instead of at 3am."
```

---

### Task 8: Reviewer-letter disclosure for the next submission

**Files:**
- Create: `docs/store/discord-disclosure.md` (a fragment the next reviewer letter folds in)

**Interfaces:** none — documentation.

- [ ] **Step 1: Write the disclosure fragment**

Covering, in the letter's established voice: the third-party library (`Lachee.DiscordRichPresence`, MIT) now in the signed package; the local named-pipe IPC to the user's own Discord client; the new outbound host (`discord.com`) for user-configured webhooks, off by default; and the `roblox-rororo:` URI-scheme registration for inbound joins. Frame it against the existing plugin host — local IPC the user opts into — plus an outbound call to an address the user typed in themselves. State plainly that no new package capabilities are required: the named pipe is covered by `runFullTrust` and outbound HTTPS needs no declaration.

- [ ] **Step 2: Commit**

```bash
git add docs/store/discord-disclosure.md
git commit -m "docs(store): reviewer disclosure fragment for the Discord integration

No new package capabilities — the named pipe is covered by runFullTrust and
outbound HTTPS needs no declaration — but a new third-party library, a new local
IPC channel, a new outbound host, and a URI-scheme registration all belong in the
next letter rather than being discovered in a diff."
```

---

## Self-review

**Spec coverage:** §5.3 triggers and two-control routing → Tasks 1, 2, 6. §5.4 setup UX → Tasks 3, 7. §5.5 defaults off → Tasks 1, 2 (`AlertDestination.None` default, asserted). §6 alert data flow → Task 5. §7.1 streamer mode outbound → Task 5 (`RenderName`). §7.2 no private link in webhooks → Task 1 (type-level, reflection-asserted). §8 error handling → Tasks 4, 5, 7. §9 Store disclosure → Task 8.

**Types:** `AlertKind`/`AlertTrigger`/`WebhookPayload` defined Task 1, consumed Tasks 2, 4, 5. `RoutedAlert`/`AlertRouter.Cooldown` defined Task 2, consumed Task 5. `WebhookUrlKind`/`WebhookUrlVerdict` defined Task 3, consumed Task 7. `WebhookSendResult` defined Task 4, consumed Task 5. `WebhookIdentity` defined Task 7. `StubHttpHandler` is introduced in Task 4 and extracted to a shared file in Task 5 — that extraction is called out in Task 5's step 1 so the two suites do not each define one.

**Cross-plan dependency:** `DiscordConfig`, `DiscordConfigStore`, and `AlertDestination` come from the presence plan's Task 1. This plan cannot start before that task lands.
