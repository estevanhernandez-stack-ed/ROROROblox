namespace ROROROblox.Core;

/// <summary>
/// Opens a path with the OS shell. Exists so "Open log folder" is assertable in a test and so no
/// view-model test launches Explorer on a CI runner.
/// </summary>
public interface IShellOpener
{
    /// <summary>
    /// Best-effort. Implementations swallow and log rather than throw: failing to open a folder is
    /// never worth taking the app down.
    /// </summary>
    void Open(string path);
}
