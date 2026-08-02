using System.Diagnostics;

namespace ROROROblox.Core.Diagnostics;

public sealed class ProcessMemoryProbe : IProcessMemoryProbe
{
    public bool TryReadPrivateBytes(int pid, out long privateBytes)
    {
        privateBytes = 0;
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Refresh();
            privateBytes = p.PrivateMemorySize64;
            return true;
        }
        catch
        {
            // Exited, access denied, or gone mid-read. UNKNOWN — never a zero reading.
            return false;
        }
    }
}
