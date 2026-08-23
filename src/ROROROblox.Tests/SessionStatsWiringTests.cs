using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROROROblox.App;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The registration swap is the whole of the feature's wiring: nothing calls the stats store
/// directly, so if <see cref="ISessionHistoryStore"/> stops resolving to the decorator, stats
/// silently stop accruing and every number on the page quietly freezes.
///
/// <para>That failure has no symptom until someone notices a total that has not moved in a week,
/// which is exactly the kind of thing a wiring test is for. Same family as
/// <c>TypedHttpClientRegistrationTests</c>: unit-green is not evidence about the composition root.</para>
/// </summary>
public class SessionStatsWiringTests : IDisposable
{
    // NOT disposed until the test is: the container resolves ILogger<T> lazily, so a factory
    // disposed at the end of BuildContainer throws ObjectDisposedException on first resolve.
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(_ => { });

    public void Dispose() => _loggerFactory.Dispose();

    private ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();
        ROROROblox.App.App.ConfigureServices(services, _loggerFactory);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void TheHistoryStoreResolvesToTheStatsRecordingDecorator()
    {
        using var sp = BuildContainer();

        var store = sp.GetRequiredService<ISessionHistoryStore>();

        Assert.IsType<StatsRecordingSessionHistoryStore>(store);
    }

    [Fact]
    public void TheStatsStoreResolves()
    {
        using var sp = BuildContainer();

        Assert.NotNull(sp.GetRequiredService<ISessionStatsStore>());
    }

    [Fact]
    public void TheStatsStoreIsASingletonSoConcurrencyAndHistoryShareOneFile()
    {
        // Two instances would mean two read-modify-write cycles racing over one path, and the
        // concurrency hook resolves its own reference separately from the decorator's.
        using var sp = BuildContainer();

        Assert.Same(sp.GetRequiredService<ISessionStatsStore>(), sp.GetRequiredService<ISessionStatsStore>());
    }
}
