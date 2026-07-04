namespace ROROROblox.App.Distribution;

/// <summary>
/// Whether RoRoRo is running as an MSIX-packaged app (Microsoft Store or self-signed sideload) or
/// unpackaged (Velopack direct download, <c>dotnet run</c>, F5). The plugin marketplace is active
/// only when unpackaged — this keeps the Store-listed binary inside policy 10.2.2, which forbids
/// reading a curated plugin list from a server. The gate is a property of how the binary is running,
/// not a build-time flag, so it cannot silently regress across releases.
/// </summary>
internal interface IDistributionMode
{
    bool IsPackaged { get; }
}
