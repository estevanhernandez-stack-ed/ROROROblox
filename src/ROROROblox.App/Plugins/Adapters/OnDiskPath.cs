using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Resolves a path to where the file actually lives on disk, as the filesystem reports it for an open
/// handle (<c>GetFinalPathNameByHandle</c>). Issue #242.
///
/// Why this exists: on a Store-only install, MSIX redirects new paths under <c>%LOCALAPPDATA%</c> into the
/// package's <c>LocalCache</c>, so the plugins folder the host sees as
/// <c>%LOCALAPPDATA%\ROROROblox\plugins</c> really lives at
/// <c>…\Packages\&lt;family&gt;\LocalCache\Local\ROROROblox\plugins</c>. The virtual path only resolves
/// inside the package context. A plugin started by it that ends up outside that context cannot find its
/// own exe, and the .NET apphost exits <c>0x80008085</c> (CoreHostCurHostFindFailure) within a second.
/// Reproduced 2026-10-25: the same plugin ran normally from the real path inside and outside the package.
/// Measured on the same day that, inside the package, this API returns the LocalCache path.
///
/// It also follows junctions and symlinks, which is what the tests use: a package context cannot be built
/// in a unit test, a junction can, and both are the same question to this API.
///
/// Never throws. Anything it cannot resolve comes back unchanged, so an unpackaged install, a missing
/// path or a filesystem that refuses the query behaves exactly as before.
/// </summary>
internal static class OnDiskPath
{
    public static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        try
        {
            // FILE_FLAG_BACKUP_SEMANTICS is what lets CreateFile open a directory; harmless on a file.
            using var handle = CreateFileW(
                path, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid) return path;

            var buffer = new StringBuilder(512);
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length >= buffer.Capacity)
            {
                // The first call returns the size needed when the buffer was too small.
                buffer = new StringBuilder((int)length + 1);
                length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            }
            if (length == 0 || length >= buffer.Capacity) return path;

            return StripDevicePrefix(buffer.ToString());
        }
        catch
        {
            return path;
        }
    }

    /// <summary>The API answers in <c>\\?\</c> form; everything else in the host speaks plain paths.</summary>
    internal static string StripDevicePrefix(string finalPath)
    {
        if (finalPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + finalPath[8..];
        if (finalPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            return finalPath[4..];
        return finalPath;
    }

    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x1 | 0x2 | 0x4; // read | write | delete
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);
}
