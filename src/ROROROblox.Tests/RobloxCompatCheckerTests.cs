using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Coverage of <see cref="RobloxCompatChecker.ResolveMutexNameAsync"/> — the config-driven
/// singleton-mutex-name resolver (spec item #1). Three-tier fallback: valid remote config ->
/// last-known-good cache -> hardcoded <see cref="MutexHolder.DefaultMutexName"/>. Degrade-safe:
/// ANY failure resolves to a usable name and NEVER throws, so a broken roblox-compat.json can
/// never brick multi-instance. HTTP is stubbed via <see cref="StubHttpHandler"/>; the
/// last-known-good cache is an injected seam (no disk).
/// </summary>
public class RobloxCompatCheckerTests
{
    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // Builds valid roblox-compat.json (camelCase, backslashes escaped) via the serializer.
    private static string ConfigJson(string mutexName) =>
        JsonSerializer.Serialize(new
        {
            knownGoodVersionMin = "2.0.0.0",
            knownGoodVersionMax = "2.999.0.0",
            mutexName,
            generatedAt = "2026-05-28T00:00:00Z",
        });

    private static RobloxCompatChecker Checker(
        StubHttpHandler stub,
        Func<string?>? readLkg = null,
        Action<string>? writeLkg = null) =>
        new(new HttpClient(stub), readLkg ?? (() => null), writeLkg ?? (_ => { }));

    [Fact]
    public async Task ResolveMutexNameAsync_ReturnsRemoteConfig_WhenConfigNameValid()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, ConfigJson(@"Local\ROBLOX_singletonEvent")));
        var checker = Checker(stub);

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_singletonEvent", name);
        Assert.Equal(MutexNameSource.RemoteConfig, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_PersistsValidRemoteNameToLastKnownGood()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, ConfigJson(@"Local\ROBLOX_renamed_2027")));
        string? persisted = null;
        var checker = Checker(stub, writeLkg: v => persisted = v);

        await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_renamed_2027", persisted);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_FallsBackToLastKnownGood_WhenFetchFails()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(_ => throw new HttpRequestException("offline"));
        var checker = Checker(stub, readLkg: () => @"Local\ROBLOX_renamed_2027");

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_renamed_2027", name);
        Assert.Equal(MutexNameSource.LastKnownGood, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_PrefersLastKnownGood_WhenRemoteNameInvalid()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, ConfigJson("")));   // empty -> invalid remote name
        var checker = Checker(stub, readLkg: () => @"Local\ROBLOX_renamed_2027");

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_renamed_2027", name);
        Assert.Equal(MutexNameSource.LastKnownGood, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_FallsBackToDefault_WhenFetchFailsAndNoCache()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var checker = Checker(stub, readLkg: () => null);

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(MutexHolder.DefaultMutexName, name);
        Assert.Equal(MutexNameSource.Default, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_FallsBackToDefault_WhenCacheIsAlsoInvalid()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(_ => throw new HttpRequestException("offline"));
        var checker = Checker(stub, readLkg: () => "   ");   // garbage cache

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(MutexHolder.DefaultMutexName, name);
        Assert.Equal(MutexNameSource.Default, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_DoesNotPersist_WhenRemoteNameInvalid()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, ConfigJson("")));
        var persisted = false;
        var checker = Checker(stub, readLkg: () => null, writeLkg: _ => persisted = true);

        await checker.ResolveMutexNameAsync();

        Assert.False(persisted);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_NeverThrows_WhenPersistThrows()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, ConfigJson(@"Local\ROBLOX_ok")));
        var checker = Checker(stub, writeLkg: _ => throw new IOException("disk full"));

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_ok", name);
        Assert.Equal(MutexNameSource.RemoteConfig, source);   // persist failure is swallowed
    }

    [Fact]
    public async Task ResolveMutexNameAsync_NeverThrows_WhenCacheReadThrows()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(_ => throw new HttpRequestException("offline"));
        var checker = Checker(stub, readLkg: () => throw new IOException("disk read failed"));

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(MutexHolder.DefaultMutexName, name);
        Assert.Equal(MutexNameSource.Default, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_HitsCompatConfigEndpoint_WithGet()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, ConfigJson(@"Local\ROBLOX_singletonEvent")));
        var checker = Checker(stub);

        await checker.ResolveMutexNameAsync();

        var req = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Contains("roblox-compat.json", req.RequestUri?.ToString());
    }

    [Fact]
    public void ResolvesViaTypedHttpClientFactory_WithExactlyOneApplicableCtor()
    {
        // Regression (smoke-caught): a SECOND public ctor made the AddHttpClient<I,T> typed-client
        // activator throw "Multiple constructors ... There should only be one applicable constructor"
        // at App startup. Direct-construction unit tests and a clean build both missed it — only the
        // real typed-client resolution path exercises ctor selection.
        var services = new ServiceCollection();
        services.AddHttpClient<IRobloxCompatChecker, RobloxCompatChecker>();
        using var provider = services.BuildServiceProvider();

        var checker = provider.GetRequiredService<IRobloxCompatChecker>();

        Assert.NotNull(checker);
    }
}
