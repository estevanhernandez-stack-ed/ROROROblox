using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using ROROROblox.Core;

namespace ROROROblox.App;

/// <summary>
/// Registration of the two cookie-carrying typed clients, lifted out of <c>App.ConfigureServices</c>
/// so a test can build the exact registration the app uses and read each client's primary handler
/// (<c>TypedHttpClientRegistrationTests</c>). Nothing else about the registration changed.
/// <para>
/// <c>UseCookies = false</c> is load-bearing for BOTH clients and is written out twice on purpose:
/// the rule is easier to see duplicated than hidden behind a shared handler factory. Both attach
/// <c>.ROBLOSECURITY</c> per request as a header. With the default cookie container enabled the
/// first request's account cookie lands in the container and every later request, for every other
/// account, rides it: every alt launches as, or fetches facts for, the first user.
/// </para>
/// </summary>
internal static class RobloxHttpClients
{
    internal static IServiceCollection AddRobloxHttpClients(this IServiceCollection services)
    {
        // RobloxApi over a managed HttpClient (factory handles lifetime + DNS rotation).
        // UseCookies=false is load-bearing: RobloxApi sets the .ROBLOSECURITY cookie manually
        // per-request via request.Headers.Add("Cookie", ...). With the default cookie container
        // enabled, the first auth-ticket request leaks its account's cookie into the container
        // and every subsequent Launch As routes through that same account — every alt opens as
        // the first user. This is the actual root cause of the multi-instance bug v1.2 saw.
        services.AddHttpClient<IRobloxApi, RobloxApi>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Clear();
            var version = Distribution.AppVersion.Display(new Distribution.Win32DistributionMode());
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseCookies = false,
        });

        // Account facts (Robux, friends online, membership): its own typed client, its own handler,
        // the same load-bearing UseCookies line as above, plus AllowAutoRedirect = false: this client
        // only ever expects a direct answer, and a redirect would re-send the Cookie header to
        // wherever the 3xx points. A 3xx surfaces as AccountFactsRequestException(3xx) -> LastError.
        // RobloxApi's handler is deliberately unchanged (share-link resolution may rely on redirects).
        // Deliberately NOT a shared factory.
        services.AddHttpClient<IAccountFactsApi, AccountFactsApi>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Clear();
            var version = Distribution.AppVersion.Display(new Distribution.Win32DistributionMode());
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
        });

        return services;
    }
}
