using System.Reflection;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Which binary is this host? Surfaced through <c>GetHostInfo</c> (contract 0.12.0) so an agent
/// driving the MCP connector can tell a Store MSIX, a sideload MSIX and a dev build apart. Before
/// this, all three reported the same version string and a false green slipped through on
/// 2026-08-22 because of it.
/// </summary>
public interface IHostBuildIdentity
{
    /// <summary>"store", "sideload" or "dev".</summary>
    string InstallFlavour { get; }

    /// <summary>Full path of the running executable, or empty when the OS cannot say.</summary>
    string ProcessPath { get; }

    /// <summary>
    /// <c>AssemblyInformationalVersionAttribute</c> of the App assembly: the version plus, when the
    /// build carried one, a <c>+commit</c> suffix. Falls back to the assembly version.
    /// </summary>
    string InformationalVersion { get; }
}

/// <summary>
/// Derives the flavour from the package signature, because nothing in the package identity can:
/// <c>finalize-store-build.ps1</c> and <c>build-msix.ps1 -Sideload</c> ship the same manifest
/// <c>Identity</c> (Name <c>626LabsLLC.RoRoRoBlox</c>, the Store publisher CN; the sideload cert
/// <c>dev-cert.pfx</c> carries that CN too), so family name and publisher are identical. What
/// differs is who signed it: the Store re-signs, the sideload is signed with the developer cert.
/// <c>Package.SignatureKind</c> reports exactly that (<c>Store</c> vs <c>Developer</c>).
/// </summary>
public sealed class HostBuildIdentity : IHostBuildIdentity
{
    public const string Store = "store";
    public const string Sideload = "sideload";
    public const string Dev = "dev";

    /// <summary>
    /// Production wiring: real package probe, <see cref="Environment.ProcessPath"/>, and the App
    /// assembly's informational version.
    /// </summary>
    public HostBuildIdentity()
        : this(ProbePackageSignatureKind, Environment.ProcessPath, ReadInformationalVersion(typeof(HostBuildIdentity).Assembly))
    {
    }

    /// <param name="signatureKindProbe">
    /// Returns the package signature kind by name (<c>"Store"</c>, <c>"Developer"</c>, ...), or null
    /// when the process has no package identity. Injected so the branch is testable unpackaged.
    /// </param>
    public HostBuildIdentity(Func<string?> signatureKindProbe, string? processPath, string informationalVersion)
    {
        ArgumentNullException.ThrowIfNull(signatureKindProbe);
        string? kind;
        try
        {
            kind = signatureKindProbe();
        }
        catch
        {
            kind = null;
        }

        InstallFlavour = kind is null
            ? Dev
            : string.Equals(kind, "Store", StringComparison.OrdinalIgnoreCase) ? Store : Sideload;
        ProcessPath = processPath ?? string.Empty;
        InformationalVersion = informationalVersion ?? string.Empty;
    }

    public string InstallFlavour { get; }

    public string ProcessPath { get; }

    public string InformationalVersion { get; }

    internal static string ReadInformationalVersion(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return !string.IsNullOrWhiteSpace(info) ? info : assembly.GetName().Version?.ToString() ?? string.Empty;
    }

    private static string? ProbePackageSignatureKind()
    {
        // Same no-package test the marketplace gate uses; only then touch Package.Current, which
        // throws for an unpackaged process.
        if (!new Distribution.Win32DistributionMode().IsPackaged)
        {
            return null;
        }

        return Windows.ApplicationModel.Package.Current.SignatureKind.ToString();
    }
}
