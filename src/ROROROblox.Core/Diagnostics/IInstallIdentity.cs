namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Which channel this copy of RoRoRo was installed from. Store and Sideload are MSIX (told apart
/// by who signed the package); DirectDownload is the Velopack Setup.exe; Dev is anything else
/// unpackaged (a build run from source).
/// </summary>
public enum InstallChannel
{
    Store,
    Sideload,
    DirectDownload,
    Dev,
}

/// <summary>
/// The version and install channel Diagnostics reports. Implemented in the App, where the package
/// identity and Velopack are visible; Core only consumes it so the snapshot tells the truth about
/// the running binary instead of the assembly it happens to carry (#52).
/// </summary>
public interface IInstallIdentity
{
    /// <summary>The version the About page shows (<c>major.minor.build</c>, no leading v).</summary>
    string Version { get; }

    InstallChannel Channel { get; }
}
