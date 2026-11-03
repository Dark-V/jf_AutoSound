namespace SidecarBridge.Core;

public sealed record ScopeSnapshot(string[] Videos, string[] Sidecars, Dictionary<string, long> DirectoryStamps, string[] Warnings)
{
    public bool IsCurrent() => DirectoryStamps.All(p => Directory.Exists(p.Key) && Directory.GetLastWriteTimeUtc(p.Key).Ticks == p.Value);
}
public static class Discovery
{
    public static ScopeSnapshot Read(string root, string scope, BridgeSettings settings, CancellationToken ct)
    {
        PathSafety.Validate(root, scope);
        var videos = new List<string>();
        var tracks = new List<string>();
        var warnings = new List<string>();
        var stamps = new Dictionary<string, long>(PathSafety.Comparer);
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((scope, 0));
        int count = 0;
        while (stack.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            PathSafety.Validate(root, directory.Path);
            stamps[directory.Path] = Directory.GetLastWriteTimeUtc(directory.Path).Ticks;
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path).Order(PathSafety.Comparer))
                {
                    ct.ThrowIfCancellationRequested();
                    if (++count > Math.Clamp(settings.MaximumEntries, 1, 200000)) throw new IOException("Scope entry limit exceeded; no partial plan was applied. Narrow the search scope.");
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { warnings.Add("Skipped symlink/junction: " + path); continue; }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (directory.Depth < Math.Clamp(settings.MaximumDepth, 0, 12)) stack.Push((path, directory.Depth + 1));
                        else warnings.Add("Depth limit: " + path);
                    }
                    else
                    {
                        var extension = Path.GetExtension(path).ToLowerInvariant();
                        if (extension is ".mkv" or ".mp4") videos.Add(path);
                        else if ((settings.EnableAudio && extension == ".mka") ||
                            (settings.EnableSubtitles && extension is ".ass" or ".ssa" or ".srt" or ".vtt")) tracks.Add(path);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
            { warnings.Add("Skipped inaccessible directory: " + directory.Path); }
        }
        return new(videos.ToArray(), tracks.ToArray(), stamps, warnings.ToArray());
    }
}
