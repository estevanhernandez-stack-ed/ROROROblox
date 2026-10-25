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
    private readonly PluginJobObject? _job;

    /// <param name="job">Ties every started plugin to the host's lifetime (F-101). Null keeps
    /// the pre-v1.21 behaviour, where a plugin outlived the session that launched it.</param>
    public DefaultPluginProcessStarter(
        ILogger<DefaultPluginProcessStarter>? log = null,
        PluginJobObject? job = null)
    {
        _log = log;
        _job = job;
    }

    public event Action<int>? ProcessExited;

    /// <summary>
    /// The launch's <see cref="ProcessStartInfo"/>, split out so the environment contract is
    /// assertable without starting a process (v1.33 item 8). A real child cannot be asked what its
    /// environment was — <see cref="IPluginProcessStarter.Start"/> takes no arguments to pass a
    /// probe, and reading another process's environment is not something to build a test on.
    /// </summary>
    internal static ProcessStartInfo BuildStartInfo(string exePath, PluginLaunchReason reason)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            // LOAD-BEARING FOR THE ENVIRONMENT, not only for CreateNoWindow: ProcessStartInfo
            // throws on Start when Environment has been populated and UseShellExecute is true.
            // The two cannot both be had, and this is the one that keeps the variable.
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty,
        };
        psi.Environment[PluginLaunchReasonExtensions.EnvironmentVariableName] = reason.ToWireValue();
        return psi;
    }

    public int Start(string pluginId, string exePath, PluginLaunchReason reason)
    {
        if (string.IsNullOrEmpty(exePath)) throw new ArgumentException("exePath is required.", nameof(exePath));
        if (!System.IO.File.Exists(exePath))
        {
            _log?.LogError(
                "Plugin executable not found: {PluginId} ({ExePath}) — broken install or quarantined by antivirus?",
                pluginId, exePath);
            throw new System.IO.FileNotFoundException("Plugin executable not found.", exePath);
        }

        // Launch by where the exe really lives, never by a virtualized path (#242). On a Store-only
        // install the two differ and only the real one works if the child lands outside the package.
        var launchPath = OnDiskPath.Resolve(exePath);
        if (!string.Equals(launchPath, exePath, StringComparison.OrdinalIgnoreCase))
        {
            _log?.LogInformation(
                "Plugin {PluginId} resolves to {LaunchPath} on disk (requested {ExePath}); launching by the on-disk path.",
                pluginId, launchPath, exePath);
        }

        var psi = BuildStartInfo(launchPath, reason);
        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };

        // The Exited handler disposes the Process, so it must not run until Start has read the pid
        // (#242). A plugin that dies before Start returns used to fire Exited first: the handler
        // disposed, then process.Id threw "No process is associated with this object" and the
        // install reported a failed start. Seen in the field when Start stalled 14 s on a first run.
        // The gate is bounded so a path that never opens it cannot pin a thread-pool thread forever.
        // Never call process.WaitForExit inside Start: it raises Exited on the calling thread, which
        // would block here on a gate only Start can open.
        var pidRead = new ManualResetEventSlim(false);
        var pid = -1;
        var startedUtc = DateTime.UtcNow;
        process.Exited += (_, _) =>
        {
            pidRead.Wait(TimeSpan.FromSeconds(10));
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
            pidRead.Dispose();
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Plugin process failed to start: {PluginId} ({ExePath}).", pluginId, exePath);
            pidRead.Dispose();
            throw;
        }

        try
        {
            AfterStartForTest?.Invoke(process);
            pid = process.Id;
            // Assign AFTER Start — a process must exist to join a job. The window between the two is
            // the one case this cannot cover: a host death in those microseconds still strands the
            // child, which is why the startup sweep exists rather than being redundant.
            var adopted = _job?.TryAssign(pid) ?? false;

            _log?.LogInformation(
                "Plugin process started: {PluginId} pid {Pid} ({ExePath}); reason: {Reason}; job-managed: {Adopted}.",
                pluginId, pid, launchPath, reason.ToWireValue(), adopted);
            return pid;
        }
        finally
        {
            // Opened after the started line, so a plugin that dies at launch logs "started" then "exited",
            // never the reverse.
            pidRead.Set();
        }
    }

    /// <summary>
    /// Test seam: runs between <c>Process.Start</c> and reading the pid. Lets a test hold Start at the
    /// exact point the field stall did (#242), so the race is deterministic rather than occasional.
    /// </summary>
    internal Action<Process>? AfterStartForTest { get; set; }

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
        // Both spellings of the folder (#242): plugins launch by the on-disk path, but callers hand in the
        // path the host sees, which on a Store-only install is the virtual one. Matching only that would
        // report nothing running, and the uninstall/update wait would delete files under a live plugin.
        var prefixes = new[] { Path.GetFullPath(dirPath), Path.GetFullPath(OnDiskPath.Resolve(dirPath)) }
            .Select(p => p.EndsWith(Path.DirectorySeparatorChar) ? p : p + Path.DirectorySeparatorChar)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hits = new List<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var image = process.MainModule?.FileName;
                if (image is not null)
                {
                    var full = Path.GetFullPath(image);
                    if (prefixes.Any(p => full.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        hits.Add(process.Id);
                    }
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
