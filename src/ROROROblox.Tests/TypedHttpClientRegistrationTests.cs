using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;
using ROROROblox.App.Discord;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests;

/// <summary>
/// Production-wiring guard for every <c>AddHttpClient&lt;I,T&gt;</c> typed client. The typed-client
/// activator requires EXACTLY ONE applicable constructor for <c>[HttpClient]</c>; a second
/// applicable ctor (e.g. a <c>(HttpClient, ILogger&lt;T&gt;)</c> overload, since AddLogging registers
/// <c>ILogger&lt;T&gt;</c>) throws "Multiple constructors ... one applicable constructor" at RESOLVE
/// time — invisible to direct-construction unit tests and to a clean build, caught only when the
/// app actually starts. Two such bugs (RobloxCompatChecker, RobloxUpdateProbe) reached a 612-green
/// branch and crashed startup. This test exercises the real DI resolution path so they can't again.
/// </summary>
public class TypedHttpClientRegistrationTests
{
    [Theory]
    [InlineData(typeof(IRobloxApi))]
    [InlineData(typeof(IAccountFactsApi))]
    [InlineData(typeof(IRobloxCompatChecker))]
    [InlineData(typeof(IRobloxUpdateProbe))]
    [InlineData(typeof(DiscordWebhookSender))]
    [InlineData(typeof(WebhookProbe))]
    [InlineData(typeof(ROROROblox.App.Notify.PushoverSender))]
    [InlineData(typeof(ROROROblox.App.Notify.NtfySender))]
    [InlineData(typeof(ROROROblox.Core.KnownIssues.IKnownIssuesFeed))]
    public void TypedHttpClient_Resolves_WithExactlyOneApplicableCtor(Type serviceType)
    {
        var services = new ServiceCollection();
        services.AddLogging(); // registers ILogger<T> — the second-applicable-ctor trigger
        services.AddHttpClient<IRobloxApi, RobloxApi>();
        services.AddHttpClient<IAccountFactsApi, AccountFactsApi>();
        services.AddHttpClient<IRobloxCompatChecker, RobloxCompatChecker>();
        services.AddHttpClient<IRobloxUpdateProbe, RobloxUpdateProbe>();
        services.AddHttpClient<DiscordWebhookSender>();
        services.AddHttpClient<WebhookProbe>();
        services.AddHttpClient<ROROROblox.App.Notify.PushoverSender>();
        services.AddHttpClient<ROROROblox.App.Notify.NtfySender>();
        services.AddHttpClient<ROROROblox.Core.KnownIssues.IKnownIssuesFeed, ROROROblox.Core.KnownIssues.KnownIssuesFeed>();
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService(serviceType);

        Assert.NotNull(resolved);
    }

    /// <summary>
    /// <c>UseCookies = false</c> is the line CLAUDE.md calls load-bearing and used to say no test
    /// guards. With a cookie container, the first request's <c>.ROBLOSECURITY</c> is kept and every
    /// later request, for every other account, rides it. v1.36 adds a second client that attaches a
    /// cookie per request, so there are now two places to get it wrong and this reads both.
    /// <para>
    /// Read from the REAL <c>App.ConfigureServices</c> graph: the primary handler is found by
    /// walking the factory's handler chain down to the innermost handler, so a refactor that moves
    /// either client onto a shared or default handler fails here, not in production as "every alt
    /// opens as the first user".
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(typeof(IRobloxApi))]
    [InlineData(typeof(IAccountFactsApi))]
    public void CookieCarryingClients_PrimaryHandler_HasUseCookiesFalse(Type clientInterface)
    {
        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        HttpMessageHandler handler = factory.CreateHandler(clientInterface.Name);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler
                ?? throw new InvalidOperationException("A delegating handler in the chain has no inner handler.");
        }

        var primary = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(primary.UseCookies,
            $"{clientInterface.Name}'s primary handler has UseCookies=true: the first account's cookie would be reused for every account.");
    }

    /// <summary>
    /// F1 (dpapi-cookie-blast-radius): both cookie-carrying clients attach the cookie per request, so a
    /// followed 3xx would re-send it to wherever the redirect points. Each must see the 3xx itself. The
    /// facts client closed it in v1.36 item 3; RobloxApi followed on 2026-11-02, once reading it showed
    /// every cookie-carrying call goes to a fixed roblox.com API endpoint and share links resolve by an
    /// API POST, not a redirect chase.
    /// </summary>
    [Theory]
    [InlineData(typeof(IRobloxApi))]
    [InlineData(typeof(IAccountFactsApi))]
    public void CookieCarryingClients_PrimaryHandler_DoNotFollowRedirects(Type clientInterface)
    {
        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        using var provider = services.BuildServiceProvider();

        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientInterface.Name);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler ?? throw new InvalidOperationException("A delegating handler has no inner handler.");
        }

        var primary = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(primary.AllowAutoRedirect, $"{clientInterface.Name} follows redirects: the Cookie header would travel with them.");
    }

    [Theory]
    [InlineData(typeof(IRobloxApi))]
    [InlineData(typeof(IAccountFactsApi))]
    public void CookieCarryingClients_SendTheRororoUserAgent(Type clientInterface)
    {
        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientInterface.Name);

        var product = Assert.Single(client.DefaultRequestHeaders.UserAgent);
        Assert.Equal("RORORO", product.Product?.Name);
        Assert.False(string.IsNullOrWhiteSpace(product.Product?.Version));
    }

    [Fact]
    public void AccountFactsApi_HasExactlyOnePublicConstructor()
    {
        var ctors = typeof(AccountFactsApi).GetConstructors();
        var ctor = Assert.Single(ctors);
        Assert.Equal(new[] { typeof(HttpClient), typeof(ILogger<AccountFactsApi>) }, ctor.GetParameters().Select(p => p.ParameterType));
    }

    /// <summary>
    /// The other half of the same failure, and the one that actually shipped to a smoke build:
    /// <c>DiscordWebhookSender</c> took a NON-GENERIC <c>ILogger</c>. DI registers only
    /// <c>ILogger&lt;T&gt;</c>, so it threw "Unable to resolve service for type ...ILogger" at
    /// resolve time — clean build, 1247 green tests, and Settings would not open, because the
    /// failure cascaded into the composition-root method that also wired the Preferences factory.
    /// <para>
    /// Asserted through the real graph rather than by reading the constructor: the point is that
    /// the container can build it, not that the signature looks right.
    /// </para>
    /// </summary>
    [Fact]
    public void AlertDispatcher_ResolvesThroughTheRealGraph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient<DiscordWebhookSender>();
        services.AddSingleton<ITrayService, NoOpTrayService>();
        services.AddSingleton(_ => new DiscordConfigService(
            new DiscordConfigStore(Path.Combine(Path.GetTempPath(), $"rororo-reg-test-{Guid.NewGuid():N}.dat"))));
        services.AddSingleton(sp => new AlertDispatcher(
            sp.GetRequiredService<DiscordWebhookSender>(),
            sp.GetRequiredService<ITrayService>(),
            () => sp.GetRequiredService<DiscordConfigService>().Current,
            TimeProvider.System,
            sp.GetRequiredService<ILogger<AlertDispatcher>>()));
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<AlertDispatcher>());
    }

    private sealed class NoOpTrayService : ITrayService
    {
        public void Show() { }
        public void UpdateStatus(MultiInstanceState state) { }
        public void ShowToast(string title, string message, Guid? accountId = null) { }
        public void SetMemoryWarning(bool active) { }
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
}
