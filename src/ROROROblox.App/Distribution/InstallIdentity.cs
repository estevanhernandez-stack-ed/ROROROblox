using ROROROblox.App.Plugins;
using ROROROblox.Core.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace ROROROblox.App.Distribution;

/// <summary>
/// #52: one answer to "which version, which channel" for Diagnostics. The version is the About
/// page's helper (<see cref="AppVersion.Display"/> with the real distribution probe), so the two
/// surfaces cannot disagree. The channel composes <see cref="IHostBuildIdentity.InstallFlavour"/>
/// (store / sideload / dev, from the package signature) with Velopack's <c>IsInstalled</c>, which
/// splits the unpackaged case into a direct download and a build run from source. The plugin
/// contract's <c>InstallFlavour</c> is untouched.
/// </summary>
internal sealed class InstallIdentity : IInstallIdentity
{
    private const string GitHubRepoUrl = "https://github.com/estevanhernandez-stack-ed/ROROROblox";

    public InstallIdentity(IDistributionMode mode, IHostBuildIdentity buildIdentity)
        : this(() => AppVersion.Display(mode), buildIdentity.InstallFlavour, ProbeVelopackInstalled)
    {
    }

    /// <param name="version">Produces the display version; injected so tests need no packaging.</param>
    /// <param name="installFlavour">An <see cref="HostBuildIdentity"/> flavour string.</param>
    /// <param name="velopackInstalled">Whether Velopack manages this install. Only read when unpackaged.</param>
    public InstallIdentity(Func<string> version, string installFlavour, Func<bool> velopackInstalled)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(velopackInstalled);
        Version = version();
        Channel = Resolve(installFlavour, velopackInstalled);
    }

    public string Version { get; }

    public InstallChannel Channel { get; }

    internal static InstallChannel Resolve(string? installFlavour, Func<bool> velopackInstalled)
    {
        if (string.Equals(installFlavour, HostBuildIdentity.Store, StringComparison.OrdinalIgnoreCase))
        {
            return InstallChannel.Store;
        }

        if (string.Equals(installFlavour, HostBuildIdentity.Sideload, StringComparison.OrdinalIgnoreCase))
        {
            return InstallChannel.Sideload;
        }

        bool installed;
        try
        {
            installed = velopackInstalled();
        }
        catch
        {
            installed = false;
        }

        return installed ? InstallChannel.DirectDownload : InstallChannel.Dev;
    }

    private static bool ProbeVelopackInstalled()
        => new UpdateManager(new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false)).IsInstalled;
}
