using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Production <see cref="IPluginProcessStarter"/> backed by <see cref="Process.Start"/>.
/// Wires <see cref="Process.Exited"/> per-plugin so the supervisor can detect crash /
/// kill / graceful exit uniformly. The PID -> plugin id mapping is owned upstream by
/// <see cref="PluginProcessSupervisor"/>; this layer only knows about PIDs.
///
/// This is also the only place the host ever learns a plugin's exit code — the plugin's
/// own log starts at its App.OnStartup, so launch failures and pre-managed-code deaths
/// (missing EXE, AV quarantine, hostfxr on a broken extraction) are visible ONLY here.
/// 0xE0434352 = managed unhandled exception; hostfxr codes = broken install; exit 0 at
/// sub-second uptime = clean self-refusal. Issue #36.
/// </summary>
public sealed class DefaultPluginProcessStarter : IPluginProcessStarter
{
    private readonly ILogger<DefaultPluginProcessStarter>? _log;

    public DefaultPluginProcessStarter(ILogger<DefaultPluginProcessStarter>? log = null)
    {
        _log = log;
    }

    public event Action<int>? ProcessExited;

    public int Start(string pluginId, string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) throw new ArgumentException("exePath is required.", nameof(exePath));
        if (!System.IO.File.Exists(exePath))
        {
            _log?.LogError(
                "Plugin executable not found: {PluginId} ({ExePath}) — broken install or quarantined by antivirus?",
                pluginId, exePath);
            throw new System.IO.FileNotFoundException("Plugin executable not found.", exePath);
        }

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
        var startedUtc = DateTime.UtcNow;
        process.Exited += (_, _) =>
        {
            int pid;
            try { pid = process.Id; }
            catch { pid = -1; }
            // ExitCode is valid once Exited has fired; read it before Dispose. The hex
            // form is what discriminates crash class (0xE0434352 = managed exception).
            var exitCodeHex = "unknown";
            try { exitCodeHex = "0x" + process.ExitCode.ToString("X8"); } catch { }
            _log?.LogInformation(
                "Plugin process exited: {PluginId} pid {Pid} code {ExitCodeHex} after {UptimeSeconds:F1}s.",
                pluginId, pid, exitCodeHex, (DateTime.UtcNow - startedUtc).TotalSeconds);
            try { ProcessExited?.Invoke(pid); }
            catch { /* never let a subscriber's throw take the host down */ }
            try { process.Dispose(); } catch { }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Plugin process failed to start: {PluginId} ({ExePath}).", pluginId, exePath);
            throw;
        }
        _log?.LogInformation(
            "Plugin process started: {PluginId} pid {Pid} ({ExePath}).",
            pluginId, process.Id, exePath);
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

    public IReadOnlyList<int> FindRunningUnder(string dirPath)
    {
        var prefix = Path.GetFullPath(dirPath);
        if (!prefix.EndsWith(Path.DirectorySeparatorChar)) prefix += Path.DirectorySeparatorChar;

        var hits = new List<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var image = process.MainModule?.FileName;
                if (image is not null &&
                    Path.GetFullPath(image).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(process.Id);
                }
            }
            catch
            {
                // MainModule throws for processes we can't query (access denied, exited
                // mid-enumeration, bitness mismatch). A plugin process runs as the same
                // user, non-elevated — those are queryable. Anything that throws isn't ours.
            }
            finally
            {
                process.Dispose();
            }
        }
        return hits;
    }
}
