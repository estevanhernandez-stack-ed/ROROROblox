using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace ROROROblox.Core.Diagnostics;

public sealed class SystemMemoryProbe : ISystemMemoryProbe
{
    public bool TryRead(out long totalPhysicalBytes, out long availablePhysicalBytes)
    {
        totalPhysicalBytes = 0;
        availablePhysicalBytes = 0;
        try
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!PInvoke.GlobalMemoryStatusEx(ref status))
            {
                return false;
            }
            totalPhysicalBytes = (long)status.ullTotalPhys;
            availablePhysicalBytes = (long)status.ullAvailPhys;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
