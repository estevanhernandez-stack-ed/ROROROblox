namespace ROROROblox.App.Plugins;

public sealed record InstalledPlugin
{
    public required PluginManifest Manifest { get; init; }
    public required string InstallDir { get; init; }
    public required ConsentRecord Consent { get; init; }

    public string ExecutablePath => System.IO.Path.Combine(InstallDir, Manifest.Id + ".exe");
}
