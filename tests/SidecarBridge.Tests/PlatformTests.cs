using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using SidecarBridge.Core;
using Xunit;

namespace SidecarBridge.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows filesystem test"; }
}
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Linux filesystem test"; }
}
public sealed class PlatformTests
{
    [WindowsFact, SupportedOSPlatform("windows")]
    public async Task DeniedCreateFilesIsReportedWithoutChangingSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "bridge-permission-" + Guid.NewGuid().ToString("N"));
        var dir = Directory.CreateDirectory(Path.Combine(root, "Video"));
        var audio = Directory.CreateDirectory(Path.Combine(root, "Audio", "Studio"));
        var video = Path.Combine(dir.FullName, "Film.mkv"); File.WriteAllText(video, "video");
        var source = Path.Combine(audio.FullName, "Film.mka"); File.WriteAllText(source, "audio"); File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-2));
        var acl = dir.GetAccessControl(); var original = dir.GetAccessControl();
        try
        {
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles, AccessControlType.Deny)); dir.SetAccessControl(acl);
            var result = await new BridgeEngine(Path.Combine(root, "state")).ProcessAsync(root, root, video, new(), new object(), false, default);
            Assert.Empty(result.ChangedPaths); Assert.Contains(result.Entries, e => e.Status == "Skipped" && e.Reason.Contains("native error 5", StringComparison.Ordinal));
            Assert.Equal("audio", File.ReadAllText(source));
        }
        finally { dir.SetAccessControl(original); Directory.Delete(root, true); }
    }
    [LinuxFact, SupportedOSPlatform("linux")]
    public void DifferentFilesystemsDoNotFallBackToCopy()
    {
        var root = Directory.CreateTempSubdirectory("bridge-device-");
        var other = Directory.CreateDirectory(Path.Combine("/dev/shm", "bridge-device-" + Guid.NewGuid().ToString("N")));
        try
        {
            var source = Path.Combine(root.FullName, "source.mka"); File.WriteAllText(source, "source");
            var destination = Path.Combine(other.FullName, "link.mka");
            Assert.Throws<IOException>(() => NativeFiles.CreateLink(source, destination));
            Assert.False(File.Exists(destination)); Assert.Equal("source", File.ReadAllText(source));
        }
        finally { root.Delete(true); other.Delete(true); }
    }
    [LinuxFact, SupportedOSPlatform("linux")]
    public void ReadOnlyDirectoryDoesNotFallBackToCopy()
    {
        var root = Directory.CreateTempSubdirectory("bridge-readonly-");
        var source = Path.Combine(root.FullName, "source.mka"); File.WriteAllText(source, "source");
        var target = Directory.CreateDirectory(Path.Combine(root.FullName, "target"));
        try
        {
            File.SetUnixFileMode(target.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Throws<IOException>(() => NativeFiles.CreateLink(source, Path.Combine(target.FullName, "link.mka")));
            Assert.Equal("source", File.ReadAllText(source));
        }
        finally { File.SetUnixFileMode(target.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); root.Delete(true); }
    }
}
