namespace ROROROblox.Core;

/// <summary>
/// The one place that decides where RoRoRo keeps its own data: <c>%LOCALAPPDATA%\ROROROblox</c>.
/// <para>
/// Every path under that folder (accounts, settings, logs, caches, plugins, sentinels) is built
/// through <see cref="Folder"/> or <see cref="Combine"/>, and a source fence
/// (<c>AppDataRootFenceTests</c>) fails if a second piece of code resolves
/// <c>SpecialFolder.LocalApplicationData</c> for it. The Roblox client's own folders
/// (<c>%LOCALAPPDATA%\Roblox</c>, <c>%LOCALAPPDATA%\Packages</c>) are not RoRoRo's and do not go
/// through here; the fence allow-lists those by file and count.
/// </para>
/// <para>
/// <see cref="Folder"/> is resolved on every read and never cached in a <c>static readonly</c>
/// field, so a value derived from it at type initialisation cannot freeze a path before whatever
/// configures this class has run. Callers should follow the same rule: a property or a method,
/// not a static field, for anything derived from the root.
/// </para>
/// </summary>
public static class AppDataRoot
{
    /// <summary>The folder name under <c>%LOCALAPPDATA%</c>. An identifier, not the brand.</summary>
    private const string FolderName = "ROROROblox";

    /// <summary>RoRoRo's data folder. Resolved through <c>Environment.GetFolderPath</c> on every read.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    /// <summary>A path under <see cref="Folder"/>. No parts returns the folder itself.</summary>
    public static string Combine(params string[] parts) => Path.Combine([Folder, .. parts]);
}
