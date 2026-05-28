using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

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
    public void TypedHttpClient_Resolves_WithExactlyOneApplicableCtor(Type serviceType)
    {
        var services = new ServiceCollection();
        services.AddLogging(); // registers ILogger<T> — the second-applicable-ctor trigger
        services.AddHttpClient<IRobloxApi, RobloxApi>();
        services.AddHttpClient<IRobloxCompatChecker, RobloxCompatChecker>();
        services.AddHttpClient<IRobloxUpdateProbe, RobloxUpdateProbe>();
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService(serviceType);

        Assert.NotNull(resolved);
    }
}
