using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.JobObjects;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// A Windows Job Object that kills every plugin process when the host's handle closes — including
/// when the host CRASHES or is killed from Task Manager.
/// <para>
/// WHY THIS EXISTS (F-101). Windows does not terminate children when a parent dies. RoRoRo autostarts
/// plugins at launch (<c>App.xaml.cs</c>) and had nothing that stopped them at exit, so every session
/// left one live plugin process behind and they accumulated: <b>six <c>626labs.ur-task.exe</c>
/// processes were observed running, holding roughly 950 MB, with RoRoRo itself not running at
/// all.</b> On a product whose pitch is running several Roblox clients side by side, and which ships
/// a memory watchdog to protect exactly that headroom, orphans eating it silently is the product
/// undermining its own core claim.
/// </para>
/// <para>
/// WHY A JOB OBJECT AND NOT A SHUTDOWN HOOK. A hook — <c>Application.Exit</c>, <c>ProcessExit</c>,
/// a <c>finally</c> — only runs when the host exits in an orderly way, and the cases that stranded
/// these processes are precisely the disorderly ones: a crash, a kill, a debugger stop, a machine
/// losing power. <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> is enforced by the KERNEL when the last
/// handle to the job closes, which happens on process death however that death arrives. It is the
/// only mechanism that covers the failure it was built for.
/// </para>
/// <para>
/// DELIBERATELY NOT FATAL. Every failure path here degrades to today's behaviour — an unparented
/// plugin process — rather than blocking a launch. A plugin that starts without job membership is a
/// leak; a plugin that refuses to start because job assignment failed is a broken feature. The leak
/// is also swept at startup by <c>PluginProcessSupervisor.SweepOrphans</c>, which is the belt to
/// this brace and the thing that clears a pile made before this shipped.
/// </para>
/// </summary>
public sealed class PluginJobObject : IDisposable
{
    private readonly ILogger? _log;
    private readonly SafeFileHandle? _job;
    private bool _disposed;

    /// <summary>True when the kernel is enforcing kill-on-close. False means every spawn degrades
    /// to an unparented process and the startup sweep is the only cleanup.</summary>
    public bool IsActive => _job is { IsInvalid: false };

    public PluginJobObject(ILogger? log = null)
    {
        _log = log;

        try
        {
            // Unnamed: this job belongs to this host instance only. A named job would be shared
            // across concurrent RoRoRo instances, and one exiting would kill the other's plugins.
            var handle = PInvoke.CreateJobObject((Windows.Win32.Security.SECURITY_ATTRIBUTES?)null, (string?)null);
            if (handle is null || handle.IsInvalid)
            {
                _log?.LogWarning(
                    "Could not create the plugin job object; plugin processes will not be tied to this "
                    + "host's lifetime and will rely on the startup orphan sweep instead.");
                return;
            }

            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            unsafe
            {
                var ok = PInvoke.SetInformationJobObject(
                    new Windows.Win32.Foundation.HANDLE(handle.DangerousGetHandle()),
                    JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                    &limits,
                    (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));

                if (!ok)
                {
                    _log?.LogWarning(
                        "Could not set KILL_ON_JOB_CLOSE on the plugin job object (win32 {Error}); "
                        + "plugins will not be tied to this host's lifetime.",
                        System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    handle.Dispose();
                    return;
                }
            }

            _job = handle;
            _log?.LogInformation("Plugin job object active — plugin processes die with this host.");
        }
        catch (Exception ex)
        {
            // Including DllNotFoundException / EntryPointNotFound on a surface that does not have
            // the API. Degrade, never block a launch.
            _log?.LogWarning(ex, "Plugin job object unavailable; falling back to the startup orphan sweep.");
        }
    }

    /// <summary>
    /// Puts <paramref name="pid"/> under the job. Best-effort by contract: a false return means the
    /// process runs unparented, which is what every plugin did before this existed.
    /// </summary>
    public bool TryAssign(int pid)
    {
        if (_job is null || _job.IsInvalid) return false;

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            using var handle = new SafeFileHandle(process.Handle, ownsHandle: false);

            if (PInvoke.AssignProcessToJobObject(_job, handle)) return true;

            _log?.LogWarning(
                "Could not assign plugin pid {Pid} to the job object (win32 {Error}); it will not be "
                + "killed with the host.",
                pid, System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            return false;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Could not assign plugin pid {Pid} to the job object.", pid);
            return false;
        }
    }

    /// <summary>
    /// Closes the job handle, which is what triggers the kernel's kill-on-close. Not required for
    /// correctness — process death closes it anyway, which is the entire point — but an orderly
    /// shutdown should not wait for the process to die.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _job?.Dispose();
    }
}
