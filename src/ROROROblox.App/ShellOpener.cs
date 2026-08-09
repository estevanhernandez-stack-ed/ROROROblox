using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ROROROblox.Core;

namespace ROROROblox.App;

/// <summary>Shell-open via <c>UseShellExecute</c>. Mirrors what App.xaml.cs did inline before F-001.</summary>
internal sealed class ShellOpener : IShellOpener
{
    private readonly ILogger<ShellOpener>? _log;

    public ShellOpener(ILogger<ShellOpener>? log = null) => _log = log;

    public void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Couldn't open {Path}", path);
        }
    }
}
