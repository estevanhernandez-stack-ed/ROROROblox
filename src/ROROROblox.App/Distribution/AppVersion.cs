using Windows.ApplicationModel;

namespace ROROROblox.App.Distribution;

/// <summary>
/// The version RoRoRo shows and reports (About page, log banner, <c>RORORO/&lt;version&gt;</c>
/// User-Agent). A packaged build reads the MSIX package identity, because the package version can
/// be bumped in Partner Center without a rebuild and the assembly version would then lie. An
/// unpackaged build reads the assembly. Always <c>major.minor.build</c>, as before.
/// </summary>
internal static class AppVersion
{
    /// <summary>The fallback shown when no version can be read at all.</summary>
    internal const string Unknown = "0.0.0";

    /// <summary>
    /// Display version for the running process. Packaged: the package identity's version, falling
    /// back to the assembly on any failure. Unpackaged: the assembly.
    /// </summary>
    internal static string Display(IDistributionMode mode)
    {
        if (mode.IsPackaged)
        {
            try
            {
                return Format(Package.Current.Id.Version);
            }
            catch
            {
                // No package identity after all, or the WinRT call failed: the assembly is the
                // honest fallback, not a crash on the startup path.
            }
        }

        return FromAssembly();
    }

    /// <summary>Assembly version of the App, three components.</summary>
    internal static string FromAssembly() =>
        Format(typeof(AppVersion).Assembly.GetName().Version);

    internal static string Format(PackageVersion version) =>
        $"{version.Major}.{version.Minor}.{version.Build}";

    internal static string Format(Version? version) =>
        version?.ToString(3) ?? Unknown;
}
