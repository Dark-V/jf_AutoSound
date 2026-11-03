using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SidecarBridge.Core;

public static partial class NativeFiles
{
    public sealed record Identity(ulong Device, ulong Number, uint DeviceMinor = 0);
    public static Identity GetIdentity(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(handle, out var info)) ThrowNative("File identity");
            return new(info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow);
        }
        if (OperatingSystem.IsLinux())
        {
            if (Statx(-100, path, 0x100, 0x100, out var info) != 0) ThrowNative("statx identity");
            if ((info.Mask & 0x100) == 0 || (info.Mode & 0xf000) != 0x8000) throw new IOException("Not a regular file or inode identity unavailable.");
            return new(info.DevMajor, info.Inode, info.DevMinor);
        }
        throw new PlatformNotSupportedException("Hardlink identity is implemented for Windows and Linux only.");
    }
    public static bool Same(string first, string second) => GetIdentity(first) == GetIdentity(second);
    private static void ThrowNative(string operation)
    {
        var error = Marshal.GetLastPInvokeError();
        throw new IOException($"{operation}: {new Win32Exception(error).Message} (native error {error}). Hardlinks require the same filesystem, write permission and filesystem support; no copy/symlink fallback.");
    }
    public static void CreateLink(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLink(destination, source, IntPtr.Zero)) ThrowNative("CreateHardLink");
        }
        else if (OperatingSystem.IsLinux())
        {
            if (Link(source, destination) != 0) ThrowNative("link");
        }
        else throw new PlatformNotSupportedException("Only Windows and Linux hardlinks are implemented.");
    }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string destination, string source, IntPtr security);
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Link(string source, string destination);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsInfo info);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsInfo
    {
        public uint Attributes;
        public uint CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, ModifiedLow, ModifiedHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    // Linux's statx ABI is identical on x64 and ARM64; libc stat layouts are not.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxInfo
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DevMajor;
        [FieldOffset(140)] public uint DevMinor;
    }
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Statx(int dirfd, string path, int flags, uint mask, out StatxInfo info);
}
