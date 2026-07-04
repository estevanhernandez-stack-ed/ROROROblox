using System.Net.Http;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Fetches + parses the remote marketplace catalog. Only ever called when RoRoRo is unpackaged (the
/// caller gates on <see cref="Distribution.IDistributionMode"/>). Any failure — offline, non-200,
/// malformed JSON — resolves to an EMPTY list, never an exception: the marketplace simply shows no
/// Available section. Mirrors the remote-config fetch shape of <c>RobloxCompatChecker</c>.
/// </summary>
internal sealed class PluginCatalogClient
{
    private readonly Func<CancellationToken, Task<string>> _fetch;

    // Test seam: inject the raw-JSON fetch directly.
    public PluginCatalogClient(Func<CancellationToken, Task<string>> fetch)
    {
        _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
    }

    // Production: GET the catalog URL and read the body as a string.
    public PluginCatalogClient(HttpClient http, string catalogUrl)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogUrl);
        _fetch = async ct =>
        {
            using var response = await http.GetAsync(catalogUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        };
    }

    public async Task<IReadOnlyList<PluginCatalogEntry>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _fetch(ct).ConfigureAwait(false);
            return PluginCatalogParser.Parse(json);
        }
        catch (Exception)
        {
            // Offline / non-200 / cancelled → no catalog. Never blocks the Plugins window.
            return [];
        }
    }
}
