using System.Runtime.InteropServices;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Reads another process's command line via <c>NtQueryInformationProcess</c> with
/// <c>ProcessCommandLineInformation</c> (60), on a handle opened with only
/// <c>PROCESS_QUERY_LIMITED_INFORMATION</c> so it works without elevation for same-user processes.
/// Plain P/Invoke rather than CsWin32: ntdll's NT calls are not in the Win32 metadata.
/// <b>Never throws.</b> Any failure (access denied, exited process, bad status) returns null, and
/// callers treat null as "cannot classify" (#241).
/// </summary>
public static class ProcessCommandLine
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferOverflow = unchecked((int)0x80000005);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);

    public static string? TryRead(int pid)
    {
        IntPtr handle = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out var needed);
            if (status != StatusInfoLengthMismatch && status != StatusBufferOverflow && status != StatusBufferTooSmall)
            {
                return null;
            }
            if (needed < (uint)(IntPtr.Size * 2) || needed > 1_000_000)
            {
                return null;
            }

            buffer = Marshal.AllocHGlobal((int)needed);
            status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, needed, out _);
            if (status < 0)
            {
                return null;
            }

            // UNICODE_STRING: USHORT Length; USHORT MaximumLength; (pad on x64) PWSTR Buffer.
            int length = (ushort)Marshal.ReadInt16(buffer, 0);
            var strPtr = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            if (strPtr == IntPtr.Zero || length == 0)
            {
                return null;
            }
            return Marshal.PtrToStringUni(strPtr, length / 2);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); }
            if (handle != IntPtr.Zero) { CloseHandle(handle); }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int infoClass, IntPtr info, uint infoLength, out uint returnLength);
}
