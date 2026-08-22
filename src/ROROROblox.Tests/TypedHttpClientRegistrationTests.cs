using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    [InlineData(typeof(IRobloxCompatChecker))]
    [InlineData(typeof(IRobloxUpdateProbe))]
    [InlineData(typeof(DiscordWebhookSender))]
    [InlineData(typeof(WebhookProbe))]
    public void TypedHttpClient_Resolves_WithExactlyOneApplicableCtor(Type serviceType)
    {
        var services = new ServiceCollection();
        services.AddLogging(); // registers ILogger<T> — the second-applicable-ctor trigger
        services.AddHttpClient<IRobloxApi, RobloxApi>();
        services.AddHttpClient<IRobloxCompatChecker, RobloxCompatChecker>();
        services.AddHttpClient<IRobloxUpdateProbe, RobloxUpdateProbe>();
        services.AddHttpClient<DiscordWebhookSender>();
        services.AddHttpClient<WebhookProbe>();
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService(serviceType);

        Assert.NotNull(resolved);
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
        public void ShowToast(string title, string message) { }
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
}
