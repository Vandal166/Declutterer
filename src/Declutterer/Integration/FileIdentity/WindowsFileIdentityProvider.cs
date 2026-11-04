using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Declutterer.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace Declutterer.Integration.FileIdentity;

/// <summary>
/// Windows file identity: the volume serial number plus the 64-bit file index from GetFileInformationByHandle. It survives a rename
/// or a move within the volume and changes when an item is deleted and recreated. The handle is opened for metadata only
/// (no access rights, reparse points not followed), so nothing is read and no last-access time moves.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileIdentityProvider : IFileIdentityProvider
{
    private const uint ShareAll = 0x1 | 0x2 | 0x4;          // read | write | delete: never block anyone else
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;         // needed to open a directory
    private const uint OpenReparsePoint = 0x00200000;        // identify the link itself, do not follow it

    public bool IsSupported => true;

    public string? TryGetFileId(string path)
    {
        try
        {
            using var handle = CreateFile(path, 0, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
                return null;
            if (!GetFileInformationByHandle(handle, out var info))
                return null;
            return $"{info.VolumeSerialNumber:X8}:{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
}
