using System.Diagnostics;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Production <see cref="IPluginProcessStarter"/> backed by <see cref="Process.Start"/>.
/// Wires <see cref="Process.Exited"/> per-plugin so the supervisor can detect crash /
/// kill / graceful exit uniformly. The PID -> plugin id mapping is owned upstream by
/// <see cref="PluginProcessSupervisor"/>; this layer only knows about PIDs.
/// </summary>
public sealed class DefaultPluginProcessStarter : IPluginProcessStarter
{
    public event Action<int>? ProcessExited;

    public int Start(string pluginId, string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) throw new ArgumentException("exePath is required.", nameof(exePath));
        if (!System.IO.File.Exists(exePath)) throw new System.IO.FileNotFoundException("Plugin executable not found.", exePath);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty,
        };
        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };

        // Capture pid in a closure so the Exited handler can fire it back; reading
        // process.Id after the Exited event raised on the OS thread is safe.
        process.Exited += (_, _) =>
        {
            int pid;
            try { pid = process.Id; }
            catch { pid = -1; }
            try { ProcessExited?.Invoke(pid); }
            catch { /* never let a subscriber's throw take the host down */ }
            try { process.Dispose(); } catch { }
        };

        process.Start();
        return process.Id;
    }

    public void Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
            // Already exited — process id no longer maps to a live process.
        }
        catch (InvalidOperationException)
        {
            // Race: process exited between the GetProcessById and HasExited check.
        }
        catch
        {
            // Last-ditch swallow; v1 doesn't escalate Kill failures (the supervisor
            // strips the mapping unconditionally on next process-exit signal).
        }
    }
}
