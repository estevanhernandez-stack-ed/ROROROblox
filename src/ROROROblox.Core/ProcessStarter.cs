using System.Diagnostics;

namespace ROROROblox.Core;

/// <summary>
/// Default <see cref="IProcessStarter"/> backed by <see cref="Process.Start(ProcessStartInfo)"/>.
/// </summary>
public sealed class ProcessStarter : IProcessStarter
{
    public int StartViaShell(string fileNameOrUri)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileNameOrUri,
            UseShellExecute = true,
        };
        var process = Process.Start(info);
        return process?.Id ?? 0;
    }
}
